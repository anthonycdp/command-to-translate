# Ollama Startup Dependency Design

**Status**: Approved
**Date**: 2026-03-24

## Summary

Ensure the translation model is loaded and ready before the application attempts to use it at Windows startup. This is accomplished by having the installer orchestrate the entire dependency chain: Ollama installation, model download, and scheduled task creation for startup preloading.

## Problem

Race condition at Windows logon:
1. Application starts via startup entry (registry Run or scheduled task)
2. Ollama service may not be fully initialized
3. Model `translategemma` is not loaded in memory
4. Translation attempts fail with connection errors

Current mitigation (health checks every 30s) is reactive and causes poor user experience during the first translations after system boot.

## Solution Overview

Shift from reactive (wait for errors) to proactive (ensure dependencies ready):

1. **Installer** verifies/installs Ollama, downloads model, creates scheduled tasks
2. **Scheduled tasks** orchestrate startup order: load model first, then start app with delay

```
[Windows Logon]
      │
      ├─► Task: LoadTranslateGemma (immediate)
      │         └─► ollama run translategemma (loads model to memory)
      │
      └─► Task: StartCommandToTranslate (30s delay)
                └─► CommandToTranslate.exe
                          └─► Model ready, translations work immediately
```

## Requirements

1. Ollama installed automatically if not present
2. Model `translategemma` downloaded during installation
3. Scheduled task preloads model at every logon
4. Application starts 30 seconds after logon
5. Graceful error handling if Ollama installation fails
6. User can opt-out of automatic startup

## Implementation

### File: `installer/CommandToTranslate.iss`

#### 1. Add Ollama installation step

Create a Pascal function to check and install Ollama. Uses PowerShell for downloading (no external dependencies):

```iss
[Code]
const
  OLLAMA_INSTALLER_URL = 'https://ollama.com/download/OllamaSetup.exe';

function IsOllamaInstalled: Boolean;
var
  ResultCode: Integer;
begin
  // Exec returns True if execution succeeded; ResultCode is output parameter
  if Exec('cmd', '/c ollama --version', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0)
  else
    Result := False;
end;

function InstallOllama: Boolean;
var
  InstallerPath: string;
  ResultCode: Integer;
begin
  InstallerPath := ExpandConstant('{tmp}\OllamaSetup.exe');
  Result := False;

  // Download Ollama installer using PowerShell (no external dependencies)
  if Exec('powershell',
          '-NoProfile -Command "Invoke-WebRequest -Uri ''' + OLLAMA_INSTALLER_URL + ''' -OutFile ''' + InstallerPath + '''"',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if ResultCode <> 0 then
    begin
      Log('Failed to download Ollama installer: PowerShell returned ' + IntToStr(ResultCode));
      Exit;
    end;
  end
  else
  begin
    Log('Failed to execute PowerShell for download');
    Exit;
  end;

  // Run Ollama installer silently
  if Exec(InstallerPath, '/S', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := (ResultCode = 0);
    if Result then
      Log('Ollama installed successfully')
    else
      Log('Ollama installation failed with code: ' + IntToStr(ResultCode));
  end
  else
  begin
    Log('Failed to execute Ollama installer');
  end;
end;
```

#### 2. Add model download and preload step

```iss
function DownloadAndLoadModel: Boolean;
var
  ResultCode: Integer;
begin
  Result := False;

  // Pull model (download if not present)
  Log('Downloading translategemma model...');
  if Exec('cmd', '/c ollama pull translategemma', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if ResultCode <> 0 then
    begin
      Log('Model pull failed with code: ' + IntToStr(ResultCode));
      Exit;
    end;
  end
  else
  begin
    Log('Failed to execute ollama pull');
    Exit;
  end;

  // Load model to memory using echo to terminate stdin
  // This warms up the model for first use while keeping it loaded for 5 minutes
  Log('Preloading model into memory...');
  if Exec('cmd', '/c echo. | ollama run translategemma --keepalive 5m', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := (ResultCode = 0);
    if Result then
      Log('Model preloaded successfully')
    else
      Log('Model preload failed with code: ' + IntToStr(ResultCode));
  end;
end;
```

#### 3. Create scheduled tasks for startup

Replace the registry Run entry with scheduled tasks. Respects user's choice for startup:

```iss
procedure CreateStartupTasks;
var
  AppPath: string;
  ResultCode: Integer;
begin
  AppPath := ExpandConstant('{app}\command-to-translate.exe');

  // Task 1: Load model at logon (immediate)
  // Uses cmd wrapper for proper command execution
  if Exec('schtasks',
          '/Create /TN "LoadTranslateGemma" /TR "cmd /c echo. | ollama run translategemma --keepalive 5m" /SC ON_LOGON /RL HIGHEST /F',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if ResultCode = 0 then
      Log('LoadTranslateGemma task created successfully')
    else
      Log('Failed to create LoadTranslateGemma task: ' + IntToStr(ResultCode));
  end;

  // Task 2: Start app at logon (30 second delay)
  // Only create if user selected startup option
  if WizardIsTaskSelected('startup') then
  begin
    if Exec('schtasks',
            '/Create /TN "StartCommandToTranslate" /TR "' + AppPath + '" /SC ON_LOGON /DELAY 00:00:30 /F',
            '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      if ResultCode = 0 then
        Log('StartCommandToTranslate task created successfully')
      else
        Log('Failed to create StartCommandToTranslate task: ' + IntToStr(ResultCode));
    end;
  end;

  // Remove old registry startup entry if exists (migration from older versions)
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CommandToTranslate');
end;
```

#### 4. Hook into installation sequence

**Important**: `CurStepChanged` is a **procedure**, not a function:

```iss
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Step 1: Ensure Ollama is installed
    if not IsOllamaInstalled then
    begin
      if not InstallOllama then
      begin
        MsgBox('Ollama installation failed. Please install Ollama manually from ollama.com',
               mbError, MB_OK);
        Exit;
      end;
    end;

    // Step 2: Download and load model
    if not DownloadAndLoadModel then
    begin
      Log('Model setup failed - app will show error on first translation attempt');
    end;

    // Step 3: Create startup tasks
    CreateStartupTasks;
  end;
end;
```

#### 5. Update [Tasks] section

Keep the `startup` task for user choice (checked by default):

```iss
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "startup"; Description: "Start with Windows"; GroupDescription: "Additional options:"; Flags: checkedonce
Name: "launchapp"; Description: "Launch command-to-translate"; GroupDescription: "Post-install:"; Flags: checkedonce
```

#### 6. Add uninstall cleanup

```iss
[UninstallRun]
; Remove scheduled tasks on uninstall
Filename: "schtasks"; Parameters: "/Delete /TN ""LoadTranslateGemma"" /F"; Flags: runhidden
Filename: "schtasks"; Parameters: "/Delete /TN ""StartCommandToTranslate"" /F"; Flags: runhidden

[UninstallDelete]
; Clean up any leftover files
Type: filesandordirs; Name: "{app}"
```

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Ollama not installed | Download via PowerShell and install silently. If download fails, show error with manual install link. |
| Model download fails | Log error, continue installation. App will show error on first translation attempt. |
| Scheduled task creation fails | Log error. User can manually create task or run app normally. |
| Ollama service slow to start | 30s app delay provides buffer. Health check catches edge cases. |
| User unchecks "Start with Windows" | Only LoadTranslateGemma task is created (model preload). App task is skipped. |

## Trade-offs

- **30-second fixed delay**: Simpler than dynamic detection, but may be insufficient on very slow machines. Users can manually adjust the scheduled task delay if needed.
- **PowerShell for download**: Adds dependency on PowerShell (available on Windows 7+), but avoids bundling third-party Inno Setup plugins.
- **Task Scheduler instead of registry Run**: Slightly more complex, but provides delay capability and better control over startup order.
- **echo. | for stdin termination**: Works reliably to terminate interactive session while keeping model loaded.

## Files Changed

| File | Changes |
|------|---------|
| `installer/CommandToTranslate.iss` | Add Ollama installation, model download, scheduled tasks, cleanup |

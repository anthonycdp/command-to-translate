# Ollama Startup Dependency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Modify the Inno Setup installer to automatically install Ollama, download the translation model, and create scheduled tasks for proper startup ordering.

**Architecture:** The installer will check for Ollama and install if missing, download the `translategemma` model, and create two scheduled tasks: one to preload the model at logon (immediate), and one to start the app with a 30-second delay. This ensures the model is ready before the app tries to use it.

**Tech Stack:** Inno Setup 6.x, Pascal scripting, Windows Task Scheduler (`schtasks.exe`), PowerShell (for downloading)

---

## File Structure

| File | Action | Purpose |
|------|--------|---------|
| `installer/CommandToTranslate.iss` | Modify | Add Ollama install, model download, scheduled tasks |

---

## Task 1: Add Constants and Helper Functions

**Files:**
- Modify: `installer/CommandToTranslate.iss` (add to `[Code]` section)

- [ ] **Step 1: Add OLLAMA_INSTALLER_URL constant and IsOllamaInstalled function**

Add after the `[Code]` section header:

```iss
const
  OLLAMA_INSTALLER_URL = 'https://ollama.com/download/OllamaSetup.exe';

function IsOllamaInstalled: Boolean;
var
  ResultCode: Integer;
begin
  if Exec('cmd', '/c ollama --version', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0)
  else
    Result := False;
end;
```

- [ ] **Step 2: Commit**

```bash
git add installer/CommandToTranslate.iss
git commit -m "feat(installer): add Ollama detection constant and function"
```

---

## Task 2: Add Ollama Installation Function

**Files:**
- Modify: `installer/CommandToTranslate.iss` (add to `[Code]` section)

- [ ] **Step 1: Add InstallOllama function**

Add after `IsOllamaInstalled`:

```iss
function InstallOllama: Boolean;
var
  InstallerPath: string;
  ResultCode: Integer;
begin
  InstallerPath := ExpandConstant('{tmp}\OllamaSetup.exe');
  Result := False;

  // Download Ollama installer using PowerShell
  Log('Downloading Ollama installer...');
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
  Log('Running Ollama installer...');
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

- [ ] **Step 2: Commit**

```bash
git add installer/CommandToTranslate.iss
git commit -m "feat(installer): add Ollama installation function with PowerShell download"
```

---

## Task 3: Add Model Download and Preload Function

**Files:**
- Modify: `installer/CommandToTranslate.iss` (add to `[Code]` section)

- [ ] **Step 1: Add DownloadAndLoadModel function**

Add after `InstallOllama`:

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

- [ ] **Step 2: Commit**

```bash
git add installer/CommandToTranslate.iss
git commit -m "feat(installer): add model download and preload function"
```

---

## Task 4: Add Scheduled Tasks Creation Function

**Files:**
- Modify: `installer/CommandToTranslate.iss` (add to `[Code]` section)

- [ ] **Step 1: Add CreateStartupTasks procedure**

Add after `DownloadAndLoadModel`:

```iss
procedure CreateStartupTasks;
var
  AppPath: string;
  ResultCode: Integer;
begin
  AppPath := ExpandConstant('{app}\command-to-translate.exe');

  // Task 1: Load model at logon (immediate)
  Log('Creating LoadTranslateGemma scheduled task...');
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
    Log('Creating StartCommandToTranslate scheduled task...');
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

- [ ] **Step 2: Commit**

```bash
git add installer/CommandToTranslate.iss
git commit -m "feat(installer): add scheduled tasks creation for startup orchestration"
```

---

## Task 5: Update CurStepChanged Hook

**Files:**
- Modify: `installer/CommandToTranslate.iss` (replace existing `CurStepChanged`)

- [ ] **Step 1: Replace existing CurStepChanged with new implementation**

Replace the existing `CurStepChanged` procedure (lines 59-66) with:

```iss
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then
    exit;

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
```

- [ ] **Step 2: Commit**

```bash
git add installer/CommandToTranslate.iss
git commit -m "feat(installer): integrate Ollama setup into installation sequence"
```

---

## Task 6: Remove Registry Startup Entry

**Files:**
- Modify: `installer/CommandToTranslate.iss` (remove from `[Registry]` section)

- [ ] **Step 1: Remove the registry Run entry from [Registry] section**

Delete the registry entry from the `[Registry]` section:

```iss
; DELETE THIS LINE:
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "command-to-translate"; ValueData: """{app}\command-to-translate.exe"""; Flags: uninsdeletevalue; Tasks: startup
```

The `[Registry]` section will now be empty. You can remove the entire section header if desired, or leave it empty for future use.

The `[Registry]` section will now be empty. You can remove the entire section header if desired, or leave it empty for future use.

- [ ] **Step 2: Commit**

```bash
git add installer/CommandToTranslate.iss
git commit -m "refactor(installer): remove registry Run entry, replaced by scheduled task"
```

---

## Task 7: Add Uninstall Cleanup

**Files:**
- Modify: `installer/CommandToTranslate.iss` (add sections)

- [ ] **Step 1: Add [UninstallRun] section for scheduled task cleanup**

Add at the end of the file:

```iss
[UninstallRun]
; Remove scheduled tasks on uninstall
Filename: "schtasks"; Parameters: "/Delete /TN ""LoadTranslateGemma"" /F"; Flags: runhidden
Filename: "schtasks"; Parameters: "/Delete /TN ""StartCommandToTranslate"" /F"; Flags: runhidden
```

- [ ] **Step 2: Commit**

```bash
git add installer/CommandToTranslate.iss
git commit -m "feat(installer): add uninstall cleanup for scheduled tasks"
```

---

## Task 8: Build and Test Installer

**Files:**
- Test: Manual installation test

- [ ] **Step 1: Build the installer**

```bash
.\scripts\Build-Installer.ps1
```

Expected: Installer created at `artifacts\installer\command-to-translate-setup-<version>.exe`

- [ ] **Step 2: Test installation on a clean system (or VM)**

1. Run the installer
2. Verify Ollama is installed (if not already)
3. Verify model is downloaded (`ollama list`)
4. Verify scheduled tasks exist:
   ```powershell
   schtasks /Query /TN "LoadTranslateGemma"
   schtasks /Query /TN "StartCommandToTranslate"
   ```

- [ ] **Step 3: Test startup sequence**

1. Log off and log back on
2. Wait 30+ seconds
3. Verify app started (system tray icon)
4. Verify translation works immediately

- [ ] **Step 4: Test uninstall**

1. Run uninstaller
2. Verify scheduled tasks are removed:
   ```powershell
   schtasks /Query /TN "LoadTranslateGemma"  # Should fail
   schtasks /Query /TN "StartCommandToTranslate"  # Should fail
   ```

- [ ] **Step 5: Commit final version if tests pass**

```bash
git add installer/CommandToTranslate.iss
# No changes expected, just verification
```

---

## Summary

| Task | Description |
|------|-------------|
| 1 | Add constants and `IsOllamaInstalled` function |
| 2 | Add `InstallOllama` function |
| 3 | Add `DownloadAndLoadModel` function |
| 4 | Add `CreateStartupTasks` procedure |
| 5 | Update `CurStepChanged` hook |
| 6 | Remove registry Run entry |
| 7 | Add uninstall cleanup |
| 8 | Build and test |

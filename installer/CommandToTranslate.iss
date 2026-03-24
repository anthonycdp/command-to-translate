#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{D7CBE97D-88BC-4A5B-B8D7-C1A2F2E6A9E4}
AppName=command-to-translate
AppVersion={#AppVersion}
AppPublisher=command-to-translate
DefaultDirName={localappdata}\Programs\command-to-translate
DefaultGroupName=command-to-translate
OutputDir={#OutputDir}
OutputBaseFilename=command-to-translate-setup-{#AppVersion}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\command-to-translate.exe

[Languages]
Name: "english";           MessagesFile: "compiler:Default.isl"
Name: "brazilian";         MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "spanish";           MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french";            MessagesFile: "compiler:Languages\French.isl"
Name: "german";            MessagesFile: "compiler:Languages\German.isl"
Name: "japanese";          MessagesFile: "compiler:Languages\Japanese.isl"
Name: "italian";           MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "startup"; Description: "Start with Windows"; GroupDescription: "Additional options:"; Flags: checkedonce
Name: "launchapp"; Description: "Launch command-to-translate"; GroupDescription: "Post-install:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\command-to-translate"; Filename: "{app}\command-to-translate.exe"
Name: "{autodesktop}\command-to-translate"; Filename: "{app}\command-to-translate.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\command-to-translate.exe"; Description: "Launch command-to-translate"; Flags: nowait postinstall skipifsilent; Tasks: launchapp

[Code]
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
  end
  else
  begin
    Log('Failed to execute ollama run');
  end;
end;

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
  end
  else
  begin
    Log('Failed to execute schtasks for LoadTranslateGemma');
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
    end
    else
    begin
      Log('Failed to execute schtasks for StartCommandToTranslate');
    end;
  end;

  // Remove old registry startup entry if exists (migration from older versions)
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CommandToTranslate');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then
    exit;

  // Step 1: Ensure Ollama is installed
  Log('Checking Ollama installation...');
  if not IsOllamaInstalled then
  begin
    Log('Ollama not found, installing...');
    if not InstallOllama then
    begin
      MsgBox('Ollama installation failed. Please install Ollama manually from ollama.com',
             mbError, MB_OK);
      Exit;
    end;
  end
  else
  begin
    Log('Ollama is already installed');
  end;

  // Step 2: Download and load model
  if not DownloadAndLoadModel then
  begin
    Log('Model setup failed - app will show error on first translation attempt');
  end;

  // Step 3: Create startup tasks
  CreateStartupTasks;
end;

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
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start with Windows"; GroupDescription: "Additional options:"; Flags: checkedonce
Name: "launchapp"; Description: "Launch command-to-translate"; GroupDescription: "Post-install:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\command-to-translate"; Filename: "{app}\command-to-translate.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "command-to-translate"; ValueData: """{app}\command-to-translate.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\command-to-translate.exe"; Description: "Launch command-to-translate"; Flags: nowait postinstall skipifsilent; Tasks: launchapp

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then
    exit;

  if not WizardIsTaskSelected('startup') then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'command-to-translate');
end;

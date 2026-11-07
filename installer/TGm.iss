#ifndef MyAppVersion
  #define MyAppVersion "0.1.0-preview.1"
#endif
#ifndef SourceDir
  #define SourceDir "..\release\TGm-" + MyAppVersion + "-win-x64"
#endif

#define MyAppName "TGm"
#define MyAppPublisher "TGm contributors"
#define MyAppExeName "TGm.exe"

[Setup]
AppId={{F7F03A92-2814-4ED7-BD77-90927E398A2A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Copyright © 2026 TGm contributors
DefaultDirName={localappdata}\Programs\TGm
DefaultGroupName=TGm
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\release
OutputBaseFilename=TGm-{#MyAppVersion}-win-x64-setup
SetupIconFile=..\src\TimesGateMonitor.App\Assets\TGm.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=TGm.exe
RestartApplications=no
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Unofficial PC monitor for Divoom Times Gate

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\TGm"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\TGm"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,TGm}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Uninstall-TGm.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "TGmUserCleanup"

#define MyAppName "WoodStream TimeMachine"
#define MyAppPublisher "WoodStream"
#define MyAppExeName "WoodStreamTimeMachine.exe"
#ifndef MyAppArch
  #define MyAppArch "x64"
#endif
#if MyAppArch == "x64"
  #define MyAppAllowedArchitectures "x64compatible"
#elif MyAppArch == "arm64"
  #define MyAppAllowedArchitectures "arm64"
#else
  #error Unsupported MyAppArch. Use x64 or arm64.
#endif
#define MyAppPublishDir "publish\" + MyAppArch
#define MyAppVersion GetVersionNumbersString(MyAppPublishDir + "\" + MyAppExeName)

[Setup]
AppId={{E1A6392D-C3B2-4E52-9A4B-9B28442813DC}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=Installer
OutputBaseFilename=WoodStreamTimeMachine-Setup-{#MyAppVersion}-{#MyAppArch}
SetupIconFile=Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed={#MyAppAllowedArchitectures}
ArchitecturesInstallIn64BitMode={#MyAppAllowedArchitectures}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyAppPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

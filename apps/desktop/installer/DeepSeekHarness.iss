#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{18D197C9-1407-4D94-A7A1-65A979E0D30C}
AppName=DeepSeek Harness
AppVersion={#AppVersion}
AppPublisher=warm-maple
DefaultDirName={localappdata}\Programs\DeepSeek Harness
DefaultGroupName=DeepSeek Harness
DisableProgramGroupPage=yes
OutputDir=..\..\..\.artifacts\desktop
OutputBaseFilename=DeepSeekHarness-Setup-{#AppVersion}-x64
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
WizardStyle=modern
UninstallDisplayIcon={app}\DeepSeekHarness.exe
CloseApplications=yes
RestartApplications=no

[Files]
Source: "..\..\..\.artifacts\desktop\payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\DeepSeek Harness"; Filename: "{app}\DeepSeekHarness.exe"; WorkingDir: "{userdocs}"
Name: "{autodesktop}\DeepSeek Harness"; Filename: "{app}\DeepSeekHarness.exe"; WorkingDir: "{userdocs}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Run]
Filename: "{app}\DeepSeekHarness.exe"; Description: "启动 DeepSeek Harness"; Flags: nowait postinstall skipifsilent

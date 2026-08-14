#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif

[Setup]
AppId={{FA992AB0-6900-4952-A127-0908C364204D}
AppName=DeepSeek Harness (Web)
AppVersion={#AppVersion}
AppPublisher=warm-maple
DefaultDirName={localappdata}\Programs\DeepSeek Harness
DefaultGroupName=DeepSeek Harness
DisableProgramGroupPage=yes
OutputDir=..\..\..\.artifacts\desktop-web
OutputBaseFilename=DeepSeekHarnessWeb-Setup-{#AppVersion}-x64
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
WizardStyle=modern
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\DeepSeekHarnessWeb.exe
CloseApplications=yes
RestartApplications=no

[Files]
Source: "C:\dshpkg\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.map,*.d.ts,*.d.mts,*.d.cts,*.tsbuildinfo,*.snap,*.test.js,*.spec.js,*.md,*.markdown"

[Icons]
Name: "{autoprograms}\DeepSeek Harness"; Filename: "{app}\DeepSeekHarnessWeb.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\DeepSeek Harness"; Filename: "{app}\DeepSeekHarnessWeb.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Run]
Filename: "{app}\DeepSeekHarnessWeb.exe"; Description: "启动 DeepSeek Harness"; Flags: nowait postinstall skipifsilent

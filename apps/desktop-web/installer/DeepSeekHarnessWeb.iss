#ifndef AppVersion
  #define AppVersion "0.4.0"
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
Source: "..\..\..\.artifacts\desktop-web\payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "DeepSeekHarnessWeb.exe.WebView2\*,*.map,*.d.ts,*.d.mts,*.d.cts,*.tsbuildinfo,*.snap,*.test.js,*.spec.js,*.md,*.markdown"

[Icons]
Name: "{autoprograms}\DeepSeek Harness"; Filename: "{app}\DeepSeekHarnessWeb.exe"; WorkingDir: "{app}"; IconFilename: "{app}\app.ico"
Name: "{autodesktop}\DeepSeek Harness"; Filename: "{app}\DeepSeekHarnessWeb.exe"; WorkingDir: "{app}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Run]
Filename: "{app}\DeepSeekHarnessWeb.exe"; Description: "启动 DeepSeek Harness"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Upgrades from 0.2.x land in the previous {app}: purge the old runtime (and
  // the pre-0.2.1 WebView2 cache that lived beside the exe) so stale files
  // cannot linger beside the new payload. User data in %APPDATA% is untouched.
  if CurStep = ssInstall then
  begin
    DelTree(ExpandConstant('{app}\runtime'), True, True, True);
    DelTree(ExpandConstant('{app}\DeepSeekHarnessWeb.exe.WebView2'), True, True, True);
  end;
end;

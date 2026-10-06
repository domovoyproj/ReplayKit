#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
[Setup]
AppId={{716F4DA6-7EA4-45C0-AE9A-6434053EA0B4}
AppName=ReplayKit
AppVersion={#AppVersion}
AppPublisher=domovoyproj
AppPublisherURL=https://github.com/domovoyproj/ReplayKit
AppSupportURL=https://github.com/domovoyproj/ReplayKit/issues
DefaultDirName={localappdata}\Programs\ReplayKit
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DefaultGroupName=ReplayKit
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=ReplayKit-{#AppVersion}-Setup-x64
SetupIconFile=..\src\ReplayKit\Assets\replaykit.ico
UninstallDisplayIcon={app}\ReplayKit.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "autostart"; Description: "Запускать ReplayKit вместе с Windows"; Flags: checkedonce
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; Flags: unchecked
[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{userprograms}\ReplayKit"; Filename: "{app}\ReplayKit.exe"
Name: "{userdesktop}\ReplayKit"; Filename: "{app}\ReplayKit.exe"; Tasks: desktopicon
[Registry]
Root: HKCU; Subkey: "Software\ReplayKit"; ValueType: dword; ValueName: "Installed"; ValueData: "1"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ReplayKit"; ValueData: """{app}\ReplayKit.exe"" --background"; Flags: uninsdeletevalue; Tasks: autostart
[Run]
Filename: "{app}\ReplayKit.exe"; Parameters: "--installed"; Description: "Запустить ReplayKit"; Flags: nowait postinstall skipifsilent
[UninstallDelete]
Type: files; Name: "{localappdata}\ReplayKit\settings.json"
Type: files; Name: "{localappdata}\ReplayKit\settings.json.tmp"
Type: dirifempty; Name: "{localappdata}\ReplayKit"
[UninstallRun]
Filename: "{app}\ReplayKit.exe"; Parameters: "--quit"; Flags: runhidden waituntilterminated skipifdoesntexist
[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\ReplayKit.exe')) then
    Exec(ExpandConstant('{app}\ReplayKit.exe'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ReplayKit');
end;

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef BuildDir
  #define BuildDir "..\build"
#endif

[Setup]
AppId={{B344E694-C7AB-4F7D-A97F-F296EC5CC0A3}
AppName=Zephyr Remote
AppVersion={#AppVersion}
AppVerName=Zephyr Remote {#AppVersion}
AppPublisher=Zephyr
AppPublisherURL=https://github.com/JamilDbiat/ZephyrRemote
DefaultDirName={autopf}\Zephyr Remote
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
OutputDir={#BuildDir}
OutputBaseFilename=ZephyrRemoteSetup
SetupIconFile=icon.ico
UninstallDisplayIcon={app}\ZephyrRemote.exe
UninstallDisplayName=Zephyr Remote
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
CloseApplications=no

[Files]
Source: "{#BuildDir}\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "setup.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "uninstall.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "stop.ps1"; Flags: dontcopy

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\setup.ps1"" -App ""{app}"""; Flags: runhidden waituntilterminated; StatusMsg: "Starting Zephyr Remote..."

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\uninstall.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "ZephyrCleanup"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  ExtractTemporaryFile('stop.ps1');
  Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + ExpandConstant('{tmp}\stop.ps1') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

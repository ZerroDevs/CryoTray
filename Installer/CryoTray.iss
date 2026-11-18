; CryoTray installer script (Inno Setup 6)
; Build from the repository root with build-installer.ps1, or directly:
;   ISCC.exe /DAppVersion=0.0.100 Installer\CryoTray.iss

#ifndef AppVersion
#define AppVersion "0.0.100"
#endif

[Setup]
AppId={{8F3B9A12-4D6E-4C2B-9A1F-7E5D3C1B8A90}
AppName=CryoTray
AppVersion={#AppVersion}
AppVerName=CryoTray {#AppVersion}
AppPublisher=ZeroNuxi - ZerroDevs
AppPublisherURL=https://github.com/ZerroDevs/CryoTray
AppSupportURL=https://github.com/ZerroDevs/CryoTray/issues
AppUpdatesURL=https://github.com/ZerroDevs/CryoTray/releases
DefaultDirName={autopf}\CryoTray
DefaultGroupName=CryoTray
LicenseFile=..\LICENSE
SetupIconFile=..\Images\CryoTray.ico
UninstallDisplayIcon={app}\CryoTray.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=CryoTray-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; LICENSE, NOTICE and VERSION are copied explicitly below, so exclude them from the recursive copy to avoid duplicate destinations.
; Keep the full publish output for local debugging. The installer excludes framework language satellites
; CryoTray does not use and debugger-only runtime files. Remove a language folder from this list when it becomes supported.
; Do not add createallsubdirs: it recreates the excluded satellite folders as empty directories.
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb,LICENSE,NOTICE,VERSION,\cs\*,\de\*,\es\*,\fr\*,\it\*,\ja\*,\ko\*,\pl\*,\pt-BR\*,\ru\*,\tr\*,\zh-Hans\*,\zh-Hant\*,\mscordaccore.dll,\mscordaccore_amd64_amd64_*.dll,\mscordbi.dll,\Microsoft.DiaSymReader.Native.amd64.dll"

Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\VERSION"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Resources\PawnIO_setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[InstallDelete]
; Remove empty language folders left by earlier installers. dirifempty leaves non-empty folders alone.
Type: dirifempty; Name: "{app}\cs"
Type: dirifempty; Name: "{app}\de"
Type: dirifempty; Name: "{app}\es"
Type: dirifempty; Name: "{app}\fr"
Type: dirifempty; Name: "{app}\it"
Type: dirifempty; Name: "{app}\ja"
Type: dirifempty; Name: "{app}\ko"
Type: dirifempty; Name: "{app}\pl"
Type: dirifempty; Name: "{app}\pt-BR"
Type: dirifempty; Name: "{app}\ru"
Type: dirifempty; Name: "{app}\tr"
Type: dirifempty; Name: "{app}\zh-Hans"
Type: dirifempty; Name: "{app}\zh-Hant"

[Icons]
Name: "{group}\CryoTray"; Filename: "{app}\CryoTray.exe"
Name: "{autodesktop}\CryoTray"; Filename: "{app}\CryoTray.exe"; Tasks: desktopicon

[Run]
Filename: "{tmp}\PawnIO_setup.exe"; Parameters: "-install -silent"; StatusMsg: "Installing PawnIO hardware sensor driver..."; Flags: runhidden waituntilterminated; Check: not PawnIOInstalled
Filename: "{app}\CryoTray.exe"; Description: "{cm:LaunchProgram,CryoTray}"; Flags: shellexec nowait postinstall skipifsilent

[UninstallRun]
Filename: "powercfg.exe"; Parameters: "/setactive 381b4222-f694-41f0-9685-ff5bb260df2e"; Flags: runhidden; RunOnceId: "RestoreBalancedPowerPlan"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""CryoTray"" /F"; Flags: runhidden; RunOnceId: "DeleteStartupTask"

[Code]
function PawnIOInstalled: Boolean;
begin
  Result := RegKeyExists(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Services\PawnIO');
end;

procedure StopCryoTray;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM CryoTray.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup: Boolean;
begin
  StopCryoTray;
  Result := True;
end;

function InitializeUninstall: Boolean;
begin
  StopCryoTray;
  Result := True;
end;

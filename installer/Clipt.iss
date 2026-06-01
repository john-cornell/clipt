; Clipt - Inno Setup Installer Script
; Build (unsigned): iscc installer\Clipt.iss
; Build (signed): build-setup.bat (passes /DUSINGSIGNTOOL + /SCliptSign=... to ISCC)
; Requires: dotnet build src\Clipt\Clipt.csproj -c Release (run first)
; Plugin DLLs are built into src\Clipt\bin\Release\net8.0-windows\Plugins\ and installed to {app}\Plugins

#define MyAppName "Clipt"
#define MyAppVersion "1.14.3"
#define MyAppPublisher "Clipt"
#define MyAppExeName "Clipt.exe"

[Setup]
AppId={{B3F7E2A1-9C4D-4E8B-A6F0-1D2E3F4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=CliptSetup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=lowest
MinVersion=10.0
CloseApplications=force
CloseApplicationsFilter=*.exe,*.dll
#ifdef USINGSIGNTOOL
SignTool=CliptSign
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main application (Plugins subfolder packaged separately below)
Source: "..\src\Clipt\bin\Release\net8.0-windows\*"; Excludes: "Plugins\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Tray plugins — loaded at runtime from {app}\Plugins
Source: "..\src\Clipt\bin\Release\net8.0-windows\Plugins\*.dll"; DestDir: "{app}\Plugins"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
  end;
end;

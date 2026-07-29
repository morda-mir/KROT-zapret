#define MyAppName "KROT zapret"
#define MyAppVersion "1.0"
#define MyAppPublisher "KROT contributors"
#define MyAppExeName "KROT.exe"

[Setup]
AppId={{503BB53A-44CA-4754-AF87-3FD76B9894D1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf64}\KROT zapret
DefaultGroupName=KROT zapret
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=KROT-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\KROT.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"
Name: "autostart"; Description: "Запускать KROT при входе в Windows"; GroupDescription: "Автозапуск:"; Flags: unchecked

[Dirs]
Name: "{commonappdata}\KROT zapret\profiles"
Name: "{commonappdata}\KROT zapret\lists"
Name: "{commonappdata}\KROT zapret\cache"
Name: "{commonappdata}\KROT zapret\service-state"

[Files]
Source: "..\src\KROT.App\bin\x64\Release\net48\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\src\KROT.Service\bin\x64\Release\net48\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\third_party\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\THIRD_PARTY_NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "install-service.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "uninstall-service.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion

[Icons]
Name: "{group}\KROT zapret"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\KROT zapret"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "KROT zapret"; ValueData: """{app}\{#MyAppExeName}"" --autostart"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\tools\install-service.ps1"" -ServiceDirectory ""{app}\service"""; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить KROT zapret"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\tools\uninstall-service.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveKrotService"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  PowerShellPath: String;
  Parameters: String;
  ResultCode: Integer;
begin
  Result := '';
  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Parameters :=
    '-NoProfile -ExecutionPolicy Bypass -Command "' +
    '$service = Get-Service -Name ''KROTZapret'' -ErrorAction SilentlyContinue; ' +
    'if ($null -ne $service -and $service.Status -ne ''Stopped'') { ' +
    'Stop-Service -Name ''KROTZapret'' -Force; ' +
    '$service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped, ' +
    '[TimeSpan]::FromSeconds(15)) }"';
  if not Exec(
      PowerShellPath,
      Parameters,
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) then
  begin
    Result := 'Не удалось подготовить службу KROT к обновлению.';
    exit;
  end;

  if ResultCode <> 0 then
    Result := 'Не удалось остановить службу KROT перед обновлением.';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if MsgBox('Удалить пользовательские настройки и логи KROT?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}\KROT zapret'), True, True, True);
      DelTree(ExpandConstant('{commonappdata}\KROT zapret\service-state'), True, True, True);
    end;
  end;
end;

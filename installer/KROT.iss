#define MyAppName "KROT zapret"
#ifndef MyAppVersion
  #define MyAppVersion "1.0"
#endif
#ifndef MyOutputDir
  #define MyOutputDir "Output"
#endif
#define MyAppPublisher "morda-mir"
#define MyAppExeName "KROT.exe"
#define MyRepositoryUrl "https://github.com/morda-mir/KROT-zapret"

[Setup]
AppId={{503BB53A-44CA-4754-AF87-3FD76B9894D1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyRepositoryUrl}
AppSupportURL={#MyRepositoryUrl}/issues
AppUpdatesURL={#MyRepositoryUrl}/releases/latest
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf64}\KROT zapret
DefaultGroupName=KROT zapret
DisableProgramGroupPage=yes
MinVersion=10.0
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
PrivilegesRequired=admin
OutputDir={#MyOutputDir}
OutputBaseFilename=KROT-Setup-{#MyAppVersion}-x64
SetupIconFile=..\assets\tray\krot-tray-off.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\KROT.exe
AppMutex=Local\KROT-zapret-GUI-v1
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"; Flags: unchecked

[Files]
Source: "..\src\KROT.App\bin\x64\Release\net48\*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\src\KROT.Service\bin\x64\Release\net48\*"; DestDir: "{app}\service"; Excludes: "*.pdb,*.xml"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\third_party\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\THIRD_PARTY_NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "install-service.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "uninstall-service.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion

[Icons]
Name: "{group}\KROT zapret"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Удалить KROT zapret"; Filename: "{uninstallexe}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\KROT zapret"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить KROT zapret"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\tools\uninstall-service.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveKrotService"

[Code]
const
  NetFramework48Release = 528040;

function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(
    HKLM64,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
    'Release',
    Release) and (Release >= NetFramework48Release);

  if not Result then
    MsgBox(
      'Для KROT zapret требуется Microsoft .NET Framework 4.8.' + #13#10 +
      'Установите его с официального сайта Microsoft и повторите установку.',
      mbError,
      MB_OK);
end;

function HasCommandLineParameter(const Parameter: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
  begin
    if CompareText(ParamStr(Index), Parameter) = 0 then
    begin
      Result := True;
      exit;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  PowerShellPath: String;
  Parameters: String;
  ResultCode: Integer;
begin
  Result := '';
  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Parameters :=
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' +
    '$ErrorActionPreference = ''Stop''; ' +
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
    Result := 'Не удалось подготовить службу KROT к установке.';
    exit;
  end;

  if ResultCode <> 0 then
    Result := 'Не удалось остановить службу KROT перед установкой.';
end;

procedure InstallKrotService();
var
  PowerShellPath: String;
  Parameters: String;
  ResultCode: Integer;
begin
  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Parameters :=
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' +
    AddQuotes(ExpandConstant('{app}\tools\install-service.ps1')) +
    ' -ServiceDirectory ' +
    AddQuotes(ExpandConstant('{app}\service'));

  if not Exec(
      PowerShellPath,
      Parameters,
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) then
    RaiseException('Не удалось запустить установку службы KROT.');

  if ResultCode <> 0 then
    RaiseException(
      'Не удалось установить службу KROT. Код ошибки: ' +
      IntToStr(ResultCode) + '.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallKrotService();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if HasCommandLineParameter('/DELETEUSERDATA') or
       ((not HasCommandLineParameter('/KEEPUSERDATA')) and
        (MsgBox(
          'Удалить пользовательские настройки и логи KROT?',
          mbConfirmation,
          MB_YESNO) = IDYES)) then
    begin
      DelTree(ExpandConstant('{localappdata}\KROT zapret'), True, True, True);
      DelTree(
        ExpandConstant('{commonappdata}\KROT zapret\service-state'),
        True,
        True,
        True);
    end;
  end;
end;

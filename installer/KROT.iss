#define MyAppName "KROT zapret"
#define MyAppVersion "0.1.0-alpha"
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
Source: "..\third_party\zapret\*"; DestDir: "{app}\runtime"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\third_party\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\THIRD_PARTY_NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\KROT zapret"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\KROT zapret"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "KROT zapret"; ValueData: """{app}\{#MyAppExeName}"" --autostart"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{sys}\sc.exe"; Parameters: "create KROTZapret binPath= ""{app}\service\KROT.Service.exe"" start= auto obj= LocalSystem DisplayName= ""KROT zapret Service"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "description KROTZapret ""Локальная служба управления runtime KROT zapret"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start KROTZapret"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить KROT zapret"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop KROTZapret"; Flags: runhidden waituntilterminated; RunOnceId: "StopKrotService"
Filename: "{sys}\sc.exe"; Parameters: "delete KROTZapret"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteKrotService"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if MsgBox('Удалить пользовательские настройки и логи KROT?', mbConfirmation, MB_YESNO) = IDYES then
      DelTree(ExpandConstant('{localappdata}\KROT zapret'), True, True, True);
  end;
end;


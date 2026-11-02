; Скрипт установщика ShyFlow (Inno Setup 6.3 или новее).
; Собирается автоматически через build-all.bat. Версию и путь к exe подставляет build-all.ps1.

#ifndef AppVersion
  #define AppVersion "3.9.8"
#endif
; шаг 80: ShyFlow на .NET 10 и везёт .NET с собой (папка, а не один exe): ничего не скачивается, Windows не спрашивает прав администратора
#ifndef SourceDir
  #define SourceDir "out\dev"
#endif

[Setup]
; AppId НЕ МЕНЯТЬ: по нему Windows понимает, что это та же программа (обновление поверх, удаление).
AppId={{B7E2C1A4-5D3F-4E8A-9C61-2F0A7D4B8E15}
AppName=ShyFlow
AppVersion={#AppVersion}
AppPublisher=ShyFlow
; Ставим в папку пользователя (%LocalAppData%\Programs\ShyFlow): без прав администратора.
; Обновление: пользователь запускает новый ShyFlow-Setup.exe, он ставится поверх старой версии.
PrivilegesRequired=lowest
DefaultDirName={autopf}\ShyFlow
DefaultGroupName=ShyFlow
DisableProgramGroupPage=yes
; Имя без версии, чтобы ссылка .../releases/latest/download/ShyFlow-Setup.exe всегда работала
OutputDir=out\installer
OutputBaseFilename=ShyFlow-Setup
SetupIconFile=Island\ShyFlow.ico
UninstallDisplayIcon={app}\ShyFlow.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ShyFlow"; Filename: "{app}\ShyFlow.exe"
Name: "{autodesktop}\ShyFlow"; Filename: "{app}\ShyFlow.exe"; Tasks: desktopicon

[Registry]
; чистим автозапуск, который остров сам прописывает в настройках
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "ShyFlow"; ValueType: none; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\ShyFlow.exe"; Description: "{cm:LaunchProgram,ShyFlow}"; Flags: nowait postinstall skipifsilent
; автообновление из самого ShyFlow (шаг 55): тихая установка с /relaunch=1 запускает новую версию сама
Filename: "{app}\ShyFlow.exe"; Flags: nowait; Check: ShouldRelaunch

[Code]
function ShouldRelaunch: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:relaunch|0}') = '1');
end;

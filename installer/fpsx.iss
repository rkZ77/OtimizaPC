; Instalador do RKZFPS (Inno Setup 6). Gerado por installer/publish.ps1 -Installer.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "out\app"
#endif

[Setup]
; AppId fixo: e' o que faz a versao nova atualizar a instalacao existente em
; vez de instalar uma segunda copia. Nunca mudar.
AppId={{6B2E4F1A-8C3D-4E7B-9A51-FB5C2D0E7A13}
AppName=RKZFPS
AppVersion={#AppVersion}
AppPublisher=RKZFPS
AppPublisherURL=https://otimizapc-production.up.railway.app
AppSupportURL=https://otimizapc-production.up.railway.app/conta
DefaultDirName={autopf}\FPSX
DefaultGroupName=RKZFPS
DisableProgramGroupPage=yes
; A pasta do menu Iniciar era "FPSX": sem isto a atualizacao reusaria o nome antigo.
UsePreviousGroup=no
OutputBaseFilename=RKZFPS-Setup-{#AppVersion}
SetupIconFile=..\agent\src\Fpsx.App\Assets\fpsx.ico
UninstallDisplayIcon={app}\FPSX.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 2004 (build 19041) ou mais novo, o mesmo minimo do app.
MinVersion=10.0.19041
; Instala para todos os usuarios quando houver admin, ou so' para o usuario
; atual sem admin: o app em si nao exige admin para rodar.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"
Name: "autostart"; Description: "Iniciar com o Windows, na bandeja, para medir o FPS das partidas"; GroupDescription: "Medição automática:"

[Registry]
; Mesmo valor que o app grava em Partidas > Iniciar com o Windows, e que ele
; mesmo tira ao desmarcar. A desinstalação apaga sempre.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FPSX"; ValueData: """{app}\FPSX.exe"" --tray"; Tasks: autostart; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "FPSX"; Flags: dontcreatekey uninsdeletevalue

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Atalhos com o nome antigo (FPSX): a marca virou RKZFPS e a atualização
; nao pode deixar dois atalhos para o mesmo app. O executavel, a pasta e os
; dados continuam com o nome antigo de proposito: mudar quebraria o desfazer.
Type: files; Name: "{autodesktop}\FPSX.lnk"
Type: files; Name: "{autoprograms}\FPSX\FPSX.lnk"
Type: files; Name: "{autoprograms}\FPSX\Desinstalar FPSX.lnk"
Type: dirifempty; Name: "{autoprograms}\FPSX"

[Icons]
Name: "{group}\RKZFPS"; Filename: "{app}\FPSX.exe"
Name: "{group}\Desinstalar RKZFPS"; Filename: "{uninstallexe}"
Name: "{autodesktop}\RKZFPS"; Filename: "{app}\FPSX.exe"; Tasks: desktopicon

[Run]
; Atualização pelo próprio app roda o instalador em modo silencioso: no fim
; ele reabre o RKZFPS na sessão do usuário, que é o que a pessoa espera ver.
Filename: "{app}\FPSX.exe"; Flags: nowait runasoriginaluser; Check: WizardSilent
Filename: "{app}\FPSX.exe"; Description: "Abrir o RKZFPS e analisar meu PC"; Flags: nowait postinstall skipifsilent

[Messages]
ptbr.WelcomeLabel2=Isto vai instalar o RKZFPS {#AppVersion} no seu computador.%n%nO RKZFPS analisa seu PC e aplica só otimizações compatíveis, sempre com backup e opção de desfazer. A instalação não altera nenhuma configuração do Windows.

[Code]
// Desinstalar NAO apaga a pasta de dados: ela guarda os backups que permitem
// desfazer o que o RKZFPS alterou. O usuario e' avisado para desfazer antes.
function InitializeUninstall(): Boolean;
begin
  Result := MsgBox('Antes de desinstalar, recomendamos abrir o RKZFPS, ir em Histórico e usar "Desfazer tudo" para voltar as configurações ao estado anterior.' + #13#10#13#10 +
                   'Os backups ficam guardados em %LOCALAPPDATA%\FPSX mesmo após desinstalar.' + #13#10#13#10 +
                   'Continuar com a desinstalação?', mbConfirmation, MB_YESNO) = IDYES;
end;

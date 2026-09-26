; Instalador do FPSX (Inno Setup 6). Gerado por installer/publish.ps1 -Installer.

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
AppName=FPSX
AppVersion={#AppVersion}
AppPublisher=FPSX
AppPublisherURL=https://fpsx.up.railway.app
AppSupportURL=https://fpsx.up.railway.app/conta
DefaultDirName={autopf}\FPSX
DefaultGroupName=FPSX
DisableProgramGroupPage=yes
OutputBaseFilename=FPSX-Setup-{#AppVersion}
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

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\FPSX"; Filename: "{app}\FPSX.exe"
Name: "{group}\Desinstalar FPSX"; Filename: "{uninstallexe}"
Name: "{autodesktop}\FPSX"; Filename: "{app}\FPSX.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FPSX.exe"; Description: "Abrir o FPSX e analisar meu PC"; Flags: nowait postinstall skipifsilent

[Messages]
ptbr.WelcomeLabel2=Isto vai instalar o FPSX {#AppVersion} no seu computador.%n%nO FPSX analisa seu PC e aplica só otimizações compatíveis, sempre com backup e opção de desfazer. A instalação não altera nenhuma configuração do Windows.

[Code]
// Desinstalar NAO apaga a pasta de dados: ela guarda os backups que permitem
// desfazer o que o FPSX alterou. O usuario e' avisado para desfazer antes.
function InitializeUninstall(): Boolean;
begin
  Result := MsgBox('Antes de desinstalar, recomendamos abrir o FPSX, ir em Histórico e usar "Desfazer tudo" para voltar as configurações ao estado anterior.' + #13#10#13#10 +
                   'Os backups ficam guardados em %LOCALAPPDATA%\FPSX mesmo após desinstalar.' + #13#10#13#10 +
                   'Continuar com a desinstalação?', mbConfirmation, MB_YESNO) = IDYES;
end;

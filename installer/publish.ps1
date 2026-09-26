<#
.SYNOPSIS
  Publica o app FPSX e (opcional) gera o instalador.

.EXAMPLE
  ./installer/publish.ps1                      # so' publica em installer/out/app
  ./installer/publish.ps1 -Installer           # publica e gera FPSX-Setup-<versao>.exe
#>
param(
    [string]$Configuration = "Release",
    [switch]$Installer
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $PSScriptRoot "out"
$appOut = Join-Path $out "app"

# Versao unica do produto: a do Directory.Build.props do Agent.
[xml]$props = Get-Content (Join-Path $root "agent/Directory.Build.props")
$version = $props.Project.PropertyGroup.Version
Write-Host "FPSX $version"

if (Test-Path $appOut) { Remove-Item $appOut -Recurse -Force }

# Self-contained: o cliente nao precisa instalar .NET. Arquivo unico com as
# DLLs nativas embutidas; catalogo e perfis de jogo ficam soltos ao lado do
# .exe porque o admin publica versoes deles sem recompilar o app.
dotnet publish (Join-Path $root "agent/src/Fpsx.App/Fpsx.App.csproj") `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $appOut
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou" }

# PresentMon (Intel, licenca MIT): mede o FPS das partidas. Versao fixa e
# hash conferido: o instalador nunca empacota um binario que mudou sem a
# gente saber. Para atualizar, troque versao e hash juntos.
$pmVersion = "2.5.1"
$pmSha256 = "9bec3083069f58f911e6a512f4806db51a27bd096103087bc1d05ef54c80a191"
$pmCache = Join-Path $env:LOCALAPPDATA "FPSX-dev\tools\PresentMon-$pmVersion-x64.exe"
if (-not (Test-Path $pmCache)) {
    New-Item -ItemType Directory -Force (Split-Path $pmCache) | Out-Null
    Invoke-WebRequest "https://github.com/GameTechDev/PresentMon/releases/download/v$pmVersion/PresentMon-$pmVersion-x64.exe" -OutFile $pmCache -UseBasicParsing
}
$pmHash = (Get-FileHash $pmCache -Algorithm SHA256).Hash.ToLowerInvariant()
if ($pmHash -ne $pmSha256) { throw "PresentMon com hash inesperado ($pmHash). Nao empacotado." }
$pmSig = Get-AuthenticodeSignature $pmCache
if ($pmSig.Status -ne "Valid" -or $pmSig.SignerCertificate.Subject -notmatch "O=Intel Corporation") { throw "PresentMon sem assinatura valida da Intel. Nao empacotado." }
$tools = Join-Path $appOut "tools"
New-Item -ItemType Directory -Force $tools | Out-Null
Copy-Item $pmCache (Join-Path $tools "PresentMon.exe")
Copy-Item (Join-Path $PSScriptRoot "third-party\PresentMon-LICENSE.txt") (Join-Path $tools "PresentMon-LICENSE.txt")
Write-Host "PresentMon $pmVersion incluido (hash e assinatura Intel conferidos)"

if (-not $Installer) { return }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 nao encontrado. Instale com: winget install JRSoftware.InnoSetup" }

& $iscc "/DAppVersion=$version" "/DSourceDir=$appOut" "/O$out" (Join-Path $PSScriptRoot "fpsx.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup falhou" }

$setup = Join-Path $out "FPSX-Setup-$version.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Instalador: $setup"
Write-Host "SHA-256:    $hash   (cole no admin > Atualizacoes ao publicar)"

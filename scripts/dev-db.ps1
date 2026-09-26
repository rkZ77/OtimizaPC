<#
.SYNOPSIS
  Sobe um PostgreSQL local para desenvolvimento, sem Docker e sem instalar servico.

.DESCRIPTION
  Baixa os binarios portateis oficiais (EDB) uma vez para %LOCALAPPDATA%\FPSX-dev,
  cria o banco `fpsx` (dev) e `fpsx_test` (integracao) e liga na porta 54329.
  O backend/.env de desenvolvimento ja' aponta para ca'.

.EXAMPLE
  ./scripts/dev-db.ps1          # sobe (baixa na primeira vez)
  ./scripts/dev-db.ps1 -Stop    # desliga
#>
param([switch]$Stop)

$ErrorActionPreference = "Stop"
$base = Join-Path $env:LOCALAPPDATA "FPSX-dev"
$pg = Join-Path $base "pgsql"
$data = Join-Path $base "pgdata"
$log = Join-Path $base "postgres.log"
$port = 54329
$bin = Join-Path $pg "bin"

if ($Stop) {
    & (Join-Path $bin "pg_ctl.exe") -D $data stop -m fast
    return
}

if (-not (Test-Path (Join-Path $bin "postgres.exe"))) {
    New-Item -ItemType Directory -Force $base | Out-Null
    $zip = Join-Path $base "pg.zip"
    Write-Host "Baixando PostgreSQL 16 portatil (uma vez so', ~330 MB)..."
    $ProgressPreference = "SilentlyContinue"
    Invoke-WebRequest "https://get.enterprisedb.com/postgresql/postgresql-16.4-1-windows-x64-binaries.zip" -OutFile $zip
    Expand-Archive $zip -DestinationPath $base -Force
    Remove-Item $zip
}

if (-not (Test-Path (Join-Path $data "PG_VERSION"))) {
    # trust so' em localhost: e' um banco de desenvolvimento, sem dado real.
    & (Join-Path $bin "initdb.exe") -D $data -U fpsx -A trust -E UTF8 --no-locale | Out-Null
}

& (Join-Path $bin "pg_isready.exe") -h localhost -p $port | Out-Null
if ($LASTEXITCODE -ne 0) {
    # Start-Process: o pg_ctl segura a saida do console e travaria o terminal.
    Start-Process -FilePath (Join-Path $bin "pg_ctl.exe") -ArgumentList @("-D", "`"$data`"", "-l", "`"$log`"", "-o", "`"-p $port`"", "start") -WindowStyle Hidden
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 500
        & (Join-Path $bin "pg_isready.exe") -h localhost -p $port | Out-Null
        if ($LASTEXITCODE -eq 0) { break }
    }
}

foreach ($db in "fpsx", "fpsx_test") {
    $exists = & (Join-Path $bin "psql.exe") -h localhost -p $port -U fpsx -d postgres -tAc "select 1 from pg_database where datname='$db'"
    if ($exists -ne "1") {
        & (Join-Path $bin "psql.exe") -h localhost -p $port -U fpsx -d postgres -c "create database $db" | Out-Null
    }
}

Write-Host "PostgreSQL pronto em localhost:$port (bancos: fpsx, fpsx_test)"

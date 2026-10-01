<#
.SYNOPSIS
  Sobe um PostgreSQL local para desenvolvimento, sem Docker e sem instalar servico.

.DESCRIPTION
  Baixa os binarios portateis oficiais (EDB) uma vez para %LOCALAPPDATA%\RKZFPS-dev,
  cria o banco `rkzfps` (dev) e `rkzfps_test` (integracao) e liga na porta 54329.
  O backend/.env de desenvolvimento ja' aponta para ca'.

.EXAMPLE
  ./scripts/dev-db.ps1          # sobe (baixa na primeira vez)
  ./scripts/dev-db.ps1 -Stop    # desliga
#>
param([switch]$Stop)

$ErrorActionPreference = "Stop"
$base = Join-Path $env:LOCALAPPDATA "RKZFPS-dev"
$pg = Join-Path $base "pgsql"
$data = Join-Path $base "pgdata"
$log = Join-Path $base "postgres.log"
$port = 54329
$bin = Join-Path $pg "bin"

# Quando o projeto se chamava FPSX, tudo isto ficava em FPSX-dev, com o
# usuario e os bancos `fpsx`. Renomeia uma vez, sem baixar de novo nem perder
# os dados de desenvolvimento.
$legacy = Join-Path $env:LOCALAPPDATA "FPSX-dev"
$migrated = $false
if ((Test-Path $legacy) -and -not (Test-Path $base)) {
    $legacyBin = Join-Path $legacy "pgsql\bin"
    if (Test-Path (Join-Path $legacy "pgdata\postmaster.pid")) {
        & (Join-Path $legacyBin "pg_ctl.exe") -D (Join-Path $legacy "pgdata") stop -m fast | Out-Null
    }
    Rename-Item $legacy $base
    $migrated = $true
}

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
    & (Join-Path $bin "initdb.exe") -D $data -U rkzfps -A trust -E UTF8 --no-locale | Out-Null
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

$psql = Join-Path $bin "psql.exe"
if ($migrated) {
    # O cluster antigo foi criado com o superusuario `fpsx`, que nao pode ser
    # renomeado (e' o dono do bootstrap). Cria `rkzfps` ao lado e renomeia os bancos.
    & $psql -h localhost -p $port -U fpsx -d postgres -c "CREATE ROLE rkzfps SUPERUSER LOGIN" | Out-Null
    foreach ($db in "fpsx", "fpsx_test") {
        $exists = & $psql -h localhost -p $port -U rkzfps -d postgres -tAc "select 1 from pg_database where datname='$db'"
        if ($exists -eq "1") {
            & $psql -h localhost -p $port -U rkzfps -d postgres -c "ALTER DATABASE $db RENAME TO $($db.Replace('fpsx', 'rkzfps'))" | Out-Null
        }
    }
}

foreach ($db in "rkzfps", "rkzfps_test") {
    $exists = & $psql -h localhost -p $port -U rkzfps -d postgres -tAc "select 1 from pg_database where datname='$db'"
    if ($exists -ne "1") {
        & $psql -h localhost -p $port -U rkzfps -d postgres -c "create database $db" | Out-Null
    }
}

Write-Host "PostgreSQL pronto em localhost:$port (bancos: rkzfps, rkzfps_test)"

# Creates the limited PostgreSQL role and databases, and writes the secrets to
# src/ProgLab.Server/appsettings.Local.json (ignored by Git). No password is printed.
# psql asks for the postgres admin password itself; this script never sees it.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$localSettings = Join-Path $root "src\ProgLab.Server\appsettings.Local.json"
$psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"

function New-RandomSecret([int]$length) {
    $alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789"
    $bytes = New-Object byte[] $length
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
}

if (-not (Test-Path $psql)) {
    throw "psql was not found: $psql"
}

# Re-running keeps the existing passwords.
if (Test-Path $localSettings) {
    $existing = Get-Content $localSettings -Raw -Encoding UTF8 | ConvertFrom-Json
    $appPassword = $existing.Secrets.AppPassword
    $instructorPassword = $existing.Seed.InstructorTemporaryPassword
    if (-not $appPassword -or -not $instructorPassword) {
        throw "$localSettings exists but has an unexpected format. Move it aside and run again."
    }
} else {
    $appPassword = New-RandomSecret 32
    $instructorPassword = New-RandomSecret 12
}

$env:PROGLAB_APP_PASSWORD = $appPassword
try {
    Write-Host "psql will now ask for the PostgreSQL admin (postgres) password. Characters are not shown while typing."
    & $psql -h localhost -p 5432 -U postgres -d postgres -X -q -f (Join-Path $PSScriptRoot "setup-local-db.sql")
    if ($LASTEXITCODE -ne 0) {
        throw "psql failed with exit code $LASTEXITCODE. Nothing was written to appsettings.Local.json."
    }
} finally {
    Remove-Item Env:\PROGLAB_APP_PASSWORD -ErrorAction SilentlyContinue
}

$settings = [ordered]@{
    ConnectionStrings = [ordered]@{
        Default = "Host=localhost;Port=5432;Database=proglab;Username=proglab_app;Password=$appPassword"
        Test    = "Host=localhost;Port=5432;Database=proglab_test;Username=proglab_app;Password=$appPassword"
    }
    Seed    = [ordered]@{ InstructorTemporaryPassword = $instructorPassword }
    Secrets = [ordered]@{ AppPassword = $appPassword }
}
$settings | ConvertTo-Json -Depth 3 | Out-File -FilePath $localSettings -Encoding utf8

Write-Host "DONE: role and databases are ready. Secrets are in src\ProgLab.Server\appsettings.Local.json"

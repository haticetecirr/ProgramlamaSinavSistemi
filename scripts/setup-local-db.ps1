# Creates the limited PostgreSQL role and databases, and writes the secrets to appsettings.Local.json
# (ignored by Git): next to the server when run from a published copy, otherwise in src/ProgLab.Server.
# No password is printed. psql asks for the postgres admin password itself; this script never sees it.
# Running it without the settings file creates a new app password: if the role already exists on this
# computer, copy the existing appsettings.Local.json instead.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$sourceServer = Join-Path $root "src\ProgLab.Server"
$localSettings = if (Test-Path $sourceServer) { Join-Path $sourceServer "appsettings.Local.json" } else { Join-Path $root "appsettings.Local.json" }

# The newest installed PostgreSQL version is used.
$psql = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\psql.exe" -ErrorAction SilentlyContinue |
    Sort-Object { [int]$_.Directory.Parent.Name } -Descending |
    Select-Object -First 1 -ExpandProperty FullName

function New-RandomSecret([int]$length) {
    $alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789"
    $bytes = New-Object byte[] $length
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
}

if (-not $psql) {
    throw "psql was not found under C:\Program Files\PostgreSQL. Install PostgreSQL 16 or newer."
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

Write-Host "DONE: role and databases are ready. Secrets are in $localSettings"

# VT-37 load test. Starts a throwaway server on a temporary schema inside proglab_test, runs 60 virtual
# students against it, then removes the schema. The real proglab database is never touched.
# Stop any running ProgLab server first (port 5000). No password is printed.

param(
    [int]$Students = 60,
    [int]$Seconds = 60,
    [int]$Interval = 10,
    [string]$Psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$serverDir = Join-Path $root "src\ProgLab.Server"
$localSettings = Join-Path $serverDir "appsettings.Local.json"

if (-not (Test-Path $localSettings)) { throw "$localSettings not found. Run scripts\setup-local-db.ps1 first." }
if (-not (Test-Path $Psql)) { throw "psql was not found: $Psql. Pass it with -Psql." }
if (curl.exe -k -s --max-time 2 https://127.0.0.1:5000/api/health) { throw "Port 5000 is in use: stop the running ProgLab server first." }

$settings = Get-Content $localSettings -Raw -Encoding UTF8 | ConvertFrom-Json
$schema = "t_load_" + [guid]::NewGuid().ToString("N")
$work = Join-Path ([IO.Path]::GetTempPath()) "proglab-load-$schema"

function Invoke-TestSql($sql) {
    $env:PGPASSWORD = $settings.Secrets.AppPassword
    try { $sql | & $Psql -h localhost -U proglab_app -d proglab_test -X -A -t -q } finally { Remove-Item Env:\PGPASSWORD }
}

function New-TempSecret {
    $bytes = New-Object byte[] 24
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    "Yk-" + [Convert]::ToBase64String($bytes).Replace("/", "a").Replace("+", "b").Replace("=", "c")
}

dotnet build (Join-Path $root "src\ProgLab.Server") -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Server build failed." }
dotnet build (Join-Path $root "tests\ProgLab.LoadTest") -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Load test build failed." }

$instructorPassword = New-TempSecret
$jwtKey = New-Object byte[] 64
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($jwtKey)

New-Item -ItemType Directory -Force $work | Out-Null
Invoke-TestSql "CREATE SCHEMA $schema;"
$server = $null
$exitCode = 1
try {
    # Environment variables reach only the processes started below.
    $env:ASPNETCORE_ENVIRONMENT = "Testing"
    $env:ConnectionStrings__Default = "$($settings.ConnectionStrings.Test);Search Path=$schema"
    $env:Seed__InstructorTemporaryPassword = $instructorPassword
    $env:Jwt__SigningKey = [Convert]::ToBase64String($jwtKey)
    $env:RateLimiting__AuthPermitPerMinute = "100000"
    $env:Server__CertificatePath = Join-Path $work "server.pfx"
    $env:ExamLogs__Directory = Join-Path $work "logs"
    $env:Discovery__Enabled = "false"
    $server = Start-Process (Join-Path $serverDir "bin\Release\net10.0\ProgLab.Server.exe") -WorkingDirectory $work -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $work "server.out") -RedirectStandardError (Join-Path $work "server.err")

    $ready = $false
    foreach ($i in 1..60) {
        if (curl.exe -k -s --max-time 2 https://127.0.0.1:5000/api/health) { $ready = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "The test server did not start." }

    $env:PROGLAB_LOAD_INSTRUCTOR_PASSWORD = $instructorPassword
    & (Join-Path $root "tests\ProgLab.LoadTest\bin\Release\net10.0\ProgLab.LoadTest.exe") --students $Students --seconds $Seconds --interval $Interval
    $exitCode = $LASTEXITCODE

    # Sunucu hatalari silinmeden once gosterilir.
    Get-ChildItem $work -Filter "server.*" | Select-String -Pattern "Exception:|PostgresException|\[ERR\]|\[FTL\]| ERR | fail:" | Select-Object -First 6 | ForEach-Object { $_.Line }
} finally {
    foreach ($name in "ASPNETCORE_ENVIRONMENT", "ConnectionStrings__Default", "Seed__InstructorTemporaryPassword", "Jwt__SigningKey",
        "RateLimiting__AuthPermitPerMinute", "Server__CertificatePath", "ExamLogs__Directory", "Discovery__Enabled", "PROGLAB_LOAD_INSTRUCTOR_PASSWORD") {
        Remove-Item "Env:\$name" -ErrorAction SilentlyContinue
    }
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force -Confirm:$false }
    Start-Sleep -Milliseconds 500
    $ErrorActionPreference = "Continue"
    Invoke-TestSql "SET client_min_messages = warning; DROP SCHEMA $schema CASCADE;" | Out-Null
    Remove-Item $work -Recurse -Force -Confirm:$false -ErrorAction SilentlyContinue
}

exit $exitCode

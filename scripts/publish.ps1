# Builds self-contained copies that run without installing .NET. Output: artifacts\<runtime>\Server, InstructorApp, StudentApp.
# Secrets are not copied: see "Kurulum" in README.md. artifacts\ is ignored by Git.
#   .\publish.ps1                     Windows x64
#   .\publish.ps1 -Runtime linux-x64  other runtimes: osx-arm64, osx-x64, win-arm64

param([string]$Runtime = "win-x64")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "artifacts\$Runtime"

$apps = @{
    "Server"        = "src\ProgLab.Server"
    "InstructorApp" = "src\ProgLab.InstructorApp"
    "StudentApp"    = "src\ProgLab.StudentApp"
}

foreach ($name in $apps.Keys) {
    $target = Join-Path $output $name
    dotnet publish (Join-Path $root $apps[$name]) -c Release -r $Runtime --self-contained true -o $target --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $name" }

    $size = (Get-ChildItem $target -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
    Write-Host ("{0,-14} {1,5:0} MB  {2}" -f $name, $size, $target)
}

# The instructor computer also needs the setup scripts.
$scripts = Join-Path $output "Server\scripts"
New-Item -ItemType Directory -Force $scripts | Out-Null
foreach ($file in "setup-local-db.ps1", "setup-local-db.sql", "firewall-windows.ps1", "firewall-linux.sh", "firewall-student-windows.ps1") {
    Copy-Item (Join-Path $PSScriptRoot $file) $scripts -Force
}

# Published copies must not carry passwords. A file here can only come from a manual copy.
$secrets = Get-ChildItem $output -Recurse -File -Include "appsettings.Local.json", "*.pfx"
foreach ($secret in $secrets) {
    Write-Warning "Contains secrets, do not share this folder: $($secret.FullName)"
}

Write-Host "Done."

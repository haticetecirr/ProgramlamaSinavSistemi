# Allows the student app to hear server announcements: inbound UDP 47777.
# Optional: only needed if the server does not appear in the list. Typing the IP by hand works without this.
# Run on a STUDENT computer in an Administrator PowerShell. Safe to re-run.
# Remove the rule later with: .\firewall-student-windows.ps1 -Remove

param([switch]$Remove)

$ErrorActionPreference = "Stop"
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Open PowerShell with 'Run as administrator' and run this script again."
}

$name = "ProgLab Ogrenci Kesif (UDP 47777)"
netsh advfirewall firewall delete rule name="$name" | Out-Null
if ($Remove) {
    Write-Host "Removed: $name"
    return
}

netsh advfirewall firewall add rule name="$name" dir=in action=allow protocol=UDP localport=47777 profile=any | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Could not add rule: $name"
}

Write-Host "Added: $name"

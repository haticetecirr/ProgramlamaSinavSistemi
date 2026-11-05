# Allows students to reach the server on this computer: TCP 5000 (HTTPS) and UDP 47777 (discovery).
# Run on the INSTRUCTOR computer in an Administrator PowerShell. Safe to re-run.
# Remove the rules later with: .\firewall-windows.ps1 -Remove

param([switch]$Remove)

$ErrorActionPreference = "Stop"
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Open PowerShell with 'Run as administrator' and run this script again."
}

$rules = @(
    @{ Name = "ProgLab Sunucu HTTPS (TCP 5000)"; Protocol = "TCP"; Port = 5000 },
    @{ Name = "ProgLab Sunucu Kesfi (UDP 47777)"; Protocol = "UDP"; Port = 47777 }
)

foreach ($rule in $rules) {
    # Same name is deleted first so re-running does not create duplicates. Only these two ProgLab rules are touched.
    netsh advfirewall firewall delete rule name="$($rule.Name)" | Out-Null
    if ($Remove) {
        Write-Host "Removed: $($rule.Name)"
        continue
    }

    netsh advfirewall firewall add rule name="$($rule.Name)" dir=in action=allow protocol=$($rule.Protocol) localport=$($rule.Port) profile=any | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not add rule: $($rule.Name)"
    }

    Write-Host "Added: $($rule.Name)"
}

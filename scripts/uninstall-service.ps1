<#
.SYNOPSIS
    Stops and removes the Windows Service. Published files and logs are left in place.

.EXAMPLE
    .\scripts\uninstall-service.ps1
    .\scripts\uninstall-service.ps1 -ServiceName "AcmeSync"
#>
#Requires -RunAsAdministrator
param(
    [string]$ServiceName = "WindowsServiceStarter",
    [int]$StopTimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "Service '$ServiceName' is not installed"
    return
}

if ($service.Status -ne "Stopped") {
    Write-Host "Stopping '$ServiceName'..."
    Stop-Service -Name $ServiceName -Force
    $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds($StopTimeoutSeconds))
}

# Remove-Service needs PowerShell 6+, sc.exe works everywhere.
sc.exe delete $ServiceName | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe delete failed with code $LASTEXITCODE" }

Write-Host "Service '$ServiceName' removed" -ForegroundColor Green

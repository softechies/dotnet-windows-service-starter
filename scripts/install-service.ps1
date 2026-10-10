<#
.SYNOPSIS
    Installs the published exe as a Windows Service with automatic restart on failure.

.DESCRIPTION
    Run from an elevated PowerShell after scripts\publish.ps1.
    Recovery is set to restart after 5s, 30s and 60s, with the failure counter
    reset after one day. The worker exits with code 1 on fatal errors, which is
    what triggers these actions.

.EXAMPLE
    .\scripts\install-service.ps1
    .\scripts\install-service.ps1 -ServiceName "AcmeSync" -DisplayName "Acme Sync Service" -DelayedStart
    .\scripts\install-service.ps1 -Credential (Get-Credential)   # run under a specific account
#>
#Requires -RunAsAdministrator
param(
    [string]$ServiceName = "WindowsServiceStarter",
    [string]$DisplayName = "Windows Service Starter",
    [string]$Description = "Background worker built with the .NET Windows Service starter.",
    [string]$PublishDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "publish"),
    [string]$ExeName = "WindowsServiceStarter.exe",
    [switch]$DelayedStart,
    [System.Management.Automation.PSCredential]$Credential,
    [switch]$NoStart
)

$ErrorActionPreference = "Stop"

$exe = Join-Path $PublishDir $ExeName
if (-not (Test-Path $exe)) {
    throw "Could not find $exe. Run scripts\publish.ps1 first or pass -PublishDir."
}
$exe = (Resolve-Path $exe).Path

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "Service '$ServiceName' already exists. Run scripts\uninstall-service.ps1 first."
}

$params = @{
    Name           = $ServiceName
    BinaryPathName = "`"$exe`""
    DisplayName    = $DisplayName
    Description    = $Description
    StartupType    = "Automatic"
}
if ($Credential) { $params.Credential = $Credential }

New-Service @params | Out-Null
Write-Host "Created service '$ServiceName'"

if ($DelayedStart) {
    sc.exe config $ServiceName start= delayed-auto | Out-Null
    Write-Host "Startup type set to Automatic (Delayed Start)"
}

# Restart on crash: 5s, then 30s, then every 60s. Reset the counter after 24h.
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/30000/restart/60000 | Out-Null
# Also apply the actions when the process exits with a non zero code, not only on a crash.
sc.exe failureflag $ServiceName 1 | Out-Null
Write-Host "Recovery actions configured"

# Make sure the service account can write logs next to the exe.
$logDir = Join-Path $PublishDir "logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

if (-not $NoStart) {
    Start-Service -Name $ServiceName
    Write-Host "Service started" -ForegroundColor Green
}

Get-Service -Name $ServiceName | Format-Table Name, Status, StartType -AutoSize

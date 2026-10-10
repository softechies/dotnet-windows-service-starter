<#
.SYNOPSIS
    Publishes the service to the .\publish folder, ready to install.

.EXAMPLE
    .\scripts\publish.ps1
    .\scripts\publish.ps1 -SelfContained   # no .NET runtime needed on the target machine
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\WindowsServiceStarter\WindowsServiceStarter.csproj"
$output = Join-Path $root "publish"

$publishArgs = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "-o", $output,
    "--self-contained", $SelfContained.IsPresent.ToString().ToLower()
)
if ($SelfContained) { $publishArgs += "-p:PublishSingleFile=true" }

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Published to $output" -ForegroundColor Green

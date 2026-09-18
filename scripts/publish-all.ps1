param(
    [string]$ClientVersion = "1.6.8",
    [string]$ServerVersion = "1.6.8"
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot

Write-Host "==========================================================" -ForegroundColor Yellow
Write-Host " Building & Publishing Entire Platform Suite" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Yellow

& "$scriptDir\publish-client.ps1" -Version $ClientVersion
& "$scriptDir\publish-server.ps1" -Version $ServerVersion

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL RELEASES SUCCESSFULLY GENERATED" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green

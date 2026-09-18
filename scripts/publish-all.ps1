param(
    [string]$ClientVersion = "1.6.10",
    [string]$ServerVersion = "1.6.10"
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot

Write-Host "==========================================================" -ForegroundColor Yellow
Write-Host " Building & Publishing Entire Platform Suite" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Yellow

& "$scriptDir\publish-client.ps1" -Version $ClientVersion
& "$scriptDir\publish-server.ps1" -Version $ServerVersion

$rootDir = (Resolve-Path "$scriptDir\..").Path
$clientTarget = "$rootDir\SuperAutoMater\Releases\SuperAutoMater\v$ClientVersion"
$serverTarget = "$rootDir\SuperAutoMater\Releases\SuperManager\v$ServerVersion"

# Assemble unified release packages (both apps combined, matching historical v1.6.7 format)
$unifiedFolders = @(
    "$rootDir\SuperAutoMater\Releases\v$ClientVersion",
    "$rootDir\SuperAutoMater\Releases\Latest",
    "$rootDir\Releases\v$ClientVersion",
    "$rootDir\Releases\Latest"
)

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " Assembling Unified Release Bundles" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

foreach ($folder in $unifiedFolders) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    
    # Copy Client files
    Copy-Item -Path "$clientTarget\*" -Destination $folder -Recurse -Force
    
    # Copy Server files
    Copy-Item -Path "$serverTarget\*" -Destination $folder -Recurse -Force

    Write-Host "  - Bundled: $folder" -ForegroundColor White
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL RELEASES SUCCESSFULLY GENERATED & PUBLISHED" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green

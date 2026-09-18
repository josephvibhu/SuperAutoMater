param(
    [string]$Version = "1.6.10"
)

$ErrorActionPreference = "Stop"
$rootDir = (Resolve-Path "$PSScriptRoot\..").Path
$projectPath = "$rootDir\SuperAutoMater\SuperAutoMater\SuperAutoMater.csproj"
$targetLatest = "$rootDir\SuperAutoMater\Releases\SuperAutoMater\Latest"
$targetVersion = "$rootDir\SuperAutoMater\Releases\SuperAutoMater\v$Version"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Publishing SuperAutoMater (Client) v$Version" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Ensure clean directories
New-Item -ItemType Directory -Path $targetLatest -Force | Out-Null
New-Item -ItemType Directory -Path $targetVersion -Force | Out-Null

# Publish self-contained executable
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $targetLatest --nologo

# Copy supporting files
if (Test-Path "$rootDir\SuperAutoMater\SuperAutoMater\lib") {
    Copy-Item -Path "$rootDir\SuperAutoMater\SuperAutoMater\lib" -Destination "$targetLatest\lib" -Recurse -Force
}
if (Test-Path "$rootDir\Fix-Firewall.bat") {
    Copy-Item -Path "$rootDir\Fix-Firewall.bat" -Destination "$targetLatest\Fix-Firewall.bat" -Force
}
if (Test-Path "$rootDir\sheets_url.txt") {
    Copy-Item -Path "$rootDir\sheets_url.txt" -Destination "$targetLatest\sheets_url.txt" -Force
}

# Duplicate to specific version folder
Copy-Item -Path "$targetLatest\*" -Destination $targetVersion -Recurse -Force

Write-Host "[OK] Successfully published SuperAutoMater Client to:" -ForegroundColor Green
Write-Host "  - Latest:  $targetLatest" -ForegroundColor White
Write-Host "  - Version: $targetVersion" -ForegroundColor White

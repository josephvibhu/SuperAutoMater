param(
    [string]$Version = "1.6.8"
)

$ErrorActionPreference = "Stop"
$rootDir = (Resolve-Path "$PSScriptRoot\..").Path
$projectPath = "$rootDir\SuperAutoMater\SuperManager\SuperManager.csproj"
$targetLatest = "$rootDir\SuperAutoMater\Releases\SuperManager\Latest"
$targetVersion = "$rootDir\SuperAutoMater\Releases\SuperManager\v$Version"

Write-Host "==========================================================" -ForegroundColor Magenta
Write-Host " Publishing SuperManager (Server) v$Version" -ForegroundColor Magenta
Write-Host "==========================================================" -ForegroundColor Magenta

# Ensure clean directories
New-Item -ItemType Directory -Path $targetLatest -Force | Out-Null
New-Item -ItemType Directory -Path $targetVersion -Force | Out-Null

# Publish self-contained executable
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $targetLatest --nologo

# Copy supporting files
if (Test-Path "$rootDir\Fix-Firewall.bat") {
    Copy-Item -Path "$rootDir\Fix-Firewall.bat" -Destination "$targetLatest\Fix-Firewall.bat" -Force
}

# Duplicate to specific version folder
Copy-Item -Path "$targetLatest\*" -Destination $targetVersion -Recurse -Force

Write-Host "✓ Successfully published SuperManager Server to:" -ForegroundColor Green
Write-Host "  - Latest:  $targetLatest" -ForegroundColor White
Write-Host "  - Version: $targetVersion" -ForegroundColor White

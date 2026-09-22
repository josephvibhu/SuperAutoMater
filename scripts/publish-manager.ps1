# Publish VeloxManager as a standalone single-file Windows executable
param (
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "$PSScriptRoot/../Releases/VeloxManager"
)

Write-Host "Publishing VeloxManager ($Runtime) to $OutputDir..." -ForegroundColor Cyan

if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
}

dotnet publish "$PSScriptRoot/../VeloxManager/VeloxManager.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $OutputDir

if ($LASTEXITCODE -eq 0) {
    Write-Host "==========================================" -ForegroundColor Green
    Write-Host "VeloxManager published successfully!" -ForegroundColor Green
    Write-Host "Binary: $OutputDir/VeloxManager.exe" -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Green
} else {
    Write-Error "VeloxManager publication failed."
}

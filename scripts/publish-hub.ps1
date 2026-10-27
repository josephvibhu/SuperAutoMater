# Publish VeloxHub as a standalone single-file Windows executable
param (
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "$PSScriptRoot/../Releases/VeloxHub"
)

Write-Host "Publishing VeloxHub ($Runtime) to $OutputDir..." -ForegroundColor Cyan

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

dotnet publish "$PSScriptRoot/../VeloxHub/VeloxHub.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $OutputDir

if ($LASTEXITCODE -eq 0) {
    Write-Host "==========================================" -ForegroundColor Green
    Write-Host "VeloxHub published successfully!" -ForegroundColor Green
    Write-Host "Binary: $OutputDir/VeloxHub.exe" -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Green
} else {
    Write-Error "VeloxHub publication failed."
}

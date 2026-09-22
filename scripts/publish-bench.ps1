# Publish VeloxBench as a standalone single-file Windows executable
param (
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "$PSScriptRoot/../Releases/VeloxBench"
)

Write-Host "Publishing VeloxBench ($Runtime) to $OutputDir..." -ForegroundColor Cyan

if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
}

dotnet publish "$PSScriptRoot/../VeloxBench/VeloxBench.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $OutputDir

if ($LASTEXITCODE -eq 0) {
    # Ensure lib tools exist in release directory
    $libSource = "$PSScriptRoot/../VeloxBench/lib"
    $libDest = "$OutputDir/lib"
    if (Test-Path $libSource) {
        Copy-Item -Path $libSource -Destination $OutputDir -Recurse -Force
    }

    Write-Host "==========================================" -ForegroundColor Green
    Write-Host "VeloxBench published successfully!" -ForegroundColor Green
    Write-Host "Binary: $OutputDir/VeloxBench.exe" -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Green
} else {
    Write-Error "VeloxBench publication failed."
}

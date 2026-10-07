# Publish VeloxBench as a standalone single-file Windows executable
param (
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "$PSScriptRoot/../Releases/VeloxBench"
)

Write-Host "Publishing VeloxBench ($Runtime) to $OutputDir..." -ForegroundColor Cyan

if (Test-Path "$OutputDir/VeloxBench.old.exe") {
    Remove-Item "$OutputDir/VeloxBench.old.exe" -Force -ErrorAction SilentlyContinue
}
if (Test-Path "$OutputDir/VeloxBench.exe") {
    $ts = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    Rename-Item -Path "$OutputDir/VeloxBench.exe" -NewName "VeloxBench.old-$ts.exe" -Force -ErrorAction SilentlyContinue
}

dotnet publish "$PSScriptRoot/../VeloxBench/VeloxBench.csproj" `
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

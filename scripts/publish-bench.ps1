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

    # Seed portable license.key if not already present
    $licFile = "$OutputDir/license.key"
    if (-not (Test-Path $licFile)) {
        "VLX-ALPHA-TEST-2026" | Out-File -FilePath $licFile -Encoding utf8 -NoNewline
        Write-Host "Created portable license file: $licFile" -ForegroundColor Yellow
    }

    # Seed portable config.json template if not already present
    $cfgFile = "$OutputDir/config.json"
    if (-not (Test-Path $cfgFile)) {
        @"
{
  "ApiUrl": "http://localhost:5000",
  "ApiKey": "vx-2fe1479ad70dffd3017a0aa8c67b1c270fc9dd00bb9d4bdf",
  "LicenseKey": "VLX-ALPHA-TEST-2026",
  "BenchName": "BENCH-01",
  "UpdateChannel": "alpha",
  "AutoUpdateEnabled": true
}
"@ | Out-File -FilePath $cfgFile -Encoding utf8
        Write-Host "Created portable config file: $cfgFile" -ForegroundColor Yellow
    }

    Write-Host "==========================================" -ForegroundColor Green
    Write-Host "VeloxBench published successfully!" -ForegroundColor Green
    Write-Host "Binary: $OutputDir/VeloxBench.exe" -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Green
} else {
    Write-Error "VeloxBench publication failed."
}

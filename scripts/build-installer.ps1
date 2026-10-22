# Velox Suite Professional Build & Installer Script
# Abzard Inc. (c) 2026

param(
    [string]$Configuration = "Release",
    [string]$Version = "1.6.10"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Velox Suite Enterprise Installer Packager v$Version   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Compile Velox.Shared
Write-Host "`n[1/4] Building Velox.Shared..." -ForegroundColor Yellow
dotnet build "$RootDir\Velox.Shared\Velox.Shared.csproj" -c $Configuration --nologo

# 2. Publish VeloxBench as Single-File Self-Contained Win-x64
Write-Host "`n[2/4] Publishing VeloxBench (Single-File win-x64)..." -ForegroundColor Yellow
$BenchOut = "$RootDir\Releases\VeloxBench"
dotnet publish "$RootDir\VeloxBench\VeloxBench.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $BenchOut `
    --nologo

# Ensure clean default config.json beside VeloxBench.exe
$ConfigPath = Join-Path $BenchOut "config.json"
if (-not (Test-Path $ConfigPath)) {
    $ConfigJson = @{
        ApiUrl = "http://13.206.106.146"
        DepotId = "MAIN"
        BenchId = "BENCH-01"
        UpdateChannel = "alpha"
    } | ConvertTo-Json -Depth 3
    Set-Content -Path $ConfigPath -Value $ConfigJson -Encoding utf8
    Write-Host "  -> Generated default config.json pointing to Lightsail Cloud (13.206.106.146)" -ForegroundColor Green
}

# 3. Stage Installer Directory
$InstallerOut = "$RootDir\Releases\Installer"
if (-not (Test-Path $InstallerOut)) {
    New-Item -ItemType Directory -Path $InstallerOut -Force | Out-Null
}

# 4. Search for Inno Setup (ISCC.exe)
Write-Host "`n[3/4] Checking for Inno Setup Compiler..." -ForegroundColor Yellow
$IsccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "ISCC.exe"
)

$IsccExe = $null
foreach ($path in $IsccPaths) {
    if (Get-Command $path -ErrorAction SilentlyContinue) {
        $IsccExe = $path
        break
    }
}

if ($IsccExe) {
    Write-Host "  -> Compiling Inno Setup Script: $RootDir\scripts\build-installer.iss" -ForegroundColor Green
    & $IsccExe "$RootDir\scripts\build-installer.iss"
    Write-Host "`n[4/4] Successfully created: $InstallerOut\VeloxSetup.exe" -ForegroundColor Green
} else {
    Write-Host "  -> Inno Setup (ISCC.exe) not found on system." -ForegroundColor Yellow
    Write-Host "  -> Staging portable deployment zip instead: $InstallerOut\VeloxSuite-$Version-win-x64.zip" -ForegroundColor Cyan
    
    $ZipPath = Join-Path $InstallerOut "VeloxSuite-$Version-win-x64.zip"
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Compress-Archive -Path "$BenchOut\*" -DestinationPath $ZipPath -Force
    Write-Host "`n[4/4] Successfully created portable archive: $ZipPath" -ForegroundColor Green
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "   Velox Packaging Completed Successfully!               " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

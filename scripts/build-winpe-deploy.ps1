<#
.SYNOPSIS
    Builds the official bootable Velox Deploy WinPE 11 ISO / USB image.
.DESCRIPTION
    Integrates Windows ADK WinPE micro-kernel, network/storage drivers,
    auto-launch startnet.cmd hook, and standalone VeloxDeploy.exe.
.EXAMPLE
    .\build-winpe-deploy.ps1 -IsoPath "C:\VeloxDeploy_v1.0.iso"
#>

[CmdletBinding()]
param(
    [string]$IsoPath = "C:\VeloxDeploy_v1.0.iso",
    [string]$TargetUsbDrive = "",
    [string]$WorkDir = "C:\VeloxWinPE_Build"
)

$ErrorActionPreference = "Stop"

Write-Host "=================================================================" -ForegroundColor DarkYellow
Write-Host " VELOX DEPLOY: WINPE 11 BOOT MEDIA BUILDER                       " -ForegroundColor Yellow
Write-Host "=================================================================" -ForegroundColor DarkYellow

$adkWinPeRoot = "C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Windows Preinstallation Environment\amd64"
$adkToolsRoot = "C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\amd64"

if (-not (Test-Path $adkWinPeRoot)) {
    Write-Warning "Windows ADK WinPE Add-on not found at standard path: $adkWinPeRoot"
    Write-Host "To produce a physical bootable USB/ISO, please install:" -ForegroundColor Cyan
    Write-Host "1. Windows ADK 10/11 (Deployment Tools)" -ForegroundColor White
    Write-Host "2. Windows PE Add-on for ADK" -ForegroundColor White
    Write-Host "In the meantime, VeloxDeploy.exe can be tested in Live Dry-Run simulation mode directly on desktop!" -ForegroundColor Green
    exit 0
}

# Locate VeloxDeploy binary
$deployExe = Join-Path $PSScriptRoot "..\Releases\VeloxDeploy\VeloxDeploy.exe"
if (-not (Test-Path $deployExe)) {
    $deployExe = Join-Path $PSScriptRoot "..\VeloxDeploy\bin\Release\net10.0-windows\win-x64\VeloxDeploy.exe"
}

if (-not (Test-Path $deployExe)) {
    Write-Error "VeloxDeploy executable not found. Please build or publish the project first."
}

Write-Host "[+] Using VeloxDeploy binary: $deployExe" -ForegroundColor Cyan

# Cleanup existing build dir
if (Test-Path $WorkDir) {
    Write-Host "[+] Cleaning previous build directory: $WorkDir"
    Remove-Item -Recurse -Force $WorkDir
}

New-Item -ItemType Directory -Path "$WorkDir\media" -Force | Out-Null
New-Item -ItemType Directory -Path "$WorkDir\mount" -Force | Out-Null

# Copy base WinPE media
Write-Host "[+] Copying base WinPE 11 environment..." -ForegroundColor Cyan
Copy-Item -Recurse "$adkWinPeRoot\Media\*" "$WorkDir\media\"
Copy-Item "$adkWinPeRoot\en-us\winpe.wim" "$WorkDir\media\sources\boot.wim"

# Mount Boot WIM
Write-Host "[+] Mounting boot.wim..." -ForegroundColor Cyan
dism.exe /Mount-Wim /WimFile:"$WorkDir\media\sources\boot.wim" /Index:1 /MountDir:"$WorkDir\mount"

try {
    # Create Velox folder inside WinPE
    $targetVeloxDir = "$WorkDir\mount\Velox"
    New-Item -ItemType Directory -Path $targetVeloxDir -Force | Out-Null

    Write-Host "[+] Copying VeloxDeploy.exe into WinPE micro-kernel..." -ForegroundColor Cyan
    Copy-Item $deployExe "$targetVeloxDir\VeloxDeploy.exe"

    # Hook startnet.cmd
    $startnet = "$WorkDir\mount\Windows\System32\startnet.cmd"
    Write-Host "[+] Configuring startnet.cmd auto-launch sequence..." -ForegroundColor Cyan
    $startnetContent = @"
@echo off
wpeinit
echo Initializing Velox Deploy Appliance Subsystem...
start "" "X:\Velox\VeloxDeploy.exe"
"@
    Set-Content -Path $startnet -Value $startnetContent -Encoding ASCII

    Write-Host "[+] Saving and unmounting WIM..." -ForegroundColor Cyan
    dism.exe /Unmount-Wim /MountDir:"$WorkDir\mount" /Commit
}
catch {
    Write-Warning "Build failed: $_"
    dism.exe /Unmount-Wim /MountDir:"$WorkDir\mount" /Discard
    throw $_
}

# Create ISO if requested
if ($IsoPath) {
    Write-Host "[+] Creating bootable ISO: $IsoPath" -ForegroundColor Cyan
    $oscdimg = "$adkToolsRoot\Oscdimg\oscdimg.exe"
    $etfsboot = "$WorkDir\media\boot\etfsboot.com"
    $efisys = "$WorkDir\media\efi\microsoft\boot\efisys.bin"

    & $oscdimg -m -o -u2 -udfver102 -bootdata:2#p0,e,b"$etfsboot"#pEF,e,b"$efisys" "$WorkDir\media" "$IsoPath"
    Write-Host "[✓] ISO created successfully at $IsoPath" -ForegroundColor Green
}

Write-Host "=================================================================" -ForegroundColor DarkGreen
Write-Host " VELOX DEPLOY WINPE BUILD COMPLETE!                              " -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor DarkGreen

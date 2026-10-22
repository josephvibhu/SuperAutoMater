param (
    [Parameter(Mandatory=$false)]
    [string]$PartnerName = 'Ritchie-Depot',

    [Parameter(Mandatory=$false)]
    [string]$LicenseKey = 'VLX-ALPHA-RITCHIE-01',

    [Parameter(Mandatory=$false)]
    [string]$ServerUrl = 'http://127.0.0.1:5000',

    [Parameter(Mandatory=$false)]
    [string]$BenchName = 'BENCH-01',

    [Parameter(Mandatory=$false)]
    [string]$DestinationDir = ''
)

$ErrorActionPreference = 'Stop'

$sourceDir = Join-Path $PSScriptRoot '..\Releases\VeloxBench'
if (-not (Test-Path (Join-Path $sourceDir 'VeloxBench.exe'))) {
    Write-Host 'VeloxBench.exe not found in Releases. Building first...' -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot 'publish-bench.ps1')
}

if ([string]::IsNullOrWhiteSpace($DestinationDir)) {
    $DestinationDir = Join-Path $PSScriptRoot "..\Releases\Kits\$PartnerName"
}

Write-Host '==========================================================' -ForegroundColor Cyan
Write-Host "   VeloxBench USB Kit Staging for: $PartnerName" -ForegroundColor Cyan
Write-Host "   Destination: $DestinationDir" -ForegroundColor Cyan
Write-Host "   Server URL:  $ServerUrl" -ForegroundColor Cyan
Write-Host "   License Key: $LicenseKey" -ForegroundColor Cyan
Write-Host '==========================================================' -ForegroundColor Cyan

if (-not (Test-Path $DestinationDir)) {
    New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null
}

# 1. Copy Binary
Write-Host '[1/5] Copying VeloxBench.exe...' -ForegroundColor Gray
Copy-Item -Path (Join-Path $sourceDir 'VeloxBench.exe') -Destination (Join-Path $DestinationDir 'VeloxBench.exe') -Force

# 2. Copy lib tools (smartctl and HDSentinel)
Write-Host '[2/5] Copying lib diagnostics binaries...' -ForegroundColor Gray
$libDir = Join-Path $sourceDir 'lib'
if (Test-Path $libDir) {
    Copy-Item -Path $libDir -Destination $DestinationDir -Recurse -Force
}

# 3. Copy Firewall Utility
$fwFile = Join-Path $sourceDir 'Fix-Firewall.bat'
if (Test-Path $fwFile) {
    Copy-Item -Path $fwFile -Destination (Join-Path $DestinationDir 'Fix-Firewall.bat') -Force
}

# 4. Generate Config and License
Write-Host '[3/5] Writing configured config.json and license.key...' -ForegroundColor Gray
$cfgObj = [ordered]@{
    ApiUrl = $ServerUrl
    ApiKey = 'vx-2fe1479ad70dffd3017a0aa8c67b1c270fc9dd00bb9d4bdf'
    LicenseKey = $LicenseKey
    BenchName = $BenchName
    UpdateChannel = 'alpha'
    AutoUpdateEnabled = $true
}
$cfgObj | ConvertTo-Json -Depth 4 | Out-File -FilePath (Join-Path $DestinationDir 'config.json') -Encoding ascii -Force
$LicenseKey.Trim() | Out-File -FilePath (Join-Path $DestinationDir 'license.key') -Encoding ascii -NoNewline -Force

# 5. Write Quick Start Card
Write-Host '[4/5] Generating Field Card for Technicians...' -ForegroundColor Gray
$cardText = @"
================================================================================
   VELOXBENCH -- QUICK START GUIDE (TECHNICIAN FIELD CARD)
   Partner Depot: $PartnerName | License: $LicenseKey
================================================================================

HOW TO TEST A LAPTOP IN 2 MINUTES:
--------------------------------------------------------------------------------
1. PLUG USB INTO LAPTOP.
2. OPEN 'VeloxBench.exe' (No installation needed).
3. PRESS [F5] OR CLICK 'EXPRESS QC'.
   - The app will test CPU stress, RAM, Battery health %, SSD SMART health,
     Display RGB cycle, Audio, and Ports in ~120 seconds.
4. CHECK THE BADGE ON SCREEN:
   - GREEN (GRADE A): Passed all tests.
   - YELLOW (GRADE B): Minor cosmetics or battery wear (<70%).
   - RED (FAIL): Hardware defect detected (bad sectors, thermal throttle).
5. SHARE RESULTS WITH BUYER:
   - Click [COPY SPECS] -> Paste directly into WhatsApp Desktop!
   - (Or click [PRINT STICKER] if thermal printer is plugged in).

OFFLINE OPERATION:
--------------------------------------------------------------------------------
- VeloxBench works 100% OFFLINE. Internet is NOT required to test laptops.
- When laptop is connected to Wi-Fi, it will automatically sync to your
  cloud inventory ledger.

COMMON STATUS AND ERROR CODES:
--------------------------------------------------------------------------------
* VLX-OK-200   : Successfully synced to cloud ledger.
* VLX-NET-100  : No internet connection. Data saved safely on this machine.
                 Will upload when Wi-Fi is connected.
* VLX-LIC-202  : Device limit reached. Contact Abzard support to refresh authorization.
* VLX-UPD-500  : Cloud auto-update available. App updates itself on launch.

HOTLINE / TECH SUPPORT:
--------------------------------------------------------------------------------
Abzard Inc / Velox Team: WhatsApp: +91-XXXXXXXXXX
================================================================================
"@
$cardText | Out-File -FilePath (Join-Path $DestinationDir 'QUICK_START_CARD.txt') -Encoding ascii -Force

Write-Host '==========================================================' -ForegroundColor Green
Write-Host 'Partner kit successfully staged at:' -ForegroundColor Green
Write-Host "   $DestinationDir" -ForegroundColor Yellow
Write-Host ''
Write-Host 'To deploy: Format USB drive (FAT32/NTFS) and copy all files' -ForegroundColor Cyan
Write-Host 'from this folder to the root of the USB drive!' -ForegroundColor Cyan
Write-Host '==========================================================' -ForegroundColor Green

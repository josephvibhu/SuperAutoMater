<#
.SYNOPSIS
    VeloxLink USB "Auto-Intake" WinPE & Portable Zero-Typing Intake Companion
.DESCRIPTION
    Scans hardware using Windows WMI/CIM (Serial, Manufacturer, Model, CPU, RAM, NVMe/SSD, Battery)
    and registers the unit immediately into Velox Depot Cloud or VeloxManager LAN Mirror
    with ZERO manual typing required.
#>

[CmdletBinding()]
param(
    [string]$ConfigPath = "$PSScriptRoot\velox_config.json",
    [switch]$ForceOffline
)

$Host.UI.RawUI.WindowTitle = "⚡ VeloxLink Auto-Intake Companion"
Clear-Host

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "      ⚡ VELOX DEPOT SUITE — ZERO-TYPING AUTO-INTAKE COMPANION   " -ForegroundColor Yellow -BackgroundColor Black
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Load Configuration
$Config = $null
if (Test-Path $ConfigPath) {
    try {
        $Config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
        Write-Host "[i] Loaded configuration from $ConfigPath" -ForegroundColor DarkGray
    } catch {
        Write-Host "[!] Warning: Failed to parse $ConfigPath, using defaults." -ForegroundColor Yellow
    }
}

$ApiUrl = if ($Config.apiUrl) { $Config.apiUrl } else { "http://13.206.106.146" }
$LanUrl = if ($Config.lanApiUrl) { $Config.lanApiUrl } else { "" }
$ApiKey = if ($Config.apiKey) { $Config.apiKey } else { "vx-2fe1479ad70dffd3017a0aa8c67b1c270fc9dd00bb9d4bdf" }
$Actor  = if ($Config.actor) { $Config.actor } else { "VeloxLink-AutoIntake" }
$Loc    = if ($Config.defaultLocation) { $Config.defaultLocation } else { "INTAKE-STAGING" }

# 2. Hardware Scan via WMI / CIM
Write-Host "[*] Scanning hardware components via WMI/CIM..." -ForegroundColor White

# Serial Number
$Bios = Get-CimInstance Win32_BIOS -ErrorAction SilentlyContinue
$Serial = if ($Bios.SerialNumber) { $Bios.SerialNumber.Trim() } else { "UNKNOWN" }

# Computer System
$Cs = Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue
$Vendor = if ($Cs.Manufacturer) { $Cs.Manufacturer.Trim() } else { "Generic" }
$ModelName = if ($Cs.Model) { $Cs.Model.Trim() } else { "Computer" }
$FullModel = "$Vendor $ModelName".Trim()

# Processor
$CpuObj = Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1
$Cpu = if ($CpuObj.Name) { $CpuObj.Name.Trim() -replace '\s+', ' ' } else { "Unknown Processor" }

# RAM Capacity
$RamTotalGb = 0
try {
    $RamModules = Get-CimInstance Win32_PhysicalMemory -ErrorAction SilentlyContinue
    $TotalBytes = ($RamModules | Measure-Object -Property Capacity -Sum).Sum
    $RamTotalGb = [Math]::Round($TotalBytes / 1GB)
} catch {
    $RamTotalGb = 8
}
$RamDisplay = "${RamTotalGb}GB RAM"

# Storage
$DiskDisplay = "Standard Drive"
$StorageHealth = 100
try {
    $Disks = Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue
    if ($Disks) {
        $PrimaryDisk = $Disks | Select-Object -First 1
        $DiskSizeGb = [Math]::Round($PrimaryDisk.Size / 1GB)
        $DiskModel = $PrimaryDisk.Model.Trim()
        $DiskDisplay = "${DiskSizeGb}GB ($DiskModel)"
    }
} catch { }

# Battery Health (for Laptops)
$BatteryHealth = $null
try {
    $StaticBatt = Get-CimInstance -Namespace root\wmi -ClassName BatteryStaticData -ErrorAction SilentlyContinue | Select-Object -First 1
    $FullCharge = Get-CimInstance -Namespace root\wmi -ClassName BatteryFullChargedCapacity -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($StaticBatt -and $FullCharge -and $StaticBatt.DesignedCapacity -gt 0) {
        $Pct = [Math]::Round(($FullCharge.FullChargedCapacity / $StaticBatt.DesignedCapacity) * 100)
        if ($Pct -gt 100) { $Pct = 100 }
        $BatteryHealth = $Pct
    }
} catch { }

# Categorize
$Category = "Laptop"
$Chassis = Get-CimInstance Win32_SystemEnclosure -ErrorAction SilentlyContinue | Select-Object -First 1
if ($Chassis.ChassisTypes -contains 3 -or $Chassis.ChassisTypes -contains 4 -or $Chassis.ChassisTypes -contains 6) {
    $Category = "Desktop"
}

# Generate Asset Tag
$RandomTag = "TAG-" + (Get-Random -Minimum 100000 -Maximum 999999)
$AssetTag = $RandomTag

# Build Specs string
$Notes = "CPU: $Cpu | RAM/SSD: $RamDisplay / $DiskDisplay | S/N: $Serial | Auto-scanned by VeloxLink"

# 3. Present Hardware Card
Write-Host ""
Write-Host "┌────────────────── HARDWARE IDENTITY CARD ──────────────────┐" -ForegroundColor Green
Write-Host ("│ Model       : " + $FullModel.PadRight(45) + "│") -ForegroundColor Green
Write-Host ("│ Serial (SN) : " + $Serial.PadRight(45) + "│") -ForegroundColor Green
Write-Host ("│ Asset Tag   : " + $AssetTag.PadRight(45) + "│") -ForegroundColor Green
Write-Host ("│ Processor   : " + $Cpu.Substring(0, [Math]::Min(45, $Cpu.Length)).PadRight(45) + "│") -ForegroundColor Green
Write-Host ("│ Memory/Disk : " + ("$RamDisplay / $DiskDisplay").Substring(0, [Math]::Min(45, ("$RamDisplay / $DiskDisplay").Length)).PadRight(45) + "│") -ForegroundColor Green
if ($BatteryHealth -ne $null) {
    Write-Host ("│ Battery QC  : " + ("${BatteryHealth}% Health").PadRight(45) + "│") -ForegroundColor Green
}
Write-Host "└─────────────────────────────────────────────────────────────┘" -ForegroundColor Green
Write-Host ""

# 4. Save Local USB Manifest
$LogFile = "$PSScriptRoot\INTAKE_LOG.csv"
$IsNewFile = -not (Test-Path $LogFile)
if ($IsNewFile) {
    "Timestamp,AssetTag,SerialNumber,Model,Category,Specs,BatteryHealth" | Out-File -FilePath $LogFile -Encoding UTF8
}
$LogLine = "$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')),$AssetTag,$Serial,""$FullModel"",$Category,""$Notes"",$BatteryHealth"
$LogLine | Out-File -FilePath $LogFile -Append -Encoding UTF8
Write-Host "[✓] Offline log updated: $LogFile" -ForegroundColor DarkGray

# 5. Push to VeloxAPI (LAN or Cloud)
if (-not $ForceOffline) {
    $TargetUrl = $ApiUrl
    # Try LAN server first if configured and reachable
    if (![string]::IsNullOrWhiteSpace($LanUrl)) {
        try {
            $LanPing = Invoke-RestMethod -Uri "$LanUrl/api/v1/assets/lookup?q=test" -TimeoutSec 2 -ErrorAction SilentlyContinue
            if ($LanPing) {
                $TargetUrl = $LanUrl
                Write-Host "[i] Active LAN Mirror detected at $LanUrl (using local depot server)" -ForegroundColor Cyan
            }
        } catch { }
    }

    Write-Host "[*] Contacting depot gateway: $TargetUrl..." -ForegroundColor White

    $IntakePayload = @{
        serial_number   = $Serial
        asset_tag       = $AssetTag
        model           = $FullModel
        category        = $Category
        battery_health  = $BatteryHealth
        storage_health  = $StorageHealth
        location        = $Loc
        notes           = $Notes
        actor           = $Actor
    }

    $JsonPayload = $IntakePayload | ConvertTo-Json -Compress

    try {
        $Headers = @{
            "X-Api-Key"    = $ApiKey
            "Content-Type" = "application/json"
        }

        $Response = Invoke-RestMethod -Uri "$TargetUrl/api/v1/assets/intake" `
                                      -Method Post `
                                      -Headers $Headers `
                                      -Body $JsonPayload `
                                      -TimeoutSec 15

        if ($Response -and $Response.id) {
            Write-Host ""
            Write-Host "=================================================================" -ForegroundColor Green
            Write-Host " ✅ INTAKE COMPLETED: $AssetTag LISTED IN DEPOT INVENTORY" -ForegroundColor Green -BackgroundColor Black
            Write-Host "    Lifecycle Queue : IntakeStaging (Ready for Bench Testing)" -ForegroundColor White
            Write-Host "    Unit Record ID  : $($Response.id)" -ForegroundColor DarkGray
            Write-Host "=================================================================" -ForegroundColor Green
        }
    } catch {
        Write-Host ""
        Write-Host "[!] Gateway upload failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "[!] Unit preserved locally on USB. Run sync when network is restored." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Press any key to complete or disconnect USB..." -ForegroundColor DarkGray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

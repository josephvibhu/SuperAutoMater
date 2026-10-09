# ⚡ VeloxLink — USB "Auto-Intake" & WinPE Portable Companion

## Overview
**VeloxLink Auto-Intake** is a portable, zero-typing hardware intake tool designed for depot intake stations, warehouse receiving docks, and bootable WinPE / live USB environments.

When plugged into any newly received laptop or PC:
1. **WMI/CIM Hardware Discovery**: Automatically reads the true BIOS serial number, system manufacturer & model name, CPU processor, total RAM, primary NVMe/SSD drive capacity & model, and laptop battery health percentage.
2. **Zero Manual Data Entry**: Formats clean, standardized specifications (`CPU: ... | RAM/SSD: ...`) and generates a unique depot asset tag (`TAG-XXXXXX`).
3. **Depot Cloud / LAN Mirror Push**: Instantly registers the unit into VeloxAPI (`/api/v1/assets/intake`) placing it into `IntakeStaging` queue ready for technician assignment and testing.
4. **Offline Resilient USB Logging**: Automatically appends each scanned asset into `INTAKE_LOG.csv` located directly on the USB drive, allowing offline batch intake in facilities without immediate internet.

---

## How to Deploy on a USB Drive

1. Copy the contents of the `scripts/velox-link` folder to the root of a FAT32 or NTFS formatted USB flash drive:
   - `VeloxLink-AutoIntake.bat`
   - `VeloxLink-AutoIntake.ps1`
   - `velox_config.json`
2. Edit `velox_config.json` if your depot uses a custom LAN IP (e.g. `http://192.168.1.100:9000`) or specific cloud API endpoint.
3. Plug the USB into any machine, double-click `VeloxLink-AutoIntake.bat` (or configure it to run in WinPE `startnet.cmd`), and the unit will be intaked into the system in under 3 seconds with 0 keystrokes!

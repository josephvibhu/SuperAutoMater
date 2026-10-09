@echo off
title VeloxLink Auto-Intake Tool
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0VeloxLink-AutoIntake.ps1"

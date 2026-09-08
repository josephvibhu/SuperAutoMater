@echo off
title SuperAutoMater GitHub Sync
cls
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Scripts\Sync-GitHub.ps1" %*
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Press any key to exit...
    pause >nul
) else (
    timeout /t 4 >nul
)

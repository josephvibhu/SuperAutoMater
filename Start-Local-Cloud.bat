@echo off
title Velox Cloud API (Local Testing Server)
cd /d "%~dp0"
echo ========================================================
echo   Starting Velox Cloud API on http://127.0.0.1:5000 ...
echo   (Press Ctrl+C to stop the server)
echo ========================================================
dotnet run --project "%~dp0VeloxAPI\VeloxAPI.csproj" --urls "http://127.0.0.1:5000"
pause

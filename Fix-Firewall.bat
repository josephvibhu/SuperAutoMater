@echo off
:: SuperAutoMater & SuperManager — Warehouse LAN Firewall Fix
:: Run this batch file as Administrator on any bench laptop or manager PC.
echo ===================================================================
echo   Configuring Windows Firewall for SuperAutoMater and SuperManager
echo ===================================================================

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Please right-click this file and select 'Run as administrator'.
    pause
    exit /b 1
)

echo [1/5] Allowing TCP Port 8443 (Bench Telemetry & Live Remote Desktop)...
netsh advfirewall firewall delete rule name="SuperAutoMater Fleet 8443" >nul 2>&1
netsh advfirewall firewall add rule name="SuperAutoMater Fleet 8443" dir=in action=allow protocol=TCP localport=8443 profile=any

echo [2/5] Allowing TCP Port 9000 (SuperManager Master Cockpit Web HUD)...
netsh advfirewall firewall delete rule name="SuperManager HUD 9000" >nul 2>&1
netsh advfirewall firewall add rule name="SuperManager HUD 9000" dir=in action=allow protocol=TCP localport=9000 profile=any

echo [3/5] Allowing UDP Port 9876 (Subnet Discovery Mesh)...
netsh advfirewall firewall delete rule name="SuperAutoMater Mesh 9876" >nul 2>&1
netsh advfirewall firewall add rule name="SuperAutoMater Mesh 9876" dir=in action=allow protocol=UDP localport=9876 profile=any

echo [4/5] Reserving HTTP.SYS URL ACLs for Port 8443 and 9000...
netsh http delete urlacl url=http://*:8443/ >nul 2>&1
netsh http add urlacl url=http://*:8443/ sddl="D:(A;;GX;;;WD)" >nul 2>&1
netsh http delete urlacl url=http://+:8443/ >nul 2>&1
netsh http add urlacl url=http://+:8443/ sddl="D:(A;;GX;;;WD)" >nul 2>&1

netsh http delete urlacl url=http://*:9000/ >nul 2>&1
netsh http add urlacl url=http://*:9000/ sddl="D:(A;;GX;;;WD)" >nul 2>&1
netsh http delete urlacl url=http://+:9000/ >nul 2>&1
netsh http add urlacl url=http://+:9000/ sddl="D:(A;;GX;;;WD)" >nul 2>&1

echo [5/5] Done! All incoming LAN ports and URL reservations are active.
echo ===================================================================
echo SuperAutoMater and SuperManager can now communicate across the LAN!
echo ===================================================================
pause

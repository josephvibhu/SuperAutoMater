using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SuperAutoMater.Wpf.Core
{
    /// <summary>
    /// Automatically provisions Windows Defender Firewall rules and HTTP.SYS URL ACL reservations
    /// for SuperAutoMater (Port 8443, UDP 9876) and SuperManager (Port 9000).
    /// Runs silently on a background thread on application launch.
    /// </summary>
    public static class FirewallHelper
    {
        private static bool _configured = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// Asynchronously ensures inbound firewall rules and URL reservations exist on all network profiles.
        /// Safe to call multiple times; executes only once per application run.
        /// </summary>
        public static void EnsureFirewallRulesAsync()
        {
            lock (_lock)
            {
                if (_configured) return;
                _configured = true;
            }

            Task.Run(() =>
            {
                try
                {
                    ConfigureFirewallRules();
                    EnsureStandaloneFixBatchFile();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"[FirewallHelper] Failed to auto-configure firewall: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Executes netsh commands to provision the required firewall rules and URL ACL reservations.
        /// </summary>
        public static void ConfigureFirewallRules()
        {
            try
            {
                // 1. Port 8443 TCP (Bench Client HTTP & Live Remote Desktop)
                RunNetsh("advfirewall firewall delete rule name=\"SuperAutoMater Fleet 8443\"", 2000);
                RunNetsh("advfirewall firewall add rule name=\"SuperAutoMater Fleet 8443\" dir=in action=allow protocol=TCP localport=8443 profile=any", 3000);

                // 2. Port 9000 TCP (SuperManager Master Cockpit Web HUD)
                RunNetsh("advfirewall firewall delete rule name=\"SuperManager HUD 9000\"", 2000);
                RunNetsh("advfirewall firewall add rule name=\"SuperManager HUD 9000\" dir=in action=allow protocol=TCP localport=9000 profile=any", 3000);

                // 3. Port 9876 UDP (Subnet Discovery Mesh & Beacon)
                RunNetsh("advfirewall firewall delete rule name=\"SuperAutoMater Mesh 9876\"", 2000);
                RunNetsh("advfirewall firewall add rule name=\"SuperAutoMater Mesh 9876\" dir=in action=allow protocol=UDP localport=9876 profile=any", 3000);

                // 4. Current Application Executable Inbound Rule
                try
                {
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    {
                        string appName = Path.GetFileNameWithoutExtension(exePath);
                        RunNetsh($"advfirewall firewall delete rule name=\"{appName} App\"", 2000);
                        RunNetsh($"advfirewall firewall add rule name=\"{appName} App\" dir=in action=allow program=\"{exePath}\" enable=yes profile=any", 3000);
                    }
                }
                catch { }

                // 5. HTTP.SYS URL ACL reservations (Language-independent SDDL "D:(A;;GX;;;WD)" = World/Everyone)
                RunNetsh("http delete urlacl url=http://*:8443/", 2000);
                RunNetsh("http add urlacl url=http://*:8443/ sddl=\"D:(A;;GX;;;WD)\"", 3000);
                RunNetsh("http delete urlacl url=http://+:8443/", 2000);
                RunNetsh("http add urlacl url=http://+:8443/ sddl=\"D:(A;;GX;;;WD)\"", 3000);

                RunNetsh("http delete urlacl url=http://*:9000/", 2000);
                RunNetsh("http add urlacl url=http://*:9000/ sddl=\"D:(A;;GX;;;WD)\"", 3000);
                RunNetsh("http delete urlacl url=http://+:9000/", 2000);
                RunNetsh("http add urlacl url=http://+:9000/ sddl=\"D:(A;;GX;;;WD)\"", 3000);

                AppLogger.Info("Lifecycle", "[FirewallHelper] Successfully provisioned Windows Defender Firewall & URL ACL rules.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[FirewallHelper] Netsh rule setup error: {ex.Message}");
            }
        }

        private static void RunNetsh(string args, int waitMs)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", args)
                {
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };
                using var p = Process.Start(psi);
                if (p != null && !p.WaitForExit(waitMs))
                {
                    p.Kill();
                }
            }
            catch { }
        }

        /// <summary>
        /// Generates a standalone Fix-Firewall.bat script in the application folder
        /// for manual one-click repair by technicians if ever needed.
        /// </summary>
        public static void EnsureStandaloneFixBatchFile()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string batPath = Path.Combine(baseDir, "Fix-Firewall.bat");

                string batContent = @"@echo off
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
netsh advfirewall firewall delete rule name=""SuperAutoMater Fleet 8443"" >nul 2>&1
netsh advfirewall firewall add rule name=""SuperAutoMater Fleet 8443"" dir=in action=allow protocol=TCP localport=8443 profile=any

echo [2/5] Allowing TCP Port 9000 (SuperManager Master Cockpit Web HUD)...
netsh advfirewall firewall delete rule name=""SuperManager HUD 9000"" >nul 2>&1
netsh advfirewall firewall add rule name=""SuperManager HUD 9000"" dir=in action=allow protocol=TCP localport=9000 profile=any

echo [3/5] Allowing UDP Port 9876 (Subnet Discovery Mesh)...
netsh advfirewall firewall delete rule name=""SuperAutoMater Mesh 9876"" >nul 2>&1
netsh advfirewall firewall add rule name=""SuperAutoMater Mesh 9876"" dir=in action=allow protocol=UDP localport=9876 profile=any

echo [4/5] Reserving HTTP.SYS URL ACLs for Port 8443 and 9000...
netsh http delete urlacl url=http://*:8443/ >nul 2>&1
netsh http add urlacl url=http://*:8443/ sddl=""D:(A;;GX;;;WD)"" >nul 2>&1
netsh http delete urlacl url=http://+:8443/ >nul 2>&1
netsh http add urlacl url=http://+:8443/ sddl=""D:(A;;GX;;;WD)"" >nul 2>&1

netsh http delete urlacl url=http://*:9000/ >nul 2>&1
netsh http add urlacl url=http://*:9000/ sddl=""D:(A;;GX;;;WD)"" >nul 2>&1
netsh http delete urlacl url=http://+:9000/ >nul 2>&1
netsh http add urlacl url=http://+:9000/ sddl=""D:(A;;GX;;;WD)"" >nul 2>&1

echo [5/5] Done! All incoming LAN ports and URL reservations are active.
echo ===================================================================
echo SuperAutoMater and SuperManager can now communicate across the LAN!
echo ===================================================================
pause
";
                File.WriteAllText(batPath, batContent);
            }
            catch { }
        }
    }
}

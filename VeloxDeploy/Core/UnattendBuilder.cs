using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VeloxDeploy.Models;

namespace VeloxDeploy.Core
{
    public class UnattendBuilder
    {
        public event Action<string>? OnLog;

        public async Task<bool> ProvisionUnattendAsync(
            string targetDriveLetter,
            HardwareProfile profile,
            string adminUsername,
            bool isDryRun,
            CancellationToken cancellationToken)
        {
            Log("[Unattend] Assembling zero-click unattend.xml configuration...");

            string xml = BuildXml(profile, adminUsername);

            if (isDryRun)
            {
                Log("[Unattend] [SIMULATION] Generated unattend.xml payload (OEM Key Injected: " + (profile.AcpiKey.HasKey ? "YES" : "NO") + ")");
                await Task.Delay(400, cancellationToken);
                Log("[Unattend] [SIMULATION] Written to W:\\Windows\\Panther\\unattend.xml");
                await Task.Delay(300, cancellationToken);
                Log("[Unattend] [SIMULATION] Staged VeloxBench.exe into W:\\ProgramData\\Velox\\ for auto-launch on first boot.");
                return true;
            }

            try
            {
                string pantherDir = Path.Combine($"{targetDriveLetter}:\\", "Windows", "Panther");
                Directory.CreateDirectory(pantherDir);

                string unattendPath = Path.Combine(pantherDir, "unattend.xml");
                await File.WriteAllTextAsync(unattendPath, xml, Encoding.UTF8, cancellationToken);
                Log($"[Unattend] Written unattend.xml to: {unattendPath}");

                // Stage VeloxBench into target system
                string veloxDir = Path.Combine($"{targetDriveLetter}:\\", "ProgramData", "Velox");
                Directory.CreateDirectory(veloxDir);

                // Check if current running directory has VeloxBench.exe
                string sourceBench = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VeloxBench.exe");
                if (!File.Exists(sourceBench))
                {
                    // Fallback check in Releases folder or sibling folder
                    string altBench = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "VeloxBench", "VeloxBench.exe"));
                    if (File.Exists(altBench)) sourceBench = altBench;
                }

                if (File.Exists(sourceBench))
                {
                    string destBench = Path.Combine(veloxDir, "VeloxBench.exe");
                    File.Copy(sourceBench, destBench, true);
                    Log($"[Unattend] Seeded VeloxBench executable into: {destBench}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Log($"[Unattend] ERROR: Failed to write unattend.xml: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ProvisionUsbAutounattendAsync(
            string usbDriveLetter,
            HardwareProfile profile,
            string adminUsername,
            bool isDryRun,
            CancellationToken cancellationToken)
        {
            Log("[Unattend] Assembling root autounattend.xml for zero-click USB installer...");

            string xml = BuildUsbAutounattendXml(profile, adminUsername);

            if (isDryRun)
            {
                Log("[Unattend] [SIMULATION] Staged autounattend.xml to " + usbDriveLetter + ":\\autounattend.xml");
                await Task.Delay(300, cancellationToken);
                Log("[Unattend] [SIMULATION] Staged VeloxBench into " + usbDriveLetter + ":\\sources\\$OEM$\\$1\\ProgramData\\Velox\\");
                return true;
            }

            try
            {
                string rootXmlPath = Path.Combine($"{usbDriveLetter}:\\", "autounattend.xml");
                await File.WriteAllTextAsync(rootXmlPath, xml, Encoding.UTF8, cancellationToken);
                Log($"[Unattend] Written USB root autounattend.xml to: {rootXmlPath}");

                // Also stage VeloxBench into $OEM$\$1\ProgramData\Velox on USB
                string oemBenchDir = Path.Combine($"{usbDriveLetter}:\\", "sources", "$OEM$", "$1", "ProgramData", "Velox");
                Directory.CreateDirectory(oemBenchDir);

                string sourceBench = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VeloxBench.exe");
                if (!File.Exists(sourceBench))
                {
                    string altBench = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "VeloxBench", "VeloxBench.exe"));
                    if (File.Exists(altBench)) sourceBench = altBench;
                }

                if (File.Exists(sourceBench))
                {
                    string destBench = Path.Combine(oemBenchDir, "VeloxBench.exe");
                    File.Copy(sourceBench, destBench, true);
                    Log($"[Unattend] Seeded VeloxBench into USB installer payload: {destBench}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Log($"[Unattend] Note on USB autounattend staging: {ex.Message}");
                return true;
            }
        }

        public string BuildUsbAutounattendXml(HardwareProfile profile, string adminUsername)
        {
            string productKeyXml = profile.AcpiKey.HasKey
                ? $"<ProductKey><Key>{profile.AcpiKey.ProductKey}</Key><WillShowUI>OnError</WillShowUI></ProductKey>"
                : string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<unattend xmlns=\"urn:schemas-microsoft-com:unattend\">");

            // windowsPE Pass: Zero-click language selection, partition & install on target machine
            sb.AppendLine("  <settings pass=\"windowsPE\">");
            sb.AppendLine("    <component name=\"Microsoft-Windows-International-Core-WinPE\" processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\" xmlns:wcm=\"http://schemas.microsoft.com/WMIConfig/2002/State\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">");
            sb.AppendLine("      <SetupUILanguage>");
            sb.AppendLine("        <UILanguage>en-US</UILanguage>");
            sb.AppendLine("      </SetupUILanguage>");
            sb.AppendLine("      <InputLocale>en-US</InputLocale>");
            sb.AppendLine("      <SystemLocale>en-US</SystemLocale>");
            sb.AppendLine("      <UILanguage>en-US</UILanguage>");
            sb.AppendLine("      <UILanguageFallback>en-US</UILanguageFallback>");
            sb.AppendLine("      <UserLocale>en-US</UserLocale>");
            sb.AppendLine("    </component>");
            sb.AppendLine("    <component name=\"Microsoft-Windows-Setup\" processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\">");
            sb.AppendLine("      <DiskConfiguration>");
            sb.AppendLine("        <Disk wcm:action=\"add\" xmlns:wcm=\"http://schemas.microsoft.com/WMIConfig/2002/State\">");
            sb.AppendLine("          <DiskID>0</DiskID>");
            sb.AppendLine("          <WillWipeDisk>true</WillWipeDisk>");
            sb.AppendLine("          <CreatePartitions>");
            sb.AppendLine("            <CreatePartition wcm:action=\"add\"><Order>1</Order><Type>EFI</Type><Size>300</Size></CreatePartition>");
            sb.AppendLine("            <CreatePartition wcm:action=\"add\"><Order>2</Order><Type>MSR</Type><Size>16</Size></CreatePartition>");
            sb.AppendLine("            <CreatePartition wcm:action=\"add\"><Order>3</Order><Type>Primary</Type><Extend>true</Extend></CreatePartition>");
            sb.AppendLine("          </CreatePartitions>");
            sb.AppendLine("          <ModifyPartitions>");
            sb.AppendLine("            <ModifyPartition wcm:action=\"add\"><Order>1</Order><PartitionID>1</PartitionID><Format>FAT32</Format><Label>System</Label></ModifyPartition>");
            sb.AppendLine("            <ModifyPartition wcm:action=\"add\"><Order>2</Order><PartitionID>3</PartitionID><Format>NTFS</Format><Label>Windows</Label><Letter>C</Letter></ModifyPartition>");
            sb.AppendLine("          </ModifyPartitions>");
            sb.AppendLine("        </Disk>");
            sb.AppendLine("      </DiskConfiguration>");
            sb.AppendLine("      <ImageInstall>");
            sb.AppendLine("        <OSImage>");
            sb.AppendLine("          <InstallTo><DiskID>0</DiskID><PartitionID>3</PartitionID></InstallTo>");
            sb.AppendLine("          <WillShowUI>OnError</WillShowUI>");
            sb.AppendLine("        </OSImage>");
            sb.AppendLine("      </ImageInstall>");
            sb.AppendLine("      <UserData>");
            sb.AppendLine("        <AcceptEula>true</AcceptEula>");
            if (!string.IsNullOrWhiteSpace(productKeyXml)) sb.AppendLine($"        {productKeyXml}");
            sb.AppendLine("      </UserData>");
            sb.AppendLine("    </component>");
            sb.AppendLine("  </settings>");

            // specialize Pass
            sb.AppendLine("  <settings pass=\"specialize\">");
            sb.AppendLine("    <component name=\"Microsoft-Windows-International-Core\" processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\" xmlns:wcm=\"http://schemas.microsoft.com/WMIConfig/2002/State\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">");
            sb.AppendLine("      <InputLocale>en-US</InputLocale>");
            sb.AppendLine("      <SystemLocale>en-US</SystemLocale>");
            sb.AppendLine("      <UILanguage>en-US</UILanguage>");
            sb.AppendLine("      <UILanguageFallback>en-US</UILanguageFallback>");
            sb.AppendLine("      <UserLocale>en-US</UserLocale>");
            sb.AppendLine("    </component>");
            sb.AppendLine("    <component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\">");
            sb.AppendLine($"      <ComputerName>VLX-{SanitizeComputerName(profile.BiosSerial)}</ComputerName>");
            if (!string.IsNullOrWhiteSpace(productKeyXml)) sb.AppendLine($"      {productKeyXml}");
            sb.AppendLine("      <RegisteredOrganization>Velox Depot Systems</RegisteredOrganization>");
            sb.AppendLine("      <RegisteredOwner>Velox Certified Refurbished</RegisteredOwner>");
            sb.AppendLine("      <TimeZone>India Standard Time</TimeZone>");
            sb.AppendLine("    </component>");
            sb.AppendLine("  </settings>");

            // oobeSystem Pass
            sb.AppendLine("  <settings pass=\"oobeSystem\">");
            sb.AppendLine("    <component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\">");
            sb.AppendLine("      <OOBE>");
            sb.AppendLine("        <HideEULAPage>true</HideEULAPage>");
            sb.AppendLine("        <HideLocalAccountScreen>true</HideLocalAccountScreen>");
            sb.AppendLine("        <HideOEMRegistrationScreen>true</HideOEMRegistrationScreen>");
            sb.AppendLine("        <HideOnlineAccountScreens>true</HideOnlineAccountScreens>");
            sb.AppendLine("        <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>");
            sb.AppendLine("        <NetworkLocation>Work</NetworkLocation>");
            sb.AppendLine("        <ProtectYourPC>3</ProtectYourPC>");
            sb.AppendLine("      </OOBE>");
            sb.AppendLine("      <UserAccounts>");
            sb.AppendLine("        <LocalAccounts>");
            sb.AppendLine("          <LocalAccount wcm:action=\"add\" xmlns:wcm=\"http://schemas.microsoft.com/WMIConfig/2002/State\">");
            sb.AppendLine($"            <Name>{adminUsername}</Name>");
            sb.AppendLine("            <Group>Administrators</Group>");
            sb.AppendLine("            <Password><Value></Value><PlainText>true</PlainText></Password>");
            sb.AppendLine("          </LocalAccount>");
            sb.AppendLine("        </LocalAccounts>");
            sb.AppendLine("      </UserAccounts>");
            sb.AppendLine("      <AutoLogon>");
            sb.AppendLine("        <Enabled>true</Enabled>");
            sb.AppendLine($"        <Username>{adminUsername}</Username>");
            sb.AppendLine("        <LogonCount>3</LogonCount>");
            sb.AppendLine("        <Password><Value></Value><PlainText>true</PlainText></Password>");
            sb.AppendLine("      </AutoLogon>");
            sb.AppendLine("      <FirstLogonCommands>");
            sb.AppendLine("        <SynchronousCommand wcm:action=\"add\" xmlns:wcm=\"http://schemas.microsoft.com/WMIConfig/2002/State\">");
            sb.AppendLine("          <Order>1</Order>");
            sb.AppendLine("          <CommandLine>cmd.exe /c start /b \"\" \"C:\\ProgramData\\Velox\\VeloxBench.exe\"</CommandLine>");
            sb.AppendLine("          <Description>Launch VeloxBench Post-Deployment Diagnostic Cockpit</Description>");
            sb.AppendLine("        </SynchronousCommand>");
            sb.AppendLine("      </FirstLogonCommands>");
            sb.AppendLine("    </component>");
            sb.AppendLine("  </settings>");

            sb.AppendLine("</unattend>");
            return sb.ToString();
        }

        public string BuildXml(HardwareProfile profile, string adminUsername)
        {
            string productKeyXml = profile.AcpiKey.HasKey
                ? $"<ProductKey><Key>{profile.AcpiKey.ProductKey}</Key><WillShowUI>OnError</WillShowUI></ProductKey>"
                : string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<unattend xmlns=\"urn:schemas-microsoft-com:unattend\">");

            // Specialize Pass: OEM Product Key, Language/Locale & Computer Name
            sb.AppendLine("  <settings pass=\"specialize\">");
            sb.AppendLine("    <component name=\"Microsoft-Windows-International-Core\" processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\" xmlns:wcm=\"http://schemas.microsoft.com/WMIConfig/2002/State\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">");
            sb.AppendLine("      <InputLocale>en-US</InputLocale>");
            sb.AppendLine("      <SystemLocale>en-US</SystemLocale>");
            sb.AppendLine("      <UILanguage>en-US</UILanguage>");
            sb.AppendLine("      <UILanguageFallback>en-US</UILanguageFallback>");
            sb.AppendLine("      <UserLocale>en-US</UserLocale>");
            sb.AppendLine("    </component>");
            sb.AppendLine("    <component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\">");
            sb.AppendLine($"      <ComputerName>VLX-{SanitizeComputerName(profile.BiosSerial)}</ComputerName>");
            if (!string.IsNullOrWhiteSpace(productKeyXml))
            {
                sb.AppendLine($"      {productKeyXml}");
            }
            sb.AppendLine("      <RegisteredOrganization>Velox Depot Systems</RegisteredOrganization>");
            sb.AppendLine("      <RegisteredOwner>Velox Certified Refurbished</RegisteredOwner>");
            sb.AppendLine("      <TimeZone>India Standard Time</TimeZone>");
            sb.AppendLine("    </component>");
            sb.AppendLine("  </settings>");

            // OOBE Pass: Bypass Microsoft Account, Setup Local User, AutoLogon, and FirstLogonCommands
            sb.AppendLine("  <settings pass=\"oobeSystem\">");
            sb.AppendLine("    <component name=\"Microsoft-Windows-Shell-Setup\" processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\">");
            sb.AppendLine("      <OOBE>");
            sb.AppendLine("        <HideEULAPage>true</HideEULAPage>");
            sb.AppendLine("        <HideLocalAccountScreen>true</HideLocalAccountScreen>");
            sb.AppendLine("        <HideOEMRegistrationScreen>true</HideOEMRegistrationScreen>");
            sb.AppendLine("        <HideOnlineAccountScreens>true</HideOnlineAccountScreens>");
            sb.AppendLine("        <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>");
            sb.AppendLine("        <NetworkLocation>Work</NetworkLocation>");
            sb.AppendLine("        <ProtectYourPC>3</ProtectYourPC>");
            sb.AppendLine("      </OOBE>");
            sb.AppendLine("      <UserAccounts>");
            sb.AppendLine("        <LocalAccounts>");
            sb.AppendLine("          <LocalAccount wcm:action=\"add\" xmlns:wcm=\"http://schemas.microsoft.com/WMIConfig/2002/State\">");
            sb.AppendLine($"            <Name>{adminUsername}</Name>");
            sb.AppendLine("            <Group>Administrators</Group>");
            sb.AppendLine("            <Password><Value></Value><PlainText>true</PlainText></Password>");
            sb.AppendLine("          </LocalAccount>");
            sb.AppendLine("        </LocalAccounts>");
            sb.AppendLine("      </UserAccounts>");
            sb.AppendLine("      <AutoLogon>");
            sb.AppendLine("        <Enabled>true</Enabled>");
            sb.AppendLine($"        <Username>{adminUsername}</Username>");
            sb.AppendLine("        <LogonCount>3</LogonCount>");
            sb.AppendLine("        <Password><Value></Value><PlainText>true</PlainText></Password>");
            sb.AppendLine("      </AutoLogon>");
            sb.AppendLine("      <FirstLogonCommands>");
            sb.AppendLine("        <SynchronousCommand wcm:action=\"add\" xmlns:wcm=\"http://schemas.microsoft.com/WMIConfig/2002/State\">");
            sb.AppendLine("          <Order>1</Order>");
            sb.AppendLine("          <CommandLine>cmd.exe /c start /b \"\" \"C:\\ProgramData\\Velox\\VeloxBench.exe\"</CommandLine>");
            sb.AppendLine("          <Description>Launch VeloxBench Post-Deployment Diagnostic Cockpit</Description>");
            sb.AppendLine("        </SynchronousCommand>");
            sb.AppendLine("      </FirstLogonCommands>");
            sb.AppendLine("    </component>");
            sb.AppendLine("  </settings>");

            sb.AppendLine("</unattend>");
            return sb.ToString();
        }

        private static string SanitizeComputerName(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial)) return "DEPOT";
            string clean = new string(serial.Where(char.IsLetterOrDigit).ToArray());
            return clean.Length > 9 ? clean[..9].ToUpperInvariant() : clean.ToUpperInvariant();
        }

        private void Log(string message)
        {
            OnLog?.Invoke(message);
        }
    }
}

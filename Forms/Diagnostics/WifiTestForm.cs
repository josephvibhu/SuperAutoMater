using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuperAutoMater
{
    public class WifiTestForm : Form
    {
        private readonly Font titleFont = new Font("Consolas", 11F, FontStyle.Bold);
        private readonly Font headerFont = new Font("Consolas", 9.5F, FontStyle.Bold);
        private readonly Font bodyFont = new Font("Consolas", 9F, FontStyle.Regular);
        private readonly Font monoFont = new Font("Consolas", 8.5F, FontStyle.Regular);

        private Label lblAdapterInfo;
        private Label lblConnectionInfo;
        private ListBox lstNetworks;
        private Panel stepsPanel;
        private Button btnRerun;
        private Button btnManualPass;
        private Button btnClose;

        private struct TestStep
        {
            public string Name;
            public Label LabelRef;
            public Label StatusRef;
        }
        private readonly List<TestStep> testSteps = new List<TestStep>();

        public WifiTestForm()
        {
            this.Text = "WiFi & Wireless Network Diagnostic";
            this.Size = new Size(680, 540);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Form1.BtopBg;
            this.ForeColor = Form1.BtopWhite;
            this.ShowInTaskbar = false;

            BuildUI();
            this.Load += async (s, e) => await RunWifiDiagnosticsAsync();
        }

        private void BuildUI()
        {
            TableLayoutPanel mainGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 5,
                ColumnCount = 1,
                Padding = new Padding(12)
            };
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));  // Header
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 85F));  // Hardware Specs
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));  // Diagnostics & Steps
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));  // Nearby Networks Scan
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));  // Action Buttons

            // 1. Header
            Label lblTitle = new Label
            {
                Dock = DockStyle.Fill,
                Text = "📶 WIFI & WIRELESS HARDWARE DIAGNOSTIC",
                ForeColor = Form1.BtopCyan,
                Font = titleFont,
                TextAlign = ContentAlignment.MiddleLeft
            };
            mainGrid.Controls.Add(lblTitle, 0, 0);

            // 2. Hardware Specs Card
            Panel infoCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Form1.BtopCardBg,
                Padding = new Padding(8),
                Margin = new Padding(0, 2, 0, 6)
            };
            infoCard.Paint += (s, pe) =>
            {
                using (Pen p = new Pen(Form1.BtopBorder, 1))
                    pe.Graphics.DrawRectangle(p, 0, 0, infoCard.Width - 1, infoCard.Height - 1);
            };

            TableLayoutPanel infoGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            infoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            infoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));

            lblAdapterInfo = new Label
            {
                Dock = DockStyle.Fill,
                Text = "ADAPTER: Detecting Wi-Fi Controller...\nSTATUS : Scanning physical interface...",
                ForeColor = Form1.BtopWhite,
                Font = bodyFont,
                TextAlign = ContentAlignment.MiddleLeft
            };

            lblConnectionInfo = new Label
            {
                Dock = DockStyle.Fill,
                Text = "SSID  : Reading network beacon...\nSIGNAL: Measuring RF link...",
                ForeColor = Form1.BtopCyan,
                Font = bodyFont,
                TextAlign = ContentAlignment.MiddleLeft
            };

            infoGrid.Controls.Add(lblAdapterInfo, 0, 0);
            infoGrid.Controls.Add(lblConnectionInfo, 1, 0);
            infoCard.Controls.Add(infoGrid);
            mainGrid.Controls.Add(infoCard, 0, 1);

            // 3. Diagnostic Pipeline Steps
            stepsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Form1.BtopCardBg,
                Padding = new Padding(8),
                Margin = new Padding(0, 0, 0, 6)
            };
            stepsPanel.Paint += (s, pe) =>
            {
                using (Pen p = new Pen(Form1.BtopBorder, 1))
                    pe.Graphics.DrawRectangle(p, 0, 0, stepsPanel.Width - 1, stepsPanel.Height - 1);
            };

            TableLayoutPanel stepsGrid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 2 };
            stepsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            stepsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            for (int i = 0; i < 4; i++) stepsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));

            string[] stepNames = {
                "1. Hardware Adapter & Interface Link",
                "2. Local Gateway & Router ICMP Ping",
                "3. DNS Resolution (Cloudflare / Google)",
                "4. Internet WAN Packet Ping (1.1.1.1)"
            };

            for (int i = 0; i < stepNames.Length; i++)
            {
                Label lblName = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = stepNames[i],
                    Font = headerFont,
                    ForeColor = Form1.BtopWhite,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                Label lblVal = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = "[ PENDING ]",
                    Font = headerFont,
                    ForeColor = Form1.BtopMuted,
                    TextAlign = ContentAlignment.MiddleRight
                };

                stepsGrid.Controls.Add(lblName, 0, i);
                stepsGrid.Controls.Add(lblVal, 1, i);

                testSteps.Add(new TestStep { Name = stepNames[i], LabelRef = lblName, StatusRef = lblVal });
            }

            stepsPanel.Controls.Add(stepsGrid);
            mainGrid.Controls.Add(stepsPanel, 0, 2);

            // 4. Nearby Wi-Fi Networks RF Antenna Scan
            Panel scanCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Form1.BtopCardBg,
                Padding = new Padding(6),
                Margin = new Padding(0, 0, 0, 6)
            };
            scanCard.Paint += (s, pe) =>
            {
                using (Pen p = new Pen(Form1.BtopBorder, 1))
                    pe.Graphics.DrawRectangle(p, 0, 0, scanCard.Width - 1, scanCard.Height - 1);
            };

            TableLayoutPanel scanGrid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            scanGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            scanGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Label lblScanTitle = new Label
            {
                Dock = DockStyle.Fill,
                Text = "📡 RF ANTENNA & OVER-THE-AIR BEACON SCAN (NEARBY NETWORKS):",
                Font = monoFont,
                ForeColor = Form1.BtopAmber,
                TextAlign = ContentAlignment.MiddleLeft
            };

            lstNetworks = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Form1.BtopBtnBg,
                ForeColor = Form1.BtopCyan,
                Font = monoFont,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false
            };

            scanGrid.Controls.Add(lblScanTitle, 0, 0);
            scanGrid.Controls.Add(lstNetworks, 0, 1);
            scanCard.Controls.Add(scanGrid);
            mainGrid.Controls.Add(scanCard, 0, 3);

            // 5. Action Buttons
            TableLayoutPanel btnGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0)
            };
            btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3F));
            btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3F));
            btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.4F));

            btnRerun = new Button
            {
                Dock = DockStyle.Fill,
                Text = "🔄 Re-run Diagnostic",
                Font = headerFont,
                BackColor = Color.FromArgb(25, 35, 50),
                ForeColor = Form1.BtopCyan,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 4, 0)
            };
            btnRerun.FlatAppearance.BorderColor = Form1.BtopCyan;
            btnRerun.Click += async (s, e) => await RunWifiDiagnosticsAsync();

            btnManualPass = new Button
            {
                Dock = DockStyle.Fill,
                Text = "✓ Mark Pass",
                Font = headerFont,
                BackColor = Color.FromArgb(20, 50, 40),
                ForeColor = Color.FromArgb(80, 250, 123),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Margin = new Padding(4, 0, 4, 0)
            };
            btnManualPass.FlatAppearance.BorderColor = Color.FromArgb(80, 250, 123);
            btnManualPass.Click += (s, e) =>
            {
                Form1.Instance?.MarkTestComplete("WiFi");
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            btnClose = new Button
            {
                Dock = DockStyle.Fill,
                Text = "✕ Close",
                Font = headerFont,
                BackColor = Color.FromArgb(35, 20, 25),
                ForeColor = Form1.BtopCoral,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Margin = new Padding(4, 0, 0, 0)
            };
            btnClose.FlatAppearance.BorderColor = Form1.BtopCoral;
            btnClose.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            btnGrid.Controls.Add(btnRerun, 0, 0);
            btnGrid.Controls.Add(btnManualPass, 1, 0);
            btnGrid.Controls.Add(btnClose, 2, 0);
            mainGrid.Controls.Add(btnGrid, 0, 4);

            this.Controls.Add(mainGrid);
        }

        private async Task RunWifiDiagnosticsAsync()
        {
            btnRerun.Enabled = false;
            lstNetworks.Items.Clear();
            lstNetworks.Items.Add("  Scanning 2.4 GHz & 5 GHz channels for wireless access points...");

            foreach (var step in testSteps)
            {
                step.StatusRef.Text = "[ RUNNING... ]";
                step.StatusRef.ForeColor = Form1.BtopAmber;
            }

            // Step 1: Adapter & Interface Query
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && n.OperationalStatus == OperationalStatus.Up)
                ?? NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                ?? NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            string adapterName = nic != null ? nic.Description : "No Wireless Adapter Found";
            string macAddress = nic != null ? string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2"))) : "N/A";
            string linkStatus = nic != null ? nic.OperationalStatus.ToString().ToUpper() : "DOWN";

            lblAdapterInfo.Text = $"ADAPTER: {adapterName}\n" +
                                 $"MAC    : {macAddress} | LINK: {linkStatus}";

            if (nic != null && nic.OperationalStatus == OperationalStatus.Up)
            {
                SetStepStatus(0, "PASS [✓ LINK UP]", Color.FromArgb(80, 250, 123));
            }
            else if (nic != null)
            {
                SetStepStatus(0, "WARN [!] DISCONNECTED", Form1.BtopAmber);
            }
            else
            {
                SetStepStatus(0, "FAIL [✕ NO HARDWARE]", Form1.BtopCoral);
            }

            // Step 2 & Live Netsh Interface Data
            await Task.Run(() =>
            {
                try
                {
                    string netshOut = RunCommand("netsh", "wlan show interfaces");
                    string ssid = ExtractRegex(netshOut, @"SSID\s*:\s*(.+)$");
                    string signal = ExtractRegex(netshOut, @"Signal\s*:\s*(.+)$");
                    string radio = ExtractRegex(netshOut, @"Radio type\s*:\s*(.+)$");
                    string channel = ExtractRegex(netshOut, @"Channel\s*:\s*(.+)$");
                    string rxRate = ExtractRegex(netshOut, @"Receive rate\s*:\s*(.+)$");

                    if (!string.IsNullOrWhiteSpace(ssid))
                    {
                        if (this.IsDisposed || !this.IsHandleCreated) return;
                        this.BeginInvoke(new Action(() =>
                        {
                            lblConnectionInfo.Text = $"SSID  : {ssid} (Signal: {signal})\n" +
                                                     $"RADIO : {radio} | CH: {channel} ({rxRate} Mbps)";
                        }));
                    }
                    else
                    {
                        if (this.IsDisposed || !this.IsHandleCreated) return;
                        this.BeginInvoke(new Action(() =>
                        {
                            lblConnectionInfo.Text = "SSID  : [Not Connected / Disconnected]\nSIGNAL: N/A";
                        }));
                    }
                }
                catch { }
            });

            // Step 2: Gateway Ping
            string gatewayIp = nic?.GetIPProperties().GatewayAddresses.FirstOrDefault()?.Address?.ToString();
            if (!string.IsNullOrEmpty(gatewayIp))
            {
                long gwPing = await PingHostAsync(gatewayIp);
                if (gwPing >= 0)
                    SetStepStatus(1, $"PASS [✓ {gwPing}ms ({gatewayIp})]", Color.FromArgb(80, 250, 123));
                else
                    SetStepStatus(1, $"FAIL [✕ NO RESPONSE ({gatewayIp})]", Form1.BtopCoral);
            }
            else
            {
                SetStepStatus(1, "SKIP [- NO GATEWAY IP]", Form1.BtopMuted);
            }

            // Step 3: DNS Lookup
            bool dnsOk = await Task.Run(() =>
            {
                try
                {
                    var hostEntry = Dns.GetHostEntry("one.one.one.one");
                    return hostEntry != null && hostEntry.AddressList.Length > 0;
                }
                catch
                {
                    try
                    {
                        var hostEntry2 = Dns.GetHostEntry("google.com");
                        return hostEntry2 != null && hostEntry2.AddressList.Length > 0;
                    }
                    catch { return false; }
                }
            });

            if (dnsOk)
                SetStepStatus(2, "PASS [✓ RESOLVED]", Color.FromArgb(80, 250, 123));
            else
                SetStepStatus(2, "FAIL [✕ RESOLUTION FAILED]", Form1.BtopCoral);

            // Step 4: Internet WAN Ping
            long wanPing = await PingHostAsync("1.1.1.1");
            if (wanPing < 0) wanPing = await PingHostAsync("8.8.8.8");

            if (wanPing >= 0)
            {
                SetStepStatus(3, $"PASS [✓ {wanPing}ms (1.1.1.1)]", Color.FromArgb(80, 250, 123));
                // Auto-mark WiFi Complete!
                Form1.Instance?.MarkTestComplete("WiFi");
            }
            else
            {
                SetStepStatus(3, "FAIL [✕ NO INTERNET ECHO]", Form1.BtopCoral);
            }

            // Perform Over-the-Air Beacon Scan for Nearby Networks
            await Task.Run(() =>
            {
                try
                {
                    string scanOut = RunCommand("netsh", "wlan show networks mode=bssid");
                    var networks = ParseNearbyNetworks(scanOut);

                    if (this.IsDisposed || !this.IsHandleCreated) return;
                    this.BeginInvoke(new Action(() =>
                    {
                        lstNetworks.Items.Clear();
                        if (networks.Count > 0)
                        {
                            foreach (var net in networks)
                            {
                                lstNetworks.Items.Add($"  {net.SSID,-28} Signal: {net.Signal,4}  |  Radio: {net.Radio,-12}  |  Auth: {net.Auth}");
                            }
                            // RF Hardware is verified working if beacons are received!
                            Form1.Instance?.MarkTestComplete("WiFi");
                        }
                        else
                        {
                            lstNetworks.Items.Add("  No wireless access points in range or Wi-Fi radio disabled.");
                        }
                    }));
                }
                catch { }
            });

            btnRerun.Enabled = true;
        }

        private void SetStepStatus(int stepIndex, string text, Color color)
        {
            if (stepIndex >= 0 && stepIndex < testSteps.Count)
            {
                testSteps[stepIndex].StatusRef.Text = text;
                testSteps[stepIndex].StatusRef.ForeColor = color;
            }
        }

        private async Task<long> PingHostAsync(string host, int timeoutMs = 1500)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (Ping p = new Ping())
                    {
                        var reply = p.Send(host, timeoutMs);
                        if (reply != null && reply.Status == IPStatus.Success)
                            return reply.RoundtripTime;
                    }
                }
                catch { }
                return -1;
            });
        }

        private string RunCommand(string file, string args)
        {
            try
            {
                using (Process p = new Process())
                {
                    p.StartInfo.FileName = file;
                    p.StartInfo.Arguments = args;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.CreateNoWindow = true;
                    p.Start();
                    string outText = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(4000);
                    return outText;
                }
            }
            catch { return ""; }
        }

        private string ExtractRegex(string source, string pattern)
        {
            if (string.IsNullOrEmpty(source)) return "";
            var match = Regex.Match(source, pattern, RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        private struct NetworkBeacon
        {
            public string SSID;
            public string Signal;
            public string Radio;
            public string Auth;
        }

        private List<NetworkBeacon> ParseNearbyNetworks(string raw)
        {
            var list = new List<NetworkBeacon>();
            if (string.IsNullOrWhiteSpace(raw)) return list;

            string[] blocks = raw.Split(new[] { "SSID " }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var block in blocks)
            {
                if (!block.Contains("Network type")) continue;

                string ssid = block.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                if (ssid.Contains(":")) ssid = ssid.Split(new[] { ':' }, 2)[1].Trim();
                if (string.IsNullOrWhiteSpace(ssid)) ssid = "[Hidden Network]";

                string signal = ExtractRegex(block, @"Signal\s*:\s*(.+)$");
                string radio = ExtractRegex(block, @"Radio type\s*:\s*(.+)$");
                string auth = ExtractRegex(block, @"Authentication\s*:\s*(.+)$");

                list.Add(new NetworkBeacon
                {
                    SSID = ssid,
                    Signal = string.IsNullOrWhiteSpace(signal) ? "N/A" : signal,
                    Radio = string.IsNullOrWhiteSpace(radio) ? "802.11" : radio,
                    Auth = string.IsNullOrWhiteSpace(auth) ? "Open" : auth
                });
            }

            return list.OrderByDescending(n => n.Signal).Take(15).ToList();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { titleFont?.Dispose(); } catch { }
                try { headerFont?.Dispose(); } catch { }
                try { bodyFont?.Dispose(); } catch { }
                try { monoFont?.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}

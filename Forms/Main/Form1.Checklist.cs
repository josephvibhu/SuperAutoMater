using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SuperAutoMater
{
    public partial class Form1
    {
        private CheckBox chkDisplay, chkAudio, chkCamera, chkKeyboard, chkTrackpad, chkGpu, chkWifi, chkFingerprint;
        private TextBox txtTechName;
        private HudButton btnSaveReport;
        private Dictionary<string, CheckBox> _checkMap;

        private Panel BuildChecklistPanel()
        {
            Panel outerBorder = new Panel { Dock = DockStyle.Fill, BackColor = HudTheme.PanelGlassTop, Padding = new Padding(4) };

            TableLayoutPanel mainGrid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));  // Checkboxes
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F)); // Tech ID Reticle Row
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F)); // Sign & Save Button

            // Checkbox Flow Layout (Avionics Status Badges)
            FlowLayoutPanel flpChecks = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = false, Margin = new Padding(0) };
            chkDisplay = CreateCheck("DISPLAY");
            chkAudio = CreateCheck("AUDIO");
            chkCamera = CreateCheck("CAMERA");
            chkKeyboard = CreateCheck("KEYBOARD");
            chkTrackpad = CreateCheck("TRACKPAD");
            chkGpu = CreateCheck("GPU 3D");
            chkWifi = CreateCheck("WI-FI");
            chkFingerprint = CreateCheck("FINGERPRINT");
            flpChecks.Controls.AddRange(new Control[] { chkDisplay, chkAudio, chkCamera, chkKeyboard, chkTrackpad, chkGpu, chkWifi, chkFingerprint });
            mainGrid.Controls.Add(flpChecks, 0, 0);

            // Tech ID Input Row (Reticle underline)
            TableLayoutPanel techRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 2, 0, 2) };
            techRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85F));
            techRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            Label lblTech = new Label
            {
                Text = "TECH // ID:",
                ForeColor = HudTheme.HudAccent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = HudTheme.FontMono11Bold
            };

            txtTechName = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 20, 24),
                ForeColor = HudTheme.TextBright,
                Font = HudTheme.FontMono11Bold,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 2, 0, 2)
            };

            techRow.Controls.Add(lblTech, 0, 0);
            techRow.Controls.Add(txtTechName, 1, 0);
            mainGrid.Controls.Add(techRow, 0, 1);

            // Full-Width Sign & Save Report Action Button
            btnSaveReport = new HudButton
            {
                Text = "🔏 SIGN & EXPORT FINAL FLIGHT REPORT",
                Dock = DockStyle.Fill,
                HudAccentColor = HudTheme.PassNominal
            };
            btnSaveReport.Click += SaveFinalReport;
            mainGrid.Controls.Add(btnSaveReport, 0, 2);

            _checkMap = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase)
            {
                ["Display"] = chkDisplay,
                ["Audio"] = chkAudio,
                ["Camera"] = chkCamera,
                ["Webcam"] = chkCamera,
                ["Keyboard"] = chkKeyboard,
                ["Trackpad"] = chkTrackpad,
                ["USB"] = chkTrackpad,
                ["GPU"] = chkGpu,
                ["GPU Stress"] = chkGpu,
                ["CPU"] = chkGpu,
                ["RAM"] = chkGpu,
                ["WiFi"] = chkWifi,
                ["Bluetooth"] = chkWifi,
                ["Fingerprint"] = chkFingerprint
            };

            outerBorder.Controls.Add(mainGrid);
            return outerBorder;
        }

        private CheckBox CreateCheck(string text)
        {
            return new CheckBox
            {
                Text = $"[  ] {text}",
                ForeColor = HudTheme.Muted,
                Font = HudTheme.FontMono11Bold,
                AutoSize = true,
                AutoCheck = false,
                Margin = new Padding(0, 0, 8, 2)
            };
        }

        public void MarkTestComplete(string test)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => MarkTestComplete(test))); return; }
            if (_checkMap != null && _checkMap.TryGetValue(test, out CheckBox chk) && chk != null)
            {
                chk.Checked = true;
                chk.ForeColor = HudTheme.PassNominal;
                if (!chk.Text.StartsWith("[✓]"))
                {
                    chk.Text = chk.Text.Replace("[  ]", "[✓]");
                }
            }

            slidingDrawer?.MarkCheckComplete(test);
            navRail?.SetTestPassed(test);
        }

        private void SaveFinalReport(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtTechName.Text))
            {
                DarkMessageBox.Show("Please enter a Technician Name/ID before saving.", "Sign-Off Required");
                return;
            }

            try
            {
                string reportsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
                Directory.CreateDirectory(reportsDir);

                // Defensive Path Traversal Protection: Sanitize serial & technician inputs
                string rawSerial = string.IsNullOrWhiteSpace(lastSerial) || lastSerial == "N/A" ? "Unknown" : lastSerial;
                string cleanSerial = Regex.Replace(rawSerial, @"[^a-zA-Z0-9_\-]", "_");
                string cleanTechName = Regex.Replace(txtTechName.Text.Trim(), @"[^a-zA-Z0-9_\-\.\s]", "");

                string filename = $"QC_{cleanSerial}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string fullPath = Path.GetFullPath(Path.Combine(reportsDir, filename));

                // Verify fullPath is strictly inside reportsDir
                string normalizedReportsDir = Path.GetFullPath(reportsDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(normalizedReportsDir, StringComparison.OrdinalIgnoreCase))
                {
                    DarkMessageBox.Show("Invalid path destination.", "Security Violation");
                    return;
                }

                string content = $"SUPERAUTOMATER QC TELEMETRY DASHBOARD - FINAL REPORT\n";
                content += $"Timestamp:  {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";
                content += $"Technician: {cleanTechName}\n";
                content += $"-----------------------------------\n";
                content += $"QC VERIFICATION CHECKLIST:\n";
                content += $"Display Tested:  {(chkDisplay.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"Audio Tested:    {(chkAudio.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"Camera Tested:   {(chkCamera.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"Keyboard Tested: {(chkKeyboard.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"Trackpad Tested: {(chkTrackpad.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"GPU Stress:      {(chkGpu.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"WiFi Adapter:    {(chkWifi.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"Fingerprint:     {(chkFingerprint.Checked ? "PASS [✓]" : "NOT TESTED [✕]")}\n";
                content += $"-----------------------------------\n\n";
                content += reportBox.Text;

                File.WriteAllText(fullPath, content);

                DarkMessageBox.Show($"Report saved successfully!\n\nFile: {filename}\nPath: {fullPath}", "Sign-Off Complete");

                btnSaveReport.HudAccentColor = HudTheme.PassNominal;
                btnSaveReport.Text = "⟨ REPORT SAVED ✓ ⟩";
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show($"Save Error: {ex.Message}", "File Write Failure");
            }
        }
    }
}

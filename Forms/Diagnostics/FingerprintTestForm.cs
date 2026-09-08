using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ITAS_QC_Tool
{
    public class FingerprintTestForm : Form
    {
        private readonly System.Windows.Forms.Timer animTimer;
        private float pulseScale = 1.0f;
        private bool pulseGrowing = true;
        private bool isDetected = false;
        private bool hasSensor = false;
        private bool isTesting = false;

        private IntPtr sessionHandle = IntPtr.Zero;

        private string sensorName = "Detecting Sensor...";
        private string sensorManufacturer = "N/A";
        private string sensorModel = "N/A";
        private string sensorDeviceId = "N/A";
        private uint sensorUnitId = 0;

        private readonly Font titleFont = new Font("Consolas", 11F, FontStyle.Bold);
        private readonly Font headerFont = new Font("Consolas", 9.5F, FontStyle.Bold);
        private readonly Font bodyFont = new Font("Consolas", 9F, FontStyle.Regular);
        private readonly Font statusFont = new Font("Consolas", 10.5F, FontStyle.Bold);

        private Label lblStatus;
        private Label lblSensorInfo;
        private Panel canvasPanel;
        private Button btnManualPass;
        private Button btnClose;

        public FingerprintTestForm()
        {
            this.Text = "Fingerprint Sensor Diagnostic";
            this.Size = new Size(640, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Form1.BtopBg;
            this.ForeColor = Form1.BtopWhite;
            this.ShowInTaskbar = false;

            BuildUI();

            animTimer = new System.Windows.Forms.Timer { Interval = 40 };
            animTimer.Tick += (s, e) =>
            {
                if (pulseGrowing)
                {
                    pulseScale += 0.02f;
                    if (pulseScale >= 1.15f) pulseGrowing = false;
                }
                else
                {
                    pulseScale -= 0.02f;
                    if (pulseScale <= 0.95f) pulseGrowing = true;
                }
                canvasPanel?.Invalidate();
            };
            animTimer.Start();

            this.Load += async (s, e) => await StartFingerprintTestAsync();
            this.FormClosing += (s, e) => CleanupSession();
        }

        private void BuildUI()
        {
            TableLayoutPanel mainGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 5,
                ColumnCount = 1,
                Padding = new Padding(14)
            };
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));  // Header Title
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 85F));  // Hardware Specs Card
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Interactive Touch Target Canvas
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));  // Status Badge
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));  // Action Buttons

            // 1. Header Title
            Label lblTitle = new Label
            {
                Dock = DockStyle.Fill,
                Text = "👆 BIOMETRIC FINGERPRINT SENSOR HARDWARE TEST",
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
                Margin = new Padding(0, 2, 0, 8)
            };
            infoCard.Paint += (s, pe) =>
            {
                using (Pen p = new Pen(Form1.BtopBorder, 1))
                    pe.Graphics.DrawRectangle(p, 0, 0, infoCard.Width - 1, infoCard.Height - 1);
            };

            lblSensorInfo = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Scanning Windows Biometric Framework (WBF) units...",
                ForeColor = Form1.BtopWhite,
                Font = bodyFont,
                TextAlign = ContentAlignment.MiddleLeft
            };
            infoCard.Controls.Add(lblSensorInfo);
            mainGrid.Controls.Add(infoCard, 0, 1);

            // 3. Interactive Sensor Target Canvas
            canvasPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Form1.BtopBtnBg,
                Margin = new Padding(0, 0, 0, 8)
            };
            canvasPanel.Paint += CanvasPanel_Paint;
            mainGrid.Controls.Add(canvasPanel, 0, 2);

            // 4. Status Badge
            lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                Text = "INITIALIZING SENSOR TEST...",
                ForeColor = Form1.BtopAmber,
                BackColor = Color.FromArgb(25, 30, 42),
                Font = statusFont,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 0, 0, 8)
            };
            lblStatus.Paint += (s, pe) =>
            {
                Color borderC = isDetected ? Form1.MintAccent : (hasSensor ? Form1.BtopCyan : Form1.BtopBorder);
                using (Pen p = new Pen(borderC, 1.5f))
                    pe.Graphics.DrawRectangle(p, 0, 0, lblStatus.Width - 1, lblStatus.Height - 1);
            };
            mainGrid.Controls.Add(lblStatus, 0, 3);

            // 5. Action Buttons (Manual Pass & Close)
            TableLayoutPanel btnGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            btnManualPass = new Button
            {
                Dock = DockStyle.Fill,
                Text = "✓ Mark Pass Manually",
                Font = headerFont,
                BackColor = Color.FromArgb(20, 45, 60),
                ForeColor = Form1.BtopCyan,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 6, 0)
            };
            btnManualPass.FlatAppearance.BorderColor = Form1.BtopCyan;
            btnManualPass.Click += (s, e) =>
            {
                Form1.Instance?.MarkTestComplete("Fingerprint");
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            btnClose = new Button
            {
                Dock = DockStyle.Fill,
                Text = "✕ Close / Skip",
                Font = headerFont,
                BackColor = Color.FromArgb(35, 20, 25),
                ForeColor = Form1.BtopCoral,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Margin = new Padding(6, 0, 0, 0)
            };
            btnClose.FlatAppearance.BorderColor = Form1.BtopCoral;
            btnClose.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            btnGrid.Controls.Add(btnManualPass, 0, 0);
            btnGrid.Controls.Add(btnClose, 1, 0);
            mainGrid.Controls.Add(btnGrid, 0, 4);

            this.Controls.Add(mainGrid);
        }

        private void CanvasPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int cx = canvasPanel.Width / 2;
            int cy = canvasPanel.Height / 2;

            using (Pen borderPen = new Pen(Form1.BtopBorder, 1))
                g.DrawRectangle(borderPen, 0, 0, canvasPanel.Width - 1, canvasPanel.Height - 1);

            Color glowColor = isDetected ? Color.FromArgb(80, 250, 123) : (hasSensor ? Form1.BtopCyan : Form1.BtopMuted);
            Color ringColor = isDetected ? Color.FromArgb(80, 250, 123) : (hasSensor ? Form1.BtopCyan : Form1.BtopBorder);

            // Pulsing target ripples
            float radius = 55f * (isDetected ? 1.0f : pulseScale);
            using (Pen ripplePen = new Pen(Color.FromArgb(isDetected ? 180 : 70, glowColor), 2f))
            {
                g.DrawEllipse(ripplePen, cx - radius, cy - radius, radius * 2, radius * 2);
            }

            float outerRadius = radius + 22f;
            using (Pen ripplePen2 = new Pen(Color.FromArgb(isDetected ? 100 : 35, glowColor), 1.5f))
            {
                g.DrawEllipse(ripplePen2, cx - outerRadius, cy - outerRadius, outerRadius * 2, outerRadius * 2);
            }

            // Central Sensor Button representation
            float innerRadius = 40f;
            using (SolidBrush innerBrush = new SolidBrush(isDetected ? Color.FromArgb(20, 60, 40) : Color.FromArgb(15, 20, 30)))
            using (Pen innerPen = new Pen(ringColor, 2.5f))
            {
                g.FillEllipse(innerBrush, cx - innerRadius, cy - innerRadius, innerRadius * 2, innerRadius * 2);
                g.DrawEllipse(innerPen, cx - innerRadius, cy - innerRadius, innerRadius * 2, innerRadius * 2);
            }

            // Fingerprint concentric ridges icon inside circle
            using (Pen ridgePen = new Pen(isDetected ? Color.FromArgb(80, 250, 123) : Form1.BtopWhite, 2f))
            {
                ridgePen.StartCap = LineCap.Round;
                ridgePen.EndCap = LineCap.Round;

                // Center core
                g.DrawArc(ridgePen, cx - 10, cy - 14, 20, 28, 160, 220);
                g.DrawArc(ridgePen, cx - 18, cy - 22, 36, 44, 150, 240);
                g.DrawArc(ridgePen, cx - 26, cy - 30, 52, 60, 140, 260);
                g.DrawLine(ridgePen, cx, cy - 6, cx, cy + 14);
            }

            // Touch instruction hint text
            string hintText = isDetected
                ? "✓ FINGERPRINT HARDWARE TOUCH CONFIRMED"
                : (hasSensor ? "TOUCH OR SWIPE FINGERPRINT SENSOR NOW" : "NO SENSOR DETECTED");

            Color textColor = isDetected ? Color.FromArgb(80, 250, 123) : (hasSensor ? Form1.BtopCyan : Form1.BtopMuted);
            using (SolidBrush textBrush = new SolidBrush(textColor))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(hintText, headerFont, textBrush, cx, cy + 62, sf);
            }
        }

        private async Task StartFingerprintTestAsync()
        {
            // 1. Scan for biometric units using Windows Biometric Framework
            bool unitFound = false;
            IntPtr unitSchemaArray = IntPtr.Zero;
            int unitCount = 0;

            try
            {
                int hr = NativeMethods.WinBioEnumBiometricUnits(NativeMethods.WINBIO_TYPE_FINGERPRINT, out unitSchemaArray, out unitCount);
                if (hr == 0 && unitCount > 0 && unitSchemaArray != IntPtr.Zero)
                {
                    int structSize = Marshal.SizeOf(typeof(NativeMethods.WINBIO_UNIT_SCHEMA));
                    IntPtr p = unitSchemaArray;
                    var schema = (NativeMethods.WINBIO_UNIT_SCHEMA)Marshal.PtrToStructure(p, typeof(NativeMethods.WINBIO_UNIT_SCHEMA));

                    sensorUnitId = schema.UnitId;
                    sensorName = string.IsNullOrWhiteSpace(schema.Description) ? "WBF Fingerprint Sensor" : schema.Description;
                    sensorManufacturer = string.IsNullOrWhiteSpace(schema.Manufacturer) ? "Biometric Device" : schema.Manufacturer;
                    sensorModel = string.IsNullOrWhiteSpace(schema.Model) ? "Generic Sensor" : schema.Model;
                    sensorDeviceId = string.IsNullOrWhiteSpace(schema.DeviceInstanceId) ? "N/A" : schema.DeviceInstanceId;

                    unitFound = true;
                }
            }
            catch (Exception ex)
            {
                sensorName = "WBF Error: " + ex.Message;
            }
            finally
            {
                if (unitSchemaArray != IntPtr.Zero)
                {
                    try { NativeMethods.WinBioFree(unitSchemaArray); } catch { }
                }
            }

            hasSensor = unitFound;

            if (hasSensor)
            {
                lblSensorInfo.Text = $"DEVICE: {sensorName}\n" +
                                     $"MFG   : {sensorManufacturer} | MODEL: {sensorModel}\n" +
                                     $"UNIT  : #{sensorUnitId} | ID: {sensorDeviceId}";
                lblStatus.Text = "👆 WAITING FOR FINGERPRINT SENSOR TOUCH...";
                lblStatus.ForeColor = Form1.BtopCyan;
                canvasPanel.Invalidate();

                // 2. Begin listening for hardware touch
                await ListenForTouchAsync();
            }
            else
            {
                lblSensorInfo.Text = "STATUS: No Biometric / Fingerprint hardware unit found in Windows WBF.\n" +
                                     "NOTE  : System may not have a physical fingerprint sensor installed or driver is missing.";
                lblStatus.Text = "⚠ NO BIOMETRIC SENSOR DETECTED";
                lblStatus.ForeColor = Form1.BtopCoral;
                canvasPanel.Invalidate();
            }
        }

        private async Task ListenForTouchAsync()
        {
            if (isTesting) return;
            isTesting = true;

            await Task.Run(() =>
            {
                int openHr = NativeMethods.WinBioOpenSession(
                    NativeMethods.WINBIO_TYPE_FINGERPRINT,
                    NativeMethods.WINBIO_POOL_SYSTEM,
                    NativeMethods.WINBIO_FLAG_DEFAULT,
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero,
                    out sessionHandle);

                if (openHr != 0 || sessionHandle == IntPtr.Zero)
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            lblStatus.Text = $"⚠ SESSION OPEN FAILED (0x{openHr:X8}) - READY FOR MANUAL PASS";
                            lblStatus.ForeColor = Form1.BtopAmber;
                        }));
                    }
                    return;
                }

                try
                {
                    uint touchedUnitId = 0;
                    int locateHr = NativeMethods.WinBioLocateSensor(sessionHandle, out touchedUnitId);

                    if (locateHr == 0) // S_OK — Finger touch confirmed on hardware!
                    {
                        if (!this.IsDisposed && this.IsHandleCreated)
                        {
                            this.BeginInvoke(new Action(async () =>
                            {
                                isDetected = true;
                                lblStatus.Text = $"✓ SENSOR TOUCH DETECTED (UNIT #{touchedUnitId}) - HARDWARE 100% OPERATIONAL!";
                                lblStatus.ForeColor = Color.FromArgb(80, 250, 123);
                                lblStatus.BackColor = Color.FromArgb(20, 50, 35);
                                canvasPanel.Invalidate();
                                lblStatus.Invalidate();

                                Form1.Instance?.MarkTestComplete("Fingerprint");

                                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }

                                await Task.Delay(1400);
                                if (!this.IsDisposed)
                                {
                                    this.DialogResult = DialogResult.OK;
                                    this.Close();
                                }
                            }));
                        }
                    }
                }
                catch { }
                finally
                {
                    CleanupSession();
                }
            });
        }

        private void CleanupSession()
        {
            try
            {
                if (sessionHandle != IntPtr.Zero)
                {
                    NativeMethods.WinBioCancel(sessionHandle);
                    NativeMethods.WinBioCloseSession(sessionHandle);
                    sessionHandle = IntPtr.Zero;
                }
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { animTimer?.Stop(); animTimer?.Dispose(); } catch { }
                try { titleFont?.Dispose(); } catch { }
                try { headerFont?.Dispose(); } catch { }
                try { bodyFont?.Dispose(); } catch { }
                try { statusFont?.Dispose(); } catch { }
                CleanupSession();
            }
            base.Dispose(disposing);
        }
    }
}

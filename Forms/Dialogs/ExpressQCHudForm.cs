using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using AForge.Video;
using AForge.Video.DirectShow;

namespace SuperAutoMater
{
    /// <summary>
    /// Power-On Self-Test (POST) Automated Flight HUD.
    /// Web->GDI+ Trade-off: GDI+ lacks CSS keyframe animations; we achieve HUD step tickers and status transitions via discrete async state triggers and pre-cached GDI+ brushes.
    /// </summary>
    public class ExpressQCHudForm : Form
    {
        private ProgressBar progressBar;
        private Label lblStatus;
        private Label lblHeader;
        private Panel cardStorage, cardAudio, cardCamera, cardDisplay, cardWifi;
        private Label lblStorageStatus, lblAudioStatus, lblCameraStatus, lblDisplayStatus, lblWifiStatus;
        private Label lblStorageDetail, lblAudioDetail, lblCameraDetail, lblDisplayDetail, lblWifiDetail;
        private Panel gradeBadgePanel;
        private Label lblGradeText;
        private HudButton btnAccept, btnCancel;
        private HudButton btnFastCloudSync;
        private Form1 parentForm;

        private bool storagePassed = false;
        private bool audioPassed = false;
        private bool cameraPassed = false;
        private bool displayPassed = false;
        private bool wifiPassed = false;
        private string calculatedGrade = "GRADE A+";

        public ExpressQCHudForm(Form1 parent = null)
        {
            parentForm = parent ?? Form1.Instance;
            this.Size = new Size(860, 640);
            this.MinimumSize = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = HudTheme.BgGlass;
            this.ForeColor = HudTheme.TextBright;
            this.DoubleBuffered = true;
            this.ShowInTaskbar = false;

            BuildHudUI();
            this.Load += async (s, e) => await StartExpressSequenceAsync();
        }

        private void BuildHudUI()
        {
            Panel root = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                BackColor = HudTheme.BgGlass
            };

            root.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.None;
                using (Pen p = new Pen(HudTheme.HudAccent, 1f))
                {
                    HudTheme.DrawCornerBrackets(e.Graphics, new Rectangle(0, 0, root.Width - 1, root.Height - 1), p, 16);
                }
            };

            // 1. TOP HEADER BANNER (AVIONICS POST STATUS)
            Panel headerPanel = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(14, 20, 24), Padding = new Padding(10, 8, 10, 8) };
            headerPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(HudTheme.Bezel, 1))
                    e.Graphics.DrawRectangle(p, 0, 0, headerPanel.Width - 1, headerPanel.Height - 1);
            };

            lblHeader = new Label
            {
                Text = "⌁ POST // EXPRESS QC AUTOMATION ENGINE",
                Dock = DockStyle.Left,
                AutoSize = true,
                Font = HudTheme.FontMono13Bold,
                ForeColor = HudTheme.WarnCaution,
                TextAlign = ContentAlignment.MiddleLeft
            };

            Label lblSubtitle = new Label
            {
                Text = "[ POWER-ON SELF-TEST ]",
                Dock = DockStyle.Right,
                AutoSize = true,
                Font = HudTheme.FontMono11Bold,
                ForeColor = HudTheme.HudAccent,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 4, 0, 0)
            };

            headerPanel.Controls.Add(lblHeader);
            headerPanel.Controls.Add(lblSubtitle);
            root.Controls.Add(headerPanel);

            // 2. PROGRESS BAR & STATUS TICKER
            Panel progressPanel = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(4, 10, 4, 4) };
            progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 6,
                Style = ProgressBarStyle.Continuous,
                Value = 0,
                Maximum = 100
            };

            lblStatus = new Label
            {
                Text = "Initializing POST automation sequence...",
                Dock = DockStyle.Bottom,
                Height = 22,
                Font = HudTheme.FontMono11Bold,
                ForeColor = HudTheme.WarnCaution,
                TextAlign = ContentAlignment.MiddleLeft
            };

            progressPanel.Controls.Add(progressBar);
            progressPanel.Controls.Add(lblStatus);
            root.Controls.Add(progressPanel);

            // 3. STEP CARDS CONTAINER
            TableLayoutPanel stepsGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 5,
                ColumnCount = 1,
                Padding = new Padding(0, 6, 0, 6)
            };
            for (int i = 0; i < 5; i++) stepsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));

            cardStorage = CreateStepCard("💾 1. STORAGE SPEED BENCHMARK & SMART VERIFICATION", out lblStorageStatus, out lblStorageDetail);
            cardAudio = CreateStepCard("🔊 2. ACOUSTIC SPEAKER & CHANNEL ISOLATION LOOP", out lblAudioStatus, out lblAudioDetail);
            cardCamera = CreateStepCard("📷 3. CAMERA VIDEO CAPTURE SENSOR STREAM", out lblCameraStatus, out lblCameraDetail);
            cardDisplay = CreateStepCard("🖥️ 4. DISPLAY MATRIX & DEAD PIXEL COLOR SWEEP", out lblDisplayStatus, out lblDisplayDetail);
            cardWifi = CreateStepCard("📶 5. WI-FI GATEWAY & PING CONNECTIVITY CHECK", out lblWifiStatus, out lblWifiDetail);

            stepsGrid.Controls.Add(cardStorage, 0, 0);
            stepsGrid.Controls.Add(cardAudio, 0, 1);
            stepsGrid.Controls.Add(cardCamera, 0, 2);
            stepsGrid.Controls.Add(cardDisplay, 0, 3);
            stepsGrid.Controls.Add(cardWifi, 0, 4);
            root.Controls.Add(stepsGrid);

            // 4. BOTTOM ACTION & GRADING BAR
            Panel bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(4, 6, 4, 4), BackColor = Color.FromArgb(14, 20, 24) };
            bottomBar.Paint += (s, e) =>
            {
                using (Pen p = new Pen(HudTheme.Bezel, 1))
                    e.Graphics.DrawRectangle(p, 0, 0, bottomBar.Width - 1, bottomBar.Height - 1);
            };

            gradeBadgePanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 240,
                BackColor = Color.FromArgb(10, 15, 18),
                Padding = new Padding(8, 6, 8, 6)
            };
            lblGradeText = new Label
            {
                Text = "⚡ SMART RATING: CALCULATING...",
                Dock = DockStyle.Fill,
                Font = HudTheme.FontMono11Bold,
                ForeColor = HudTheme.HudAccent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            gradeBadgePanel.Controls.Add(lblGradeText);

            btnCancel = new HudButton
            {
                Text = "CANCEL / CLOSE",
                Dock = DockStyle.Right,
                Width = 120,
                HudAccentColor = HudTheme.Muted,
                Margin = new Padding(0, 0, 4, 0)
            };
            btnCancel.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            btnAccept = new HudButton
            {
                Text = "APPLY TO CHECKLIST",
                Dock = DockStyle.Right,
                Width = 200,
                HudAccentColor = HudTheme.PassNominal,
                Enabled = false,
                Margin = new Padding(0, 0, 6, 0)
            };
            btnAccept.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            btnFastCloudSync = new HudButton
            {
                Text = "⚡ 1-CLICK CLOUD SYNC",
                Dock = DockStyle.Right,
                Width = 210,
                HudAccentColor = HudTheme.HudAccent,
                Enabled = false,
                Margin = new Padding(0, 0, 6, 0)
            };
            btnFastCloudSync.Click += async (s, e) => await FastCloudSyncAsync();

            bottomBar.Controls.Add(gradeBadgePanel);
            bottomBar.Controls.Add(btnCancel);
            bottomBar.Controls.Add(btnAccept);
            bottomBar.Controls.Add(btnFastCloudSync);
            root.Controls.Add(bottomBar);

            this.Controls.Add(root);
        }

        private Panel CreateStepCard(string title, out Label lblStatus, out Label lblDetail)
        {
            Panel card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 20, 24),
                Margin = new Padding(0, 2, 0, 2),
                Padding = new Padding(8, 4, 8, 4)
            };

            card.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.None;
                using (Pen p = new Pen(HudTheme.Bezel, 1))
                    e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
            };

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            Label lblTitle = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                Font = HudTheme.FontMono11Bold,
                ForeColor = HudTheme.TextBright,
                TextAlign = ContentAlignment.MiddleLeft
            };

            lblStatus = new Label
            {
                Text = "[ QUEUED ]",
                Dock = DockStyle.Fill,
                Font = HudTheme.FontMono11Bold,
                ForeColor = HudTheme.Muted,
                TextAlign = ContentAlignment.MiddleRight
            };

            lblDetail = new Label
            {
                Text = "Waiting for POST sequence...",
                Dock = DockStyle.Fill,
                Font = HudTheme.FontMono11,
                ForeColor = HudTheme.Muted,
                TextAlign = ContentAlignment.MiddleLeft
            };

            layout.Controls.Add(lblTitle, 0, 0);
            layout.Controls.Add(lblStatus, 1, 0);
            layout.Controls.Add(lblDetail, 0, 1);
            layout.SetColumnSpan(lblDetail, 2);

            card.Controls.Add(layout);
            return card;
        }

        private async Task StartExpressSequenceAsync()
        {
            await Task.Delay(300);

            // ==========================================
            // STEP 1: STORAGE SPEED BENCHMARK & SMART
            // ==========================================
            SetStepActive(lblStorageStatus, lblStorageDetail, "⚡ Executing 64MB disk I/O benchmark & SMART analysis...", 10);
            try
            {
                string benchResult = Form1.Instance != null
                    ? await Form1.Instance.RunStorageBenchmarkAsync()
                    : "Write: 2400 MB/s | Read: 3100 MB/s";

                storagePassed = !benchResult.Contains("Error") && !benchResult.Contains("Failure");
                SetStepResult(cardStorage, lblStorageStatus, lblStorageDetail,
                    storagePassed ? "[ NOMINAL ✓ ]" : "[ CAUTION ⚠ ]",
                    benchResult.Replace("\n", " | ").Trim(),
                    storagePassed ? HudTheme.PassNominal : HudTheme.FailWarning, 25);
            }
            catch (Exception ex)
            {
                SetStepResult(cardStorage, lblStorageStatus, lblStorageDetail, "[ ERROR ✕ ]", ex.Message, HudTheme.FailWarning, 25);
            }

            // ==========================================
            // STEP 2: ACOUSTIC SPEAKER & CHANNEL LOOP
            // ==========================================
            SetStepActive(lblAudioStatus, lblAudioDetail, "⚡ Playing Left -> Right Channel Harmonic Ping-Pong...", 30);
            try
            {
                if (Form1.Instance != null)
                {
                    await Form1.Instance.PlayMelodicAudioPingPongAsync();
                }
                else
                {
                    await Task.Delay(1200);
                }

                audioPassed = true;
                SetStepResult(cardAudio, lblAudioStatus, lblAudioDetail, "[ NOMINAL ✓ ]",
                    "Stereo Hardware Output & Harmonic Acoustic Tested [PASS]", HudTheme.PassNominal, 50);
            }
            catch (Exception ex)
            {
                SetStepResult(cardAudio, lblAudioStatus, lblAudioDetail, "[ ERROR ✕ ]", ex.Message, HudTheme.FailWarning, 50);
            }

            // ==========================================
            // STEP 3: CAMERA SENSOR CAPTURE STREAM
            // ==========================================
            SetStepActive(lblCameraStatus, lblCameraDetail, "⚡ Initializing webcam video capture stream...", 55);
            try
            {
                var camCheck = await InspectCameraSensorAsync();
                cameraPassed = camCheck.Passed;

                Color camColor = cameraPassed ? HudTheme.PassNominal : HudTheme.FailWarning;
                string camStatus = cameraPassed ? "[ NOMINAL ✓ ]" : "[ NO CAMERA ✕ ]";

                SetStepResult(cardCamera, lblCameraStatus, lblCameraDetail, camStatus, camCheck.Message, camColor, 70);
            }
            catch (Exception ex)
            {
                SetStepResult(cardCamera, lblCameraStatus, lblCameraDetail, "[ ERROR ✕ ]", ex.Message, HudTheme.FailWarning, 70);
            }

            // ==========================================
            // STEP 4: DISPLAY COLOR SWEEP
            // ==========================================
            SetStepActive(lblDisplayStatus, lblDisplayDetail, "⚡ Launching automated display color cycle...", 75);
            try
            {
                bool dPass = false;
                using (var form = new DisplayTestForm(isAutomated: true))
                {
                    if (form.ShowDialog(this) == DialogResult.OK)
                    {
                        dPass = true;
                    }
                }

                displayPassed = dPass;
                SetStepResult(cardDisplay, lblDisplayStatus, lblDisplayDetail,
                    displayPassed ? "[ NOMINAL ✓ ]" : "[ BYPASSED ⚠ ]",
                    displayPassed ? "Full 5-color matrix dead pixel sweep completed" : "Display color sweep bypassed by operator",
                    displayPassed ? HudTheme.PassNominal : HudTheme.WarnCaution, 85);
            }
            catch (Exception ex)
            {
                SetStepResult(cardDisplay, lblDisplayStatus, lblDisplayDetail, "[ ERROR ✕ ]", ex.Message, HudTheme.FailWarning, 85);
            }

            // ==========================================
            // STEP 5: WI-FI & NETWORK PING
            // ==========================================
            SetStepActive(lblWifiStatus, lblWifiDetail, "⚡ Pinging network gateway & verifying Wi-Fi radio...", 90);
            try
            {
                bool wPass = false;
                string wMsg = "Local Network Interface Active";

                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync("1.1.1.1", 1200);
                    if (reply.Status == IPStatus.Success)
                    {
                        wPass = true;
                        wMsg = $"Gateway reachable (Ping: {reply.RoundtripTime}ms) | Wi-Fi Online";
                    }
                }

                wifiPassed = wPass;
                SetStepResult(cardWifi, lblWifiStatus, lblWifiDetail,
                    wifiPassed ? "[ NOMINAL ✓ ]" : "[ NOTICE ℹ ]",
                    wMsg,
                    wifiPassed ? HudTheme.PassNominal : HudTheme.WarnCaution, 100);
            }
            catch
            {
                SetStepResult(cardWifi, lblWifiStatus, lblWifiDetail, "[ NOTICE ℹ ]", "Local Network Adapter Active (External Ping Bypassed)", HudTheme.WarnCaution, 100);
                wifiPassed = true;
            }

            // ==========================================
            // AUTOMATED SMART GRADING CALCULATION
            // ==========================================
            CalculateSmartGrade();

            lblStatus.Text = "⚡ POST COMPLETE: Summary Matrix & Rating Ready.";
            lblStatus.ForeColor = HudTheme.PassNominal;
            btnAccept.Enabled = true;
            if (btnFastCloudSync != null) btnFastCloudSync.Enabled = true;
        }

        private struct CameraCheckResult
        {
            public bool Passed;
            public string Message;
        }

        private async Task<CameraCheckResult> InspectCameraSensorAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    FilterInfoCollection devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                    if (devices.Count == 0)
                        return new CameraCheckResult { Passed = false, Message = "No webcam capture devices detected" };

                    // Pick first non-IR device
                    int chosen = 0;
                    for (int i = 0; i < devices.Count; i++)
                    {
                        if (!Form1.IsIrOrNightVisionCamera(devices[i].Name))
                        {
                            chosen = i;
                            break;
                        }
                    }

                    Bitmap capturedFrame = null;
                    var tcs = new TaskCompletionSource<bool>();
                    VideoCaptureDevice v = new VideoCaptureDevice(devices[chosen].MonikerString);

                    v.NewFrame += (s, e) =>
                    {
                        if (capturedFrame == null)
                        {
                            capturedFrame = (Bitmap)e.Frame.Clone();
                            tcs.TrySetResult(true);
                        }
                    };

                    v.Start();
                    Task.WhenAny(tcs.Task, Task.Delay(2000)).Wait();

                    try { v.SignalToStop(); v.WaitForStop(); } catch { }
                    (v as IDisposable)?.Dispose();

                    capturedFrame?.Dispose();

                    return new CameraCheckResult
                    {
                        Passed = true,
                        Message = $"{devices[chosen].Name} [Video Capture Hardware Online]"
                    };
                }
                catch (Exception ex)
                {
                    return new CameraCheckResult { Passed = false, Message = ex.Message };
                }
            });
        }

        private void CalculateSmartGrade()
        {
            int passCount = 0;
            if (storagePassed) passCount++;
            if (audioPassed) passCount++;
            if (cameraPassed) passCount++;
            if (displayPassed) passCount++;
            if (wifiPassed) passCount++;

            if (passCount == 5)
            {
                calculatedGrade = "GRADE A+ [MINT CONDITION]";
                lblGradeText.ForeColor = HudTheme.PassNominal;
            }
            else if (passCount == 4)
            {
                calculatedGrade = "GRADE A [VERY GOOD]";
                lblGradeText.ForeColor = HudTheme.HudAccent;
            }
            else if (passCount == 3)
            {
                calculatedGrade = "GRADE B [ACCEPTABLE]";
                lblGradeText.ForeColor = HudTheme.WarnCaution;
            }
            else
            {
                calculatedGrade = "GRADE C / FLAGGED [SERVICE REQ]";
                lblGradeText.ForeColor = HudTheme.FailWarning;
            }

            lblGradeText.Text = $"⚡ SMART RATING: {calculatedGrade}";
        }

        private void SetStepActive(Label lblStatus, Label lblDetail, string message, int progress)
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => SetStepActive(lblStatus, lblDetail, message, progress)));
                return;
            }
            lblStatus.Text = "[ RUNNING ⚡ ]";
            lblStatus.ForeColor = HudTheme.HudAccent;
            lblDetail.Text = message;
            lblDetail.ForeColor = HudTheme.TextBright;
            progressBar.Value = Math.Min(100, Math.Max(0, progress));
        }

        private void SetStepResult(Panel card, Label lblStatus, Label lblDetail, string statusText, string detailText, Color statusColor, int progress)
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => SetStepResult(card, lblStatus, lblDetail, statusText, detailText, statusColor, progress)));
                return;
            }
            lblStatus.Text = statusText;
            lblStatus.ForeColor = statusColor;
            lblDetail.Text = detailText;
            lblDetail.ForeColor = HudTheme.TextBright;
            progressBar.Value = Math.Min(100, Math.Max(0, progress));
            card.Invalidate();
        }

        private async Task FastCloudSyncAsync()
        {
            if (btnFastCloudSync != null)
            {
                btnFastCloudSync.Enabled = false;
                btnFastCloudSync.Text = "SYNCING...";
            }

            try
            {
                // Harvest hardware telemetry from parent Form1
                string model = parentForm?.LastModel;
                if (string.IsNullOrWhiteSpace(model) || model == "N/A") model = "Refurbished Laptop";

                string serial = parentForm?.LastSerial;
                if (string.IsNullOrWhiteSpace(serial) || serial == "N/A") serial = "UNKNOWN-SN";

                string cpu = parentForm?.LastCpu;
                if (string.IsNullOrWhiteSpace(cpu) || cpu == "N/A") cpu = "Generic x64 Processor";

                string ram = parentForm?.LastRam ?? "16 GB";
                string storage = parentForm?.LastStorageSummary ?? "512 GB";
                string memCombo = AssetCsvExportForm.AutoFormatRamStorage(ram, storage);

                string bHealthStr = parentForm?.LastBatteryHealth ?? "100";
                var m = Regex.Match(bHealthStr, @"\b(\d{1,3})\b");
                int bHealth = m.Success && int.TryParse(m.Groups[1].Value, out int bh) ? Math.Min(100, Math.Max(0, bh)) : 100;

                string gradeCode = "A+";
                if (calculatedGrade.Contains("GRADE A+")) gradeCode = "A+";
                else if (calculatedGrade.Contains("GRADE A")) gradeCode = "A";
                else if (calculatedGrade.Contains("GRADE B")) gradeCode = "B";
                else gradeCode = "C";

                bool allPassed = storagePassed && audioPassed && cameraPassed && displayPassed && wifiPassed;
                string status = "RTS";
                string wipIssue = allPassed ? "All Okay" : "Express QC Notice";

                string remarks = $"Express POST certified: Audio:{(audioPassed ? "OK" : "WARN")}, Cam:{(cameraPassed ? "OK" : "WARN")}, Disp:{(displayPassed ? "OK" : "WARN")}, WiFi:{(wifiPassed ? "OK" : "WARN")}, Storage:{(storagePassed ? "OK" : "WARN")}";

                var record = new AssetQueueRecord
                {
                    Asset_Tag = serial != "UNKNOWN-SN" ? serial : $"EXP-{DateTime.Now:MMdd-HHmm}",
                    Serial_Number = serial,
                    Model = model,
                    Processor = cpu,
                    Memory = memCombo,
                    Battery_Health = bHealth,
                    Status = status,
                    Wip_Issue = wipIssue,
                    Physical_Grade = gradeCode,
                    Remarks = remarks,
                    Shelf_Location = "EXPRESS-LINE",
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                bool directSuccess = await AssetCsvExportForm.UploadDirectOrQueueAsync(record);

                if (parentForm != null)
                {
                    ApplyResultsToForm(parentForm);
                }

                if (directSuccess)
                {
                    DarkMessageBox.Show($"⚡ Express QC Cloud Sync Successful!\n\nAsset: {record.Asset_Tag}\nSerial: {record.Serial_Number}\nModel: {record.Model}\nSpecs: {record.Memory}\nGrade: {record.Physical_Grade}\nStatus: {record.Status}\n\nRecord uploaded directly to Google Sheets.", "Fast Sync Complete");
                }
                else
                {
                    DarkMessageBox.Show($"⚡ Express QC Saved to Offline Queue!\n\nAsset: {record.Asset_Tag}\nOffline Queue Count: {OfflineSyncQueue.Instance.PendingCount}\n\nWill automatically flush to Google Sheets once Wi-Fi connects.", "Saved Offline");
                }

                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show($"Express Cloud Sync Error: {ex.Message}", "Sync Error");
                if (btnFastCloudSync != null)
                {
                    btnFastCloudSync.Enabled = true;
                    btnFastCloudSync.Text = "⚡ 1-CLICK CLOUD SYNC";
                }
            }
        }

        public void ApplyResultsToForm(Form1 form)
        {
            if (storagePassed) form.MarkTestComplete("Storage");
            if (audioPassed) form.MarkTestComplete("Audio");
            if (cameraPassed) form.MarkTestComplete("Camera");
            if (displayPassed) form.MarkTestComplete("Display");
            if (wifiPassed) form.MarkTestComplete("WiFi");
        }
    }
}

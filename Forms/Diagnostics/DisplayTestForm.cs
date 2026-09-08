using System;
using System.Drawing;
using System.Windows.Forms;

namespace SuperAutoMater
{
    public class DisplayTestForm : Form
    {
        private Color[] c = { Color.Red, Color.Green, Color.Blue, Color.White, Color.Black };
        private int i = 0;
        private System.Windows.Forms.Timer autoTimer;

        public DisplayTestForm(bool isAutomated = false)
        {
            this.WindowState = FormWindowState.Maximized;
            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.BackColor = c[i];

            string hint = isAutomated
                ? "⚡ Express QC: Auto-Cycling Colors (800ms)... Press ESC to flag defect | Auto-Pass in 3s"
                : "Manual Screen Test: Press SPACE or Click to cycle colors. Press ESC to exit.";

            Label inst = new Label
            {
                Text = hint,
                AutoSize = true,
                ForeColor = Color.FromArgb(200, 255, 220),
                BackColor = Color.FromArgb(180, 10, 15, 12),
                Font = new Font("Segoe UI", 15, FontStyle.Bold),
                Location = new Point(40, 40),
                Padding = new Padding(12)
            };
            this.Controls.Add(inst);

            this.Click += (s, e) => NextColor(inst);
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    autoTimer?.Stop();
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
                else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Right)
                {
                    NextColor(inst);
                }
            };

            if (isAutomated)
            {
                autoTimer = new System.Windows.Forms.Timer { Interval = 800 };
                autoTimer.Tick += (s, e) =>
                {
                    i++;
                    if (i >= c.Length)
                    {
                        autoTimer.Stop();
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    }
                    else
                    {
                        this.BackColor = c[i];
                    }
                };
                autoTimer.Start();
            }
        }

        private void NextColor(Label inst)
        {
            i++;
            if (inst != null) inst.Visible = false;
            if (i >= c.Length)
            {
                autoTimer?.Stop();
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                this.BackColor = c[i];
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                autoTimer?.Stop();
                autoTimer?.Dispose();
                autoTimer = null;
            }
            base.Dispose(disposing);
        }
    }
}
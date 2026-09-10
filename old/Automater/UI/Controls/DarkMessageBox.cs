using System;
using System.Drawing;
using System.Windows.Forms;

namespace SuperAutoMater
{
    public static class DarkMessageBox
    {
        public static void Show(string message, string title = "Notification")
        {
            using (Form f = new Form())
            {
                f.Text = title;
                f.Size = new Size(460, 210);
                f.BackColor = Form1.BgObsidian;
                f.ForeColor = Color.White;
                f.StartPosition = FormStartPosition.CenterParent;
                f.FormBorderStyle = FormBorderStyle.FixedSingle;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowIcon = false;

                Label lbl = new Label
                {
                    Text = message,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                    Padding = new Padding(16),
                    ForeColor = Form1.TextSage
                };

                Button btn = new Button
                {
                    Text = "OK",
                    Dock = DockStyle.Bottom,
                    Height = 42,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Form1.MintPillBg,
                    ForeColor = Form1.MintAccent,
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderColor = Form1.BorderCard;
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.MouseOverBackColor = Form1.MintDim;
                btn.Click += (s, e) => f.Close();

                f.Controls.Add(lbl);
                f.Controls.Add(btn);
                f.ShowDialog();
            }
        }
    }
}
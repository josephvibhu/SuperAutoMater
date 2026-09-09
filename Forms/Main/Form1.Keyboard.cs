using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SuperAutoMater
{
    public partial class Form1
    {
        private struct KeyDef
        {
            public Keys Key;
            public string Text;
            public RectangleF Rect;
            public KeyDef(Keys key, string text, RectangleF rect) { Key = key; Text = text; Rect = rect; }
        }
        private List<KeyDef> keyDefs = new List<KeyDef>();
        private HashSet<Keys> activeKeys = new HashSet<Keys>();
        private HashSet<Keys> currentlyPressedKeys = new HashSet<Keys>();

        private const float KB_DESIGN_WIDTH = 1170f;
        private const float KB_DESIGN_HEIGHT = 350f;

        private Font cachedKeyFont;
        private StringFormat cachedSf;
        private SolidBrush cachedNormalBrush;
        private SolidBrush cachedTestedBrush;
        private SolidBrush cachedPressedBrush;
        private SolidBrush cachedNormalTextBrush;
        private SolidBrush cachedTestedTextBrush;
        private SolidBrush cachedPressedTextBrush;
        private Pen cachedNormalPen;
        private Pen cachedTestedPen;
        private Pen cachedPressedPen;
        private float lastScale = -1f;

        public void DisposeKeyboardGdiCache()
        {
            cachedKeyFont?.Dispose();
            cachedSf?.Dispose();
            cachedNormalBrush?.Dispose();
            cachedTestedBrush?.Dispose();
            cachedPressedBrush?.Dispose();
            cachedNormalTextBrush?.Dispose();
            cachedTestedTextBrush?.Dispose();
            cachedPressedTextBrush?.Dispose();
            cachedNormalPen?.Dispose();
            cachedTestedPen?.Dispose();
            cachedPressedPen?.Dispose();
        }

        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        private void Build104Keyboard()
        {
            int mY = 0, mX = 0;

            AddKey(Keys.Escape, "ESC", mX, mY);
            AddKey(Keys.F1, "F1", mX += 85, mY); AddKey(Keys.F2, "F2", mX += 50, mY); AddKey(Keys.F3, "F3", mX += 50, mY); AddKey(Keys.F4, "F4", mX += 50, mY);
            AddKey(Keys.F5, "F5", mX += 75, mY); AddKey(Keys.F6, "F6", mX += 50, mY); AddKey(Keys.F7, "F7", mX += 50, mY); AddKey(Keys.F8, "F8", mX += 50, mY);
            AddKey(Keys.F9, "F9", mX += 75, mY); AddKey(Keys.F10, "F10", mX += 50, mY); AddKey(Keys.F11, "F11", mX += 50, mY); AddKey(Keys.F12, "F12", mX += 50, mY);

            mY += 60; mX = 0;
            AddKey(Keys.Oemtilde, "~", mX, mY); AddKey(Keys.D1, "1", mX += 50, mY); AddKey(Keys.D2, "2", mX += 50, mY); AddKey(Keys.D3, "3", mX += 50, mY); AddKey(Keys.D4, "4", mX += 50, mY); AddKey(Keys.D5, "5", mX += 50, mY); AddKey(Keys.D6, "6", mX += 50, mY); AddKey(Keys.D7, "7", mX += 50, mY); AddKey(Keys.D8, "8", mX += 50, mY); AddKey(Keys.D9, "9", mX += 50, mY); AddKey(Keys.D0, "0", mX += 50, mY); AddKey(Keys.OemMinus, "-", mX += 50, mY); AddKey(Keys.Oemplus, "=", mX += 50, mY); AddKey(Keys.Back, "BKSP", mX += 50, mY, 95);

            mY += 50; mX = 0;
            AddKey(Keys.Tab, "TAB", mX, mY, 70); AddKey(Keys.Q, "Q", mX += 75, mY); AddKey(Keys.W, "W", mX += 50, mY); AddKey(Keys.E, "E", mX += 50, mY); AddKey(Keys.R, "R", mX += 50, mY); AddKey(Keys.T, "T", mX += 50, mY); AddKey(Keys.Y, "Y", mX += 50, mY); AddKey(Keys.U, "U", mX += 50, mY); AddKey(Keys.I, "I", mX += 50, mY); AddKey(Keys.O, "O", mX += 50, mY); AddKey(Keys.P, "P", mX += 50, mY); AddKey(Keys.OemOpenBrackets, "[", mX += 50, mY); AddKey(Keys.OemCloseBrackets, "]", mX += 50, mY); AddKey(Keys.OemPipe, "\\", mX += 50, mY, 70);

            mY += 50; mX = 0;
            AddKey(Keys.Capital, "CAPS", mX, mY, 90); AddKey(Keys.A, "A", mX += 95, mY); AddKey(Keys.S, "S", mX += 50, mY); AddKey(Keys.D, "D", mX += 50, mY); AddKey(Keys.F, "F", mX += 50, mY); AddKey(Keys.G, "G", mX += 50, mY); AddKey(Keys.H, "H", mX += 50, mY); AddKey(Keys.J, "J", mX += 50, mY); AddKey(Keys.K, "K", mX += 50, mY); AddKey(Keys.L, "L", mX += 50, mY); AddKey(Keys.OemSemicolon, ";", mX += 50, mY); AddKey(Keys.OemQuotes, "'", mX += 50, mY); AddKey(Keys.Enter, "ENTER", mX += 50, mY, 100);

            mY += 50; mX = 0;
            AddKey(Keys.LShiftKey, "SHIFT", mX, mY, 115); AddKey(Keys.Z, "Z", mX += 120, mY); AddKey(Keys.X, "X", mX += 50, mY); AddKey(Keys.C, "C", mX += 50, mY); AddKey(Keys.V, "V", mX += 50, mY); AddKey(Keys.B, "B", mX += 50, mY); AddKey(Keys.N, "N", mX += 50, mY); AddKey(Keys.M, "M", mX += 50, mY); AddKey(Keys.Oemcomma, ",", mX += 50, mY); AddKey(Keys.OemPeriod, ".", mX += 50, mY); AddKey(Keys.OemQuestion, "/", mX += 50, mY); AddKey(Keys.RShiftKey, "SHIFT", mX += 50, mY, 125);

            mY += 50; mX = 0;
            AddKey(Keys.LControlKey, "CTRL", mX, mY, 60); AddKey(Keys.LWin, "WIN", mX += 65, mY, 60); AddKey(Keys.LMenu, "ALT", mX += 65, mY, 60); AddKey(Keys.Space, "SPACE", mX += 65, mY, 325); AddKey(Keys.RMenu, "ALT", mX += 330, mY, 60); AddKey(Keys.RWin, "WIN", mX += 65, mY, 60); AddKey(Keys.Apps, "MENU", mX += 65, mY, 60); AddKey(Keys.RControlKey, "CTRL", mX += 65, mY, 60);

            int nX = 780; mY = 0;
            AddKey(Keys.PrintScreen, "PRTSC", nX, mY); AddKey(Keys.Scroll, "SCRLK", nX += 50, mY); AddKey(Keys.Pause, "PAUSE", nX += 50, mY);
            mY += 60; nX = 780;
            AddKey(Keys.Insert, "INS", nX, mY); AddKey(Keys.Home, "HOME", nX += 50, mY); AddKey(Keys.PageUp, "PGUP", nX += 50, mY);
            mY += 50; nX = 780;
            AddKey(Keys.Delete, "DEL", nX, mY); AddKey(Keys.End, "END", nX += 50, mY); AddKey(Keys.PageDown, "PGDN", nX += 50, mY);
            mY += 100; nX = 780;
            AddKey(Keys.Up, "▲", nX + 50, mY);
            mY += 50;
            AddKey(Keys.Left, "◄", nX, mY); AddKey(Keys.Down, "▼", nX += 50, mY); AddKey(Keys.Right, "►", nX += 50, mY);

            int numX = 960; mY = 60;
            AddKey(Keys.NumLock, "NUM", numX, mY); AddKey(Keys.Divide, "/", numX += 50, mY); AddKey(Keys.Multiply, "*", numX += 50, mY); AddKey(Keys.Subtract, "-", numX += 50, mY);
            mY += 50; numX = 960;
            AddKey(Keys.NumPad7, "7", numX, mY); AddKey(Keys.NumPad8, "8", numX += 50, mY); AddKey(Keys.NumPad9, "9", numX += 50, mY); AddKey(Keys.Add, "+", numX += 50, mY, 45, 95);
            mY += 50; numX = 960;
            AddKey(Keys.NumPad4, "4", numX, mY); AddKey(Keys.NumPad5, "5", numX += 50, mY); AddKey(Keys.NumPad6, "6", numX += 50, mY);
            mY += 50; numX = 960;
            AddKey(Keys.NumPad1, "1", numX, mY); AddKey(Keys.NumPad2, "2", numX += 50, mY); AddKey(Keys.NumPad3, "3", numX += 50, mY); AddKey(Keys.Enter, "ENT", numX += 50, mY, 45, 95);
            mY += 50; numX = 960;
            AddKey(Keys.NumPad0, "0", numX, mY, 95); AddKey(Keys.Decimal, ".", numX += 100, mY);
        }

        private void AddKey(Keys key, string text, int x, int y, int w = 45, int h = 45)
        {
            keyDefs.Add(new KeyDef(key, text, new RectangleF(x, y, w, h)));
        }

        private void KeyboardPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None; // Crisp pixel avionics lines
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            if (keyboardPanel.ClientSize.Width <= 0 || keyboardPanel.ClientSize.Height <= 0) return;

            float scale = Math.Min(
                keyboardPanel.ClientSize.Width / KB_DESIGN_WIDTH,
                keyboardPanel.ClientSize.Height / KB_DESIGN_HEIGHT);
            scale = Math.Max(scale, 0.25f);

            float offsetX = (keyboardPanel.ClientSize.Width - KB_DESIGN_WIDTH * scale) / 2f;
            float offsetY = (keyboardPanel.ClientSize.Height - KB_DESIGN_HEIGHT * scale) / 2f;

            float fontSize = Math.Max(6.5f, 9.5f * scale);
            if (cachedKeyFont == null || lastScale != scale)
            {
                DisposeKeyboardGdiCache();
                cachedKeyFont = new Font("Consolas", fontSize, FontStyle.Bold);
                cachedSf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                
                // Normal unpressed/untested (Obsidian Glass)
                cachedNormalBrush = new SolidBrush(Color.FromArgb(18, 10, 30));
                cachedNormalTextBrush = new SolidBrush(HudTheme.Muted);
                cachedNormalPen = new Pen(HudTheme.Bezel, 1f);

                // Persistent Tested / Passed state (Emerald Pass)
                cachedTestedBrush = new SolidBrush(Color.FromArgb(16, 45, 30));
                cachedTestedTextBrush = new SolidBrush(HudTheme.PassNominal);
                cachedTestedPen = new Pen(HudTheme.PassNominal, 1f);

                // Live Active Pressed state (Vibrant Electric Violet with glow text)
                cachedPressedBrush = new SolidBrush(Color.FromArgb(95, 45, 160));
                cachedPressedTextBrush = new SolidBrush(Color.White);
                cachedPressedPen = new Pen(HudTheme.HudAccentSoft, 1.5f);

                lastScale = scale;
            }

            foreach (var kd in keyDefs)
            {
                RectangleF r = new RectangleF(
                    offsetX + kd.Rect.X * scale,
                    offsetY + kd.Rect.Y * scale,
                    kd.Rect.Width * scale - 2,
                    kd.Rect.Height * scale - 2);

                bool isCurrentlyPressed = currentlyPressedKeys.Contains(kd.Key);
                bool isTested = activeKeys.Contains(kd.Key);

                SolidBrush fillBrush;
                Pen borderPen;
                SolidBrush textBrush;

                if (isCurrentlyPressed)
                {
                    fillBrush = cachedPressedBrush;
                    borderPen = cachedPressedPen;
                    textBrush = cachedPressedTextBrush;
                }
                else if (isTested)
                {
                    fillBrush = cachedTestedBrush;
                    borderPen = cachedTestedPen;
                    textBrush = cachedTestedTextBrush;
                }
                else
                {
                    fillBrush = cachedNormalBrush;
                    borderPen = cachedNormalPen;
                    textBrush = cachedNormalTextBrush;
                }

                g.FillRectangle(fillBrush, r);
                g.DrawRectangle(borderPen, r.X, r.Y, r.Width, r.Height);
                g.DrawString(kd.Text, cachedKeyFont, textBrush, r, cachedSf);
            }
        }

        public void ResetKeyboardUI()
        {
            activeKeys.Clear();
            currentlyPressedKeys.Clear();
            keyboardPanel?.Invalidate();

            tpLeft = false;
            tpRight = false;
            tpMiddle = false;

            if (lblKeyboardCount != null)
            {
                lblKeyboardCount.Text = "KEYS LOGGED: 0/104";
            }

            if (lblMouseTest != null)
            {
                lblMouseTest.BackColor = HudTheme.PanelGlassTop;
                lblMouseTest.ForeColor = HudTheme.HudAccent;
                lblMouseTest.Text = "⟨ TRACKPAD // MOUSE TEST : [ L ] [ M ] [ R ] ⟩";
                lblMouseTest.Invalidate();
            }
        }

        public void HighlightKey(Keys key, bool isDown)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => HighlightKey(key, isDown)));
                return;
            }

            if (isDown)
            {
                activeKeys.Add(key);
                currentlyPressedKeys.Add(key);
            }
            else
            {
                currentlyPressedKeys.Remove(key);
            }

            if (lblKeyboardCount != null)
            {
                lblKeyboardCount.Text = $"KEYS LOGGED: {activeKeys.Count}/104";
            }

            keyboardPanel?.Invalidate();

            if (activeKeys.Count > 5) MarkTestComplete("Keyboard");
        }

        public void UnhookKeyboard()
        {
            if (_hookID != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProc = Process.GetCurrentProcess())
            using (ProcessModule curMod = curProc.MainModule)
            {
                _hookID = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, proc, NativeMethods.GetModuleHandle(curMod.ModuleName), 0);
                return _hookID;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    bool isDown = (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN);
                    bool isUp = (wParam == (IntPtr)0x0101 || wParam == (IntPtr)0x0105); // WM_KEYUP, WM_SYSKEYUP

                    if ((isDown || isUp) && lParam != IntPtr.Zero)
                    {
                        Keys key = (Keys)Marshal.ReadInt32(lParam);
                        if (Instance != null && !Instance.IsDisposed) Instance.HighlightKey(key, isDown);
                        
                        IntPtr foreground = NativeMethods.GetForegroundWindow();
                        if (foreground == Instance?.Handle && isDown)
                        {
                            if (key == Keys.LWin || key == Keys.RWin || (Control.ModifierKeys == Keys.Alt && key == Keys.Tab)) return (IntPtr)1;
                        }
                    }
                }
            }
            catch
            {
            }
            return NativeMethods.CallNextHookEx(_hookID, nCode, wParam, lParam);
        }
    }
}

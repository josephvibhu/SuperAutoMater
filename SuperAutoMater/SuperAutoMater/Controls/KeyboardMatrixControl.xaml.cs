using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SuperAutoMater.Wpf.Services;

namespace SuperAutoMater.Wpf.Controls
{
    public partial class KeyboardMatrixControl : UserControl
    {
        private class KeyVisual
        {
            public int VkCode;
            public string Label;
            public Border BorderElem;
            public TextBlock TextElem;
            public double Width;
            public bool IsTested;
            public bool IsPressed;
        }

        private readonly Dictionary<int, KeyVisual> _keyMap = new Dictionary<int, KeyVisual>();
        private readonly Dictionary<int, KeyVisual> _numpadKeyMap = new Dictionary<int, KeyVisual>();
        private readonly HashSet<int> _loggedKeys = new HashSet<int>();
        private int _totalTargetKeys = 0;
        private bool _isNumpadVisible = false;

        public event Action<int, int> ProgressChanged; // logged, total

        public KeyboardMatrixControl()
        {
            InitializeComponent();
            BuildMatrix();
            Loaded += (s, e) =>
            {
                KeyboardHookService.Instance.KeyDown += OnGlobalKeyDown;
                KeyboardHookService.Instance.KeyUp += OnGlobalKeyUp;
                KeyboardHookService.Instance.Start();
            };
            Unloaded += (s, e) =>
            {
                KeyboardHookService.Instance.KeyDown -= OnGlobalKeyDown;
                KeyboardHookService.Instance.KeyUp -= OnGlobalKeyUp;
            };
        }

        private void BuildMatrix()
        {
            StackKeyRows.Children.Clear();
            StackNumpadRows.Children.Clear();
            _keyMap.Clear();
            _numpadKeyMap.Clear();

            // Row 1: Function keys
            AddRow(StackKeyRows, _keyMap, new (int, string, double)[]
            {
                (0x1B, "ESC", 38),
                (0x70, "F1", 30), (0x71, "F2", 30), (0x72, "F3", 30), (0x73, "F4", 30),
                (0x74, "F5", 30), (0x75, "F6", 30), (0x76, "F7", 30), (0x77, "F8", 30),
                (0x78, "F9", 30), (0x79, "F10", 30), (0x7A, "F11", 30), (0x7B, "F12", 30),
                (0x2E, "DEL", 38)
            });

            // Row 2: Numbers
            AddRow(StackKeyRows, _keyMap, new (int, string, double)[]
            {
                (0xC0, "~", 28),
                (0x31, "1", 28), (0x32, "2", 28), (0x33, "3", 28), (0x34, "4", 28), (0x35, "5", 28),
                (0x36, "6", 28), (0x37, "7", 28), (0x38, "8", 28), (0x39, "9", 28), (0x30, "0", 28),
                (0xBD, "-", 28), (0xBB, "=", 28), (0x08, "BKSP", 56)
            });

            // Row 3: QWERTY (Fixed 'R' VK code to 0x52)
            AddRow(StackKeyRows, _keyMap, new (int, string, double)[]
            {
                (0x09, "TAB", 44),
                (0x51, "Q", 28), (0x57, "W", 28), (0x45, "E", 28), (0x52, "R", 28), (0x54, "T", 28),
                (0x59, "Y", 28), (0x55, "U", 28), (0x49, "I", 28), (0x4F, "O", 28), (0x50, "P", 28),
                (0xDB, "[", 28), (0xDD, "]", 28), (0xDC, "\\", 38)
            });

            // Row 4: ASDF
            AddRow(StackKeyRows, _keyMap, new (int, string, double)[]
            {
                (0x14, "CAPS", 50),
                (0x41, "A", 28), (0x53, "S", 28), (0x44, "D", 28), (0x46, "F", 28), (0x47, "G", 28),
                (0x48, "H", 28), (0x4A, "J", 28), (0x4B, "K", 28), (0x4C, "L", 28), (0xBA, ";", 28),
                (0xDE, "'", 28), (0x0D, "ENTER", 58)
            });

            // Row 5: ZXCV
            AddRow(StackKeyRows, _keyMap, new (int, string, double)[]
            {
                (0xA0, "SHIFT", 64),
                (0x5A, "Z", 28), (0x58, "X", 28), (0x43, "C", 28), (0x56, "V", 28), (0x42, "B", 28),
                (0x4E, "N", 28), (0x4D, "M", 28), (0xBC, ",", 28), (0xBE, ".", 28), (0xBF, "/", 28),
                (0xA1, "SHIFT", 64)
            });

            // Row 6: Bottom modifiers + Space
            AddRow(StackKeyRows, _keyMap, new (int, string, double)[]
            {
                (0xA2, "CTRL", 40), (0x5B, "WIN", 34), (0xA4, "ALT", 36),
                (0x20, "SPACE", 210),
                (0xA5, "ALT", 36), (0x5D, "MENU", 34), (0xA3, "CTRL", 40)
            });

            // ==============================================================
            // DYNAMIC NUMPAD & NAVIGATION BLOCK (Rows 1-6)
            // ==============================================================
            // Numpad Row 1: NumLock, Divide, Multiply, Subtract
            AddRow(StackNumpadRows, _numpadKeyMap, new (int, string, double)[]
            {
                (0x90, "NUM", 28), (0x6F, "/", 28), (0x6A, "*", 28), (0x6D, "-", 28)
            });

            // Numpad Row 2: 7, 8, 9, Add
            AddRow(StackNumpadRows, _numpadKeyMap, new (int, string, double)[]
            {
                (0x67, "7", 28), (0x68, "8", 28), (0x69, "9", 28), (0x6B, "+", 28)
            });

            // Numpad Row 3: 4, 5, 6, [Add continuation / Clear]
            AddRow(StackNumpadRows, _numpadKeyMap, new (int, string, double)[]
            {
                (0x64, "4", 28), (0x65, "5", 28), (0x66, "6", 28), (0x0C, "CLR", 28)
            });

            // Numpad Row 4: 1, 2, 3, Enter
            AddRow(StackNumpadRows, _numpadKeyMap, new (int, string, double)[]
            {
                (0x61, "1", 28), (0x62, "2", 28), (0x63, "3", 28), (0x0D, "ENT", 28)
            });

            // Numpad Row 5: 0, Decimal, Up Arrow
            AddRow(StackNumpadRows, _numpadKeyMap, new (int, string, double)[]
            {
                (0x60, "0", 58), (0x6E, ".", 28), (0x26, "▲", 28)
            });

            // Numpad Row 6: Left, Down, Right, PageDown
            AddRow(StackNumpadRows, _numpadKeyMap, new (int, string, double)[]
            {
                (0x25, "◀", 28), (0x28, "▼", 28), (0x27, "▶", 28), (0x22, "PDN", 28)
            });

            SetNumpadVisibility(false);
        }

        public void SetNumpadVisibility(bool visible)
        {
            _isNumpadVisible = visible;
            StackNumpadRows.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            NumpadDivider.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            BtnToggleNumpad.Content = visible ? "[-] HIDE NUMPAD" : "[+ EXPAND NUMPAD]";
            _totalTargetKeys = _keyMap.Count + (visible ? _numpadKeyMap.Count : 0);
            UpdateCountDisplay();
        }

        private void BtnToggleNumpad_Click(object sender, RoutedEventArgs e)
        {
            SetNumpadVisibility(!_isNumpadVisible);
        }

        private void AddRow(StackPanel parentPanel, Dictionary<int, KeyVisual> targetMap, (int vk, string text, double width)[] keys)
        {
            var rowStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1.5, 0, 1.5)
            };

            foreach (var (vk, text, width) in keys)
            {
                var txt = new TextBlock
                {
                    Text = text,
                    FontFamily = (FontFamily)FindResource("FontMono"),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var border = new Border
                {
                    Width = width,
                    Height = 26,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x2C, 0x36)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(1.5, 0, 1.5, 0),
                    Child = txt
                };

                var kv = new KeyVisual
                {
                    VkCode = vk,
                    Label = text,
                    BorderElem = border,
                    TextElem = txt,
                    Width = width,
                    IsTested = false,
                    IsPressed = false
                };

                rowStack.Children.Add(border);

                if (!targetMap.ContainsKey(vk))
                {
                    targetMap[vk] = kv;
                }
            }

            parentPanel.Children.Add(rowStack);
        }

        private void OnGlobalKeyDown(int vkCode)
        {
            if (!IsVisible) return;

            Dispatcher.InvokeAsync(() =>
            {
                if (TxtLastKey != null)
                {
                    TxtLastKey.Text = $"LAST KEY: {GetKeyLabel(vkCode)}";
                }

                // Remap general modifiers to Left versions
                if (vkCode == 0x10) vkCode = 0xA0; // VK_LSHIFT
                if (vkCode == 0x11) vkCode = 0xA2; // VK_LCONTROL
                if (vkCode == 0x12) vkCode = 0xA4; // VK_LMENU

                // Dynamically expand NumPad if ANY numpad or navigation key is pressed!
                bool isNumpadActuation = (vkCode >= 0x60 && vkCode <= 0x6F) || vkCode == 0x90 ||
                                         vkCode == 0x25 || vkCode == 0x26 || vkCode == 0x27 || vkCode == 0x28;
                if (isNumpadActuation && !_isNumpadVisible)
                {
                    SetNumpadVisibility(true);
                }

                // Check in main keymap or numpad keymap
                KeyVisual key = null;
                if (!_keyMap.TryGetValue(vkCode, out key) && !_numpadKeyMap.TryGetValue(vkCode, out key))
                {
                    // Fallback alias for laptops where top row sends media codes instead of F1-F12
                    int mediaAlias = vkCode switch
                    {
                        0xAD => 0x70, // Mute -> F1
                        0xAE => 0x71, // Vol Down -> F2
                        0xAF => 0x72, // Vol Up -> F3
                        0xB3 => 0x73, // Play/Pause -> F4
                        0xB1 => 0x75, // Prev Track -> F6
                        0xB0 => 0x76, // Next Track -> F7
                        _ => 0
                    };
                    if (mediaAlias != 0)
                    {
                        _keyMap.TryGetValue(mediaAlias, out key);
                    }
                }

                if (key != null)
                {
                    key.IsPressed = true;
                    _loggedKeys.Add(key.VkCode);
                    key.IsTested = true;
                    ApplyKeyVisual(key);
                    UpdateCountDisplay();
                }
            });
        }

        private void OnGlobalKeyUp(int vkCode)
        {
            if (!IsVisible) return;

            Dispatcher.InvokeAsync(() =>
            {
                if (vkCode == 0x10) vkCode = 0xA0;
                if (vkCode == 0x11) vkCode = 0xA2;
                if (vkCode == 0x12) vkCode = 0xA4;

                KeyVisual key = null;
                if (!_keyMap.TryGetValue(vkCode, out key) && !_numpadKeyMap.TryGetValue(vkCode, out key))
                {
                    int mediaAlias = vkCode switch
                    {
                        0xAD => 0x70,
                        0xAE => 0x71,
                        0xAF => 0x72,
                        0xB3 => 0x73,
                        0xB1 => 0x75,
                        0xB0 => 0x76,
                        _ => 0
                    };
                    if (mediaAlias != 0)
                    {
                        _keyMap.TryGetValue(mediaAlias, out key);
                    }
                }

                if (key != null)
                {
                    key.IsPressed = false;
                    ApplyKeyVisual(key);
                }
            });
        }

        private static string GetKeyLabel(int vkCode)
        {
            return vkCode switch
            {
                0x1B => "ESC",
                >= 0x70 and <= 0x7B => $"F{vkCode - 0x70 + 1}",
                0x20 => "SPACE",
                0x0D => "ENTER",
                0x08 => "BKSP",
                0x09 => "TAB",
                0x14 => "CAPS",
                0xA0 or 0x10 => "L-SHIFT",
                0xA1 => "R-SHIFT",
                0xA2 or 0x11 => "L-CTRL",
                0xA3 => "R-CTRL",
                0xA4 or 0x12 => "L-ALT",
                0xA5 => "R-ALT",
                0x5B or 0x5C => "WIN",
                0x2E => "DEL",
                0xAD => "MUTE (F1)",
                0xAE => "VOL- (F2)",
                0xAF => "VOL+ (F3)",
                0xB3 => "PLAY/PAUSE",
                0xB0 => "NEXT TRACK",
                0xB1 => "PREV TRACK",
                0x25 => "LEFT [◀]",
                0x26 => "UP [▲]",
                0x27 => "RIGHT [▶]",
                0x28 => "DOWN [▼]",
                >= 0x30 and <= 0x39 => ((char)vkCode).ToString(),
                >= 0x41 and <= 0x5A => ((char)vkCode).ToString(),
                >= 0x60 and <= 0x69 => $"NUM {vkCode - 0x60}",
                _ => $"0x{vkCode:X2}"
            };
        }

        private void ApplyKeyVisual(KeyVisual key)
        {
            if (key.IsPressed)
            {
                // Active pressed key: bright blue/white highlight
                key.BorderElem.Background = (Brush)FindResource("BrushAccentBlue");
                key.BorderElem.BorderBrush = new SolidColorBrush(Colors.White);
                key.TextElem.Foreground = new SolidColorBrush(Colors.White);
                key.BorderElem.Effect = (Effect)FindResource("EffectGlowEmerald");
            }
            else if (key.IsTested)
            {
                // Tested emerald state
                key.BorderElem.Background = new SolidColorBrush(Color.FromArgb(0x35, 0x2E, 0xA0, 0x43));
                key.BorderElem.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x3F, 0xB9, 0x50));
                key.TextElem.Foreground = (Brush)FindResource("BrushTextEmerald");
                key.BorderElem.Effect = null;
            }
            else
            {
                // Default dark slate
                key.BorderElem.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22));
                key.BorderElem.BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x2C, 0x36));
                key.TextElem.Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9));
                key.BorderElem.Effect = null;
            }
        }

        private void UpdateCountDisplay()
        {
            TxtLoggedCount.Text = $"{_loggedKeys.Count}/{_totalTargetKeys}";
            ProgressChanged?.Invoke(_loggedKeys.Count, _totalTargetKeys);
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            _loggedKeys.Clear();
            foreach (var kv in _keyMap.Values)
            {
                kv.IsTested = false;
                kv.IsPressed = false;
                ApplyKeyVisual(kv);
            }
            foreach (var kv in _numpadKeyMap.Values)
            {
                kv.IsTested = false;
                kv.IsPressed = false;
                ApplyKeyVisual(kv);
            }
            if (TxtLastKey != null)
            {
                TxtLastKey.Text = "LAST KEY: READY";
            }
            UpdateCountDisplay();
        }
    }
}

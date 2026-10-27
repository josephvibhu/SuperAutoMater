using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using SuperAutoMater.Wpf.Core;
using VeloxBench.Core;

namespace VeloxBench.Services;

public enum AppThemeMode
{
    Dark,
    Light,
    System
}

/// <summary>
/// Dynamic Theme Engine for Velox Suite (Dark, Light, and System OS Sync).
/// Provides instantaneous switching without restart or re-render flash.
/// </summary>
public sealed class ThemeService
{
    private static readonly Lazy<ThemeService> _lazy = new(() => new ThemeService());
    public static ThemeService Instance => _lazy.Value;

    private AppThemeMode _currentMode = AppThemeMode.Dark;
    private bool _isInitialized = false;

    public event Action<AppThemeMode, bool>? ThemeChanged; // mode, isDark

    public AppThemeMode CurrentMode => _currentMode;
    public bool IsDarkActive { get; private set; } = true;

    private ThemeService() { }

    public void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        try
        {
            // Listen for live OS theme changes (System mode)
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            string cfgTheme = BenchConfigService.Current?.Theme?.Trim()?.ToLowerInvariant() ?? "dark";
            var mode = cfgTheme switch
            {
                "light" => AppThemeMode.Light,
                "system" => AppThemeMode.System,
                _ => AppThemeMode.Dark
            };

            ApplyTheme(mode, persist: false);
        }
        catch (Exception ex)
        {
            AppLogger.Error("ThemeService.Initialize error", ex);
        }
    }

    public void SetTheme(AppThemeMode mode)
    {
        ApplyTheme(mode, persist: true);
    }

    private void ApplyTheme(AppThemeMode mode, bool persist)
    {
        _currentMode = mode;
        bool isDark = true;

        if (mode == AppThemeMode.Dark)
        {
            isDark = true;
        }
        else if (mode == AppThemeMode.Light)
        {
            isDark = false;
        }
        else // System
        {
            isDark = !IsWindowsLightTheme();
        }

        IsDarkActive = isDark;
        string tokenSource = isDark ? "Themes/TokensDark.xaml" : "Themes/TokensLight.xaml";

        try
        {
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var appResources = Application.Current.Resources;
                    var dictUri = new Uri($"pack://application:,,,/{tokenSource}", UriKind.Absolute);

                    // Find and replace existing token dictionary or add it
                    ResourceDictionary? existingTokenDict = null;
                    foreach (var dict in appResources.MergedDictionaries)
                    {
                        if (dict.Source != null && (dict.Source.ToString().Contains("TokensDark") || dict.Source.ToString().Contains("TokensLight")))
                        {
                            existingTokenDict = dict;
                            break;
                        }
                    }

                    var newDict = new ResourceDictionary { Source = dictUri };

                    if (existingTokenDict != null)
                    {
                        int index = appResources.MergedDictionaries.IndexOf(existingTokenDict);
                        appResources.MergedDictionaries.RemoveAt(index);
                        appResources.MergedDictionaries.Insert(index, newDict);
                    }
                    else
                    {
                        appResources.MergedDictionaries.Insert(0, newDict);
                    }

                    foreach (Window win in Application.Current.Windows)
                    {
                        win.InvalidateVisual();
                    }

                    AppLogger.Info($"[ThemeService] Switched theme to: {mode} (Active Palette: {(isDark ? "Dark" : "Light")})");
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[ThemeService] Failed to load token dictionary '{tokenSource}'", ex);
        }

        if (persist)
        {
            try
            {
                var cfg = BenchConfigService.Current;
                if (cfg != null)
                {
                    cfg.Theme = mode switch
                    {
                        AppThemeMode.Light => "light",
                        AppThemeMode.System => "system",
                        _ => "dark"
                    };
                    BenchConfigService.Save(cfg);
                }
            }
            catch { }
        }

        ThemeChanged?.Invoke(_currentMode, IsDarkActive);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && _currentMode == AppThemeMode.System)
        {
            ApplyTheme(AppThemeMode.System, persist: false);
        }
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                var val = key.GetValue("AppsUseLightTheme");
                if (val is int intVal && intVal == 1)
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }
}

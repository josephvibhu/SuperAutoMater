using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace VeloxManager.Services;

public enum AppThemeMode
{
    Dark,
    Light,
    System
}

/// <summary>
/// Dynamic Theme Engine for Velox Manager (Dark, Light, and System OS Sync).
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

            string cfgTheme = ManagerConfigService.Current?.Theme?.Trim()?.ToLowerInvariant() ?? "dark";
            var mode = cfgTheme switch
            {
                "light" => AppThemeMode.Light,
                "system" => AppThemeMode.System,
                _ => AppThemeMode.Dark
            };

            ApplyTheme(mode, persist: false);
        }
        catch (Exception)
        {
            // Fallback safe
            ApplyTheme(AppThemeMode.Dark, persist: false);
        }
    }

    public void SetTheme(AppThemeMode mode)
    {
        ApplyTheme(mode, persist: true);
    }

    public void ToggleTheme()
    {
        if (IsDarkActive)
            SetTheme(AppThemeMode.Light);
        else
            SetTheme(AppThemeMode.Dark);
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
                        if (dict.Source != null && (dict.Source.OriginalString.Contains("TokensDark.xaml", StringComparison.OrdinalIgnoreCase) ||
                                                    dict.Source.OriginalString.Contains("TokensLight.xaml", StringComparison.OrdinalIgnoreCase)))
                        {
                            existingTokenDict = dict;
                            break;
                        }
                    }

                    var newDict = new ResourceDictionary { Source = dictUri };

                    if (existingTokenDict != null)
                    {
                        int index = appResources.MergedDictionaries.IndexOf(existingTokenDict);
                        appResources.MergedDictionaries[index] = newDict;
                    }
                    else
                    {
                        appResources.MergedDictionaries.Insert(0, newDict);
                    }
                });
            }
        }
        catch (Exception)
        {
            // Logging or fallback
        }

        if (persist)
        {
            try
            {
                ManagerConfigService.Current.Theme = mode.ToString().ToLowerInvariant();
                ManagerConfigService.Save();
            }
            catch { }
        }

        ThemeChanged?.Invoke(_currentMode, IsDarkActive);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_currentMode == AppThemeMode.System && e.Category == UserPreferenceCategory.General)
        {
            ApplyTheme(AppThemeMode.System, persist: false);
        }
    }

    public static bool IsWindowsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                var val = key.GetValue("AppsUseLightTheme");
                if (val is int intVal)
                {
                    return intVal > 0;
                }
            }
        }
        catch { }
        return false;
    }
}

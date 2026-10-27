using System;
using System.Windows;
using Microsoft.Win32;

namespace VeloxHub.Services;

public enum AppThemeMode
{
    Dark,
    Light,
    System
}

public sealed class ThemeService
{
    private static readonly Lazy<ThemeService> _lazy = new(() => new ThemeService());
    public static ThemeService Instance => _lazy.Value;

    private AppThemeMode _currentMode = AppThemeMode.Light;
    private bool _isInitialized = false;

    public event Action<AppThemeMode, bool> ThemeChanged;

    public AppThemeMode CurrentMode => _currentMode;
    public bool IsDarkActive { get; private set; } = false;

    private ThemeService() { }

    public void Initialize(string defaultTheme = "Light")
    {
        if (_isInitialized) return;
        _isInitialized = true;

        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            var mode = defaultTheme?.Trim()?.ToLowerInvariant() switch
            {
                "dark" => AppThemeMode.Dark,
                "system" => AppThemeMode.System,
                _ => AppThemeMode.Light
            };

            ApplyTheme(mode, persist: false);
        }
        catch { }
    }

    public void SetTheme(AppThemeMode mode)
    {
        ApplyTheme(mode, persist: true);
    }

    private void ApplyTheme(AppThemeMode mode, bool persist)
    {
        _currentMode = mode;
        bool isDark = false;

        if (mode == AppThemeMode.Dark)
        {
            isDark = true;
        }
        else if (mode == AppThemeMode.Light)
        {
            isDark = false;
        }
        else
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
                    var dictUri = new Uri($"pack://application:,,,/VeloxHub;component/{tokenSource}", UriKind.Absolute);

                    ResourceDictionary existingTokenDict = null;
                    foreach (var dict in appResources.MergedDictionaries)
                    {
                        if (dict.Source != null && (dict.Source.ToString().Contains("TokensDark.xaml") || dict.Source.ToString().Contains("TokensLight.xaml")))
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
        catch { }

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
                object value = key.GetValue("AppsUseLightTheme");
                if (value is int intVal)
                {
                    return intVal != 0;
                }
            }
        }
        catch { }
        return false;
    }
}

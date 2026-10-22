using System;
using System.Windows;
using System.Windows.Media;
using Velox.Shared.Common;
using VeloxBench.Core;
using VeloxBench.Services;

namespace VeloxBench.Views;

public partial class LicenseActivationDialog : Window
{
    public LicenseActivationDialog()
    {
        InitializeComponent();
        RefreshUi();
    }

    private void RefreshUi()
    {
        var lic = LicenseService.Instance;
        var cfg = BenchConfigService.Current;

        TxtCompany.Text = string.IsNullOrWhiteSpace(lic.CompanyName) ? "Standard Refurb Depot" : lic.CompanyName;
        TxtBenchName.Text = string.IsNullOrWhiteSpace(cfg.BenchName) ? "BENCH-01" : cfg.BenchName;
        TxtValidity.Text = lic.IsLicensed ? (lic.IsOfflineLeaseValid ? "Offline Grace Active" : "Online Verified") : "Not Activated";
        TxtStatusSummary.Text = lic.StatusMessage;
        TxtServerUrl.Text = cfg.ApiUrl;
        TxtKeyInput.Text = lic.LicenseKey;

        if (lic.IsLicensed)
        {
            DotStatus.Fill = new SolidColorBrush(Color.FromRgb(56, 189, 248)); // #38BDF8
            TxtStatusBadge.Text = lic.IsOfflineLeaseValid ? "OFFLINE VERIFIED" : "ONLINE VERIFIED";
            TxtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
        }
        else
        {
            DotStatus.Fill = new SolidColorBrush(Color.FromRgb(248, 113, 113)); // #F87171
            TxtStatusBadge.Text = "NOT ACTIVATED";
            TxtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
        }
    }

    private void BtnPaste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                TxtKeyInput.Text = Clipboard.GetText().Trim();
            }
        }
        catch { }
    }

    private async void BtnActivate_Click(object sender, RoutedEventArgs e)
    {
        var key = TxtKeyInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowFeedback(VeloxResult.Fail(VeloxErrorCode.ERR_LIC_NOT_FOUND, "Please enter a valid license key."));
            return;
        }

        ShowFeedback(VeloxResult.Ok("Validating license with Velox Cloud..."));

        var res = await LicenseService.Instance.ActivateKeyAsync(key, persistToDisk: ChkPersistUsb.IsChecked == true);
        ShowFeedback(res);
        RefreshUi();
    }

    private async void BtnTestPing_Click(object sender, RoutedEventArgs e)
    {
        ShowFeedback(VeloxResult.Ok("Pinging Velox Cloud API..."));
        var res = await CloudSyncService.Instance.TestCloudConnectionAsync();
        ShowFeedback(res);
        RefreshUi();
    }

    private void ShowFeedback(VeloxResult res)
    {
        PnlFeedback.Visibility = Visibility.Visible;
        if (res.Success)
        {
            PnlFeedback.Background = new SolidColorBrush(Color.FromArgb(50, 16, 185, 129));
            PnlFeedback.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            TxtFeedback.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            TxtFeedback.Text = $"[{res.CodeString}] {res.Message}";
        }
        else
        {
            PnlFeedback.Background = new SolidColorBrush(Color.FromArgb(50, 239, 68, 68));
            PnlFeedback.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtFeedback.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            TxtFeedback.Text = $"[{res.CodeString}] {res.Message}" +
                               (string.IsNullOrWhiteSpace(res.Details) ? "" : $"\nDetails: {res.Details}");
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal)
            {
                DialogResult = true;
            }
        }
        catch { }
        finally
        {
            try { Close(); } catch { }
        }
    }
}

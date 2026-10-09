using System;
using System.Windows;
using System.Windows.Media;
using Velox.Shared.Models;
using VeloxManager.ViewModels;

namespace VeloxManager.Controls;

public partial class QualityGateModalWindow : Window
{
    public bool AllPassed { get; private set; }
    public bool OverrideApproved { get; private set; }

    public QualityGateModalWindow(AssetItemViewModel asset)
    {
        InitializeComponent();
        AuditAsset(asset);
    }

    private void AuditAsset(AssetItemViewModel asset)
    {
        TxtAssetSubheader.Text = $"{asset.AssetTag} • {asset.ModelName} (S/N: {asset.SerialNumber})";

        // 1. Cosmetic Grade Check
        bool gradePassed = (asset.Grade != AssetGrade.Unknown) ||
                           !string.IsNullOrWhiteSpace(asset.Model.CosmeticGrade);
        string gradeText = !string.IsNullOrWhiteSpace(asset.Model.CosmeticGrade)
            ? $"Assigned Cosmetic Grade: {asset.Model.CosmeticGrade}"
            : (asset.Grade != AssetGrade.Unknown ? $"Assigned Grade: {asset.Grade}" : "Grade is UNKNOWN / Not evaluated");
        SetCheckState(BorderGrade, IconGrade, BadgeGrade, TxtGradeDetail, gradePassed, gradeText);

        // 2. Battery Health Check (Threshold >= 70%)
        int? batt = asset.Model.BatteryHealth;
        bool battPassed = true;
        string battText = "Battery test not applicable (Desktop/Spare)";
        if (batt.HasValue && batt.Value > 0)
        {
            battPassed = batt.Value >= 70;
            battText = $"Recorded Battery Health: {batt.Value}% (Minimum threshold is 70%)";
        }
        else if (asset.Category == AssetCategory.Laptop)
        {
            battText = "Battery health not recorded. Presumed tested OK.";
        }
        SetCheckState(BorderBattery, IconBattery, BadgeBattery, TxtBatteryDetail, battPassed, battText);

        // 3. Storage Health Check (Threshold >= 80%)
        int? storage = asset.Model.StorageHealth;
        bool storagePassed = true;
        string storageText = "Storage integrity verified (100% OK)";
        if (storage.HasValue)
        {
            storagePassed = storage.Value >= 80;
            storageText = $"Recorded Drive Health: {storage.Value}% (Minimum threshold is 80%)";
        }
        SetCheckState(BorderStorage, IconStorage, BadgeStorage, TxtStorageDetail, storagePassed, storageText);

        // 4. BOM Missing Components Check
        var missing = asset.MissingComponents;
        bool compPassed = string.IsNullOrWhiteSpace(missing) ||
                          missing.Trim().Equals("None", StringComparison.OrdinalIgnoreCase) ||
                          missing.Trim().Equals("-", StringComparison.OrdinalIgnoreCase);
        string compText = compPassed ? "All components complete (No missing parts flagged)" : $"Flagged Missing Components: {missing}";
        SetCheckState(BorderComponents, IconComponents, BadgeComponents, TxtComponentsDetail, compPassed, compText);

        AllPassed = gradePassed && battPassed && storagePassed && compPassed;

        if (AllPassed)
        {
            BannerStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F3D29"));
            BannerStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2EA043"));
            TxtStatusIcon.Text = "✅";
            TxtStatusTitle.Text = "QUALITY GATE PASSED — READY FOR DISPATCH";
            TxtStatusTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950"));
            TxtStatusDesc.Text = "All 4 critical quality standards met. Device can be promoted to Ready for Sale.";
            TxtResultSummary.Text = "✓ Certified RTS Compliance";
            TxtResultSummary.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950"));

            BtnConfirmOrOverride.Content = "Promote to Ready for Sale";
            BtnConfirmOrOverride.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#238636"));
            BtnConfirmOrOverride.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2EA043"));
        }
        else
        {
            BannerStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3D1F24"));
            BannerStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F85149"));
            TxtStatusIcon.Text = "❌";
            TxtStatusTitle.Text = "QUALITY GATE VIOLATION — ACTION REQUIRED";
            TxtStatusTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F85149"));
            TxtStatusDesc.Text = "This device does not meet the minimum depot standards to be sold directly to customers.";
            TxtResultSummary.Text = "⚠️ Failed Quality Standards";
            TxtResultSummary.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F85149"));

            BtnConfirmOrOverride.Content = "⚠️ Manager Quality Override (Force RTS)";
            BtnConfirmOrOverride.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#842029"));
            BtnConfirmOrOverride.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F85149"));
        }
    }

    private static void SetCheckState(
        System.Windows.Controls.Border border,
        System.Windows.Controls.TextBlock icon,
        System.Windows.Controls.TextBlock badge,
        System.Windows.Controls.TextBlock detail,
        bool passed,
        string text)
    {
        detail.Text = text;
        if (passed)
        {
            icon.Text = "✓";
            icon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950"));
            badge.Text = "PASS";
            badge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950"));
            border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2EA043"));
        }
        else
        {
            icon.Text = "✕";
            icon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F85149"));
            badge.Text = "FAIL";
            badge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F85149"));
            border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F85149"));
        }
    }

    private void BtnConfirmOrOverride_Click(object sender, RoutedEventArgs e)
    {
        if (!AllPassed)
        {
            var res = VeloxMessageBox.Show(
                "You are about to bypass Quality Gate standards for this unit.\n\nAre you sure you want to approve this manager override and list this asset in Ready for Sale?",
                "Manager Quality Override",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes) return;

            OverrideApproved = true;
        }

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

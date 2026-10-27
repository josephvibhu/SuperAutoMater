using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using VeloxManager.Services;

namespace VeloxManager.Controls;

public partial class CloudOperationsModalWindow : Window
{
    private readonly Func<Task>? _onDatabaseMutatedAsync;

    public CloudOperationsModalWindow(Func<Task>? onDatabaseMutatedAsync = null)
    {
        InitializeComponent();
        _onDatabaseMutatedAsync = onDatabaseMutatedAsync;
        TxtApiUrl.Text = ManagerConfigService.Current.ApiUrl;
        TxtCloudStatus.Text = ManagerApiClient.Instance.IsConnected ? "● AWS CLOUD LIVE" : "○ OFFLINE / DISCONNECTED";
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void BtnBackup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            TxtStatus.Text = "Connecting to AWS Cloud to create full database backup...";
            TxtStatus.Foreground = System.Windows.Media.Brushes.LightSkyBlue;

            var sfd = new SaveFileDialog
            {
                Title = "Save Velox Cloud Database Backup",
                Filter = "JSON Backup File (*.json)|*.json",
                FileName = $"Velox_Cloud_Backup_{DateTime.Now:yyyyMMdd_HHmm}.json"
            };

            if (sfd.ShowDialog() != true)
            {
                TxtStatus.Text = "Backup cancelled by operator.";
                TxtStatus.Foreground = System.Windows.Media.Brushes.Gray;
                return;
            }

            var json = await ManagerApiClient.Instance.DownloadBackupJsonAsync();
            if (string.IsNullOrWhiteSpace(json))
            {
                TxtStatus.Text = "Failed to download backup from AWS Cloud. Check internet / API key.";
                TxtStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
                VeloxMessageBox.Show("Failed to download cloud database backup from AWS Cloud.", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error, this);
                return;
            }

            await File.WriteAllTextAsync(sfd.FileName, json);
            TxtStatus.Text = $"Backup successfully exported to: {Path.GetFileName(sfd.FileName)}";
            TxtStatus.Foreground = System.Windows.Media.Brushes.LightGreen;

            VeloxMessageBox.Show($"Cloud database backup successfully created and saved to:\n\n{sfd.FileName}",
                "Backup Exported", MessageBoxButton.OK, MessageBoxImage.Information, this);
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Backup error: {ex.Message}";
            TxtStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
            VeloxMessageBox.Show($"Error creating backup: {ex.Message}", "Backup Failed", MessageBoxButton.OK, MessageBoxImage.Error, this);
        }
    }

    private async void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select Velox Cloud Backup File to Restore",
                Filter = "JSON Backup File (*.json)|*.json"
            };

            if (ofd.ShowDialog() != true) return;

            var confirm = VeloxMessageBox.Show(
                $"Are you sure you want to restore AWS Cloud from this backup file?\n\n{ofd.FileName}\n\nThis will synchronize cloud assets with the contents of the file.",
                "Confirm Cloud Restore", MessageBoxButton.YesNo, MessageBoxImage.Question, this);

            if (confirm != MessageBoxResult.Yes) return;

            TxtStatus.Text = "Uploading backup and restoring database on AWS Cloud...";
            TxtStatus.Foreground = System.Windows.Media.Brushes.LightSkyBlue;

            var json = await File.ReadAllTextAsync(ofd.FileName);
            var (success, msg) = await ManagerApiClient.Instance.RestoreBackupJsonAsync(json);

            if (success)
            {
                TxtStatus.Text = msg;
                TxtStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                VeloxMessageBox.Show(msg, "Cloud Restore Complete", MessageBoxButton.OK, MessageBoxImage.Information, this);
                if (_onDatabaseMutatedAsync != null)
                {
                    await _onDatabaseMutatedAsync();
                }
            }
            else
            {
                TxtStatus.Text = msg;
                TxtStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
                VeloxMessageBox.Show(msg, "Restore Error", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Restore error: {ex.Message}";
            TxtStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
            VeloxMessageBox.Show($"Error restoring backup: {ex.Message}", "Restore Failed", MessageBoxButton.OK, MessageBoxImage.Error, this);
        }
    }

    private async void BtnPurge_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var confirm = VeloxMessageBox.Show(
                "⚠️ DANGER: Are you sure you want to permanently delete all previous test devices and history logs from AWS Cloud?\n\nThis will wipe existing test assets so you can intake real warehouse inventory starting clean at 0.\n\nTechnician user logins, station configs, and API keys are preserved.\n\nDo you want to proceed?",
                "Confirm Database Purge / Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning, this);

            if (confirm != MessageBoxResult.Yes) return;

            TxtStatus.Text = "Purging previous test devices from AWS Cloud...";
            TxtStatus.Foreground = System.Windows.Media.Brushes.Orange;

            var (success, msg, purged) = await ManagerApiClient.Instance.PurgeAssetsAsync();
            if (success)
            {
                TxtStatus.Text = $"Database wiped clean: {purged} previous asset entries purged.";
                TxtStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                VeloxMessageBox.Show($"Cloud inventory successfully reset to 0!\n\n{purged} previous asset entries and associated test records have been deleted from AWS Cloud.\n\nYou can now intake real inventory.",
                    "Cloud Inventory Reset", MessageBoxButton.OK, MessageBoxImage.Information, this);

                if (_onDatabaseMutatedAsync != null)
                {
                    await _onDatabaseMutatedAsync();
                }
            }
            else
            {
                TxtStatus.Text = msg;
                TxtStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
                VeloxMessageBox.Show(msg, "Purge Failed", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Purge error: {ex.Message}";
            TxtStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
            VeloxMessageBox.Show($"Error purging database: {ex.Message}", "Purge Failed", MessageBoxButton.OK, MessageBoxImage.Error, this);
        }
    }
}

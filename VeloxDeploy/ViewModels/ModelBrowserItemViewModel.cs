using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VeloxDeploy.ViewModels
{
    public class ModelBrowserItemViewModel : INotifyPropertyChanged
    {
        private bool _isCached;
        private int _infCount;
        private string _statusBadgeText = string.Empty;
        private bool _isDownloading;
        private double _downloadPercent;
        private string _downloadStatus = string.Empty;

        public string ModelName { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string Series { get; set; } = string.Empty;
        public string OsVersion { get; set; } = "Win10/Win11 x64";
        public string FileName { get; set; } = string.Empty;
        public string SizeFormatted { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public bool IsHostUnit { get; set; }

        public bool IsCached
        {
            get => _isCached;
            set
            {
                if (_isCached != value)
                {
                    _isCached = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanDownload));
                }
            }
        }

        public int InfCount
        {
            get => _infCount;
            set
            {
                if (_infCount != value)
                {
                    _infCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusBadgeText
        {
            get => _statusBadgeText;
            set
            {
                if (_statusBadgeText != value)
                {
                    _statusBadgeText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set
            {
                if (_isDownloading != value)
                {
                    _isDownloading = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanDownload));
                }
            }
        }

        public double DownloadPercent
        {
            get => _downloadPercent;
            set
            {
                if (Math.Abs(_downloadPercent - value) > 0.01)
                {
                    _downloadPercent = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DownloadStatus
        {
            get => _downloadStatus;
            set
            {
                if (_downloadStatus != value)
                {
                    _downloadStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CanDownload => !IsCached && !IsDownloading && !string.IsNullOrWhiteSpace(DownloadUrl);

        public string BrandColorHex => Manufacturer?.ToUpperInvariant() switch
        {
            "DELL" => "#3B82F6",
            "HP" => "#0284C7",
            "LENOVO" => "#EF4444",
            _ => "#9279FF"
        };

        public string BrandBgHex => Manufacturer?.ToUpperInvariant() switch
        {
            "DELL" => "#1E293B",
            "HP" => "#082F49",
            "LENOVO" => "#450A0A",
            _ => "#241E3D"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

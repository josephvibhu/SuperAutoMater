using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using VeloxDeploy.Core;
using VeloxDeploy.Models;

namespace VeloxDeploy.ViewModels
{
    public class ModelBrowserViewModel : INotifyPropertyChanged
    {
        private readonly Dispatcher _dispatcher;
        private readonly List<ModelBrowserItemViewModel> _masterCatalog = new();
        private readonly HardwareProfile? _hostProfile;

        private string _searchText = string.Empty;
        private string _selectedBrand = "All";
        private ModelBrowserItemViewModel? _selectedItem;
        private string _summaryText = string.Empty;

        private bool _isBusy;
        private string _busyStatus = string.Empty;
        private double _busyProgress;

        public event Action<bool>? RequestClose;

        public ModelBrowserViewModel(string? currentlySelectedModel = null, HardwareProfile? hostProfile = null)
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _hostProfile = hostProfile;

            SelectCommand = new RelayCommand(ExecuteSelect, () => SelectedItem != null);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
            OpenFolderCommand = new RelayCommand(ExecuteOpenFolder);
            CaptureHostCommand = new RelayCommand(async () => await ExecuteCaptureHostAsync());
            DownloadItemCommand = new RelayCommand(async param =>
            {
                if (param is ModelBrowserItemViewModel item)
                {
                    await ExecuteDownloadPackAsync(item);
                }
            });

            BuildCatalog(currentlySelectedModel);
        }

        #region Properties

        public ObservableCollection<ModelBrowserItemViewModel> FilteredItems { get; } = new();

        public ModelBrowserItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (_selectedItem != value)
                {
                    _selectedItem = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSearchText));
                    ApplyFilter();
                }
            }
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(_searchText);

        public string SelectedBrand
        {
            get => _selectedBrand;
            set
            {
                if (_selectedBrand != value)
                {
                    _selectedBrand = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsBrandAll));
                    OnPropertyChanged(nameof(IsBrandDell));
                    OnPropertyChanged(nameof(IsBrandLenovo));
                    OnPropertyChanged(nameof(IsBrandHP));
                    OnPropertyChanged(nameof(IsBrandVault));
                    ApplyFilter();
                }
            }
        }

        public bool IsBrandAll
        {
            get => SelectedBrand == "All";
            set { if (value) SelectedBrand = "All"; }
        }

        public bool IsBrandDell
        {
            get => SelectedBrand == "Dell";
            set { if (value) SelectedBrand = "Dell"; }
        }

        public bool IsBrandLenovo
        {
            get => SelectedBrand == "Lenovo";
            set { if (value) SelectedBrand = "Lenovo"; }
        }

        public bool IsBrandHP
        {
            get => SelectedBrand == "HP";
            set { if (value) SelectedBrand = "HP"; }
        }

        public bool IsBrandVault
        {
            get => SelectedBrand == "Vault";
            set { if (value) SelectedBrand = "Vault"; }
        }

        public int CountAll => _masterCatalog.Count;
        public int CountDell => _masterCatalog.Count(x => string.Equals(x.Manufacturer, "Dell", StringComparison.OrdinalIgnoreCase));
        public int CountLenovo => _masterCatalog.Count(x => string.Equals(x.Manufacturer, "Lenovo", StringComparison.OrdinalIgnoreCase));
        public int CountHP => _masterCatalog.Count(x => string.Equals(x.Manufacturer, "HP", StringComparison.OrdinalIgnoreCase));
        public int CountVault => _masterCatalog.Count(x => x.IsCached);

        public string SummaryText
        {
            get => _summaryText;
            set
            {
                if (_summaryText != value)
                {
                    _summaryText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy != value)
                {
                    _isBusy = value;
                    OnPropertyChanged();
                }
            }
        }

        public string BusyStatus
        {
            get => _busyStatus;
            set
            {
                if (_busyStatus != value)
                {
                    _busyStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public double BusyProgress
        {
            get => _busyProgress;
            set
            {
                if (Math.Abs(_busyProgress - value) > 0.01)
                {
                    _busyProgress = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Commands

        public ICommand SelectCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CaptureHostCommand { get; }
        public ICommand DownloadItemCommand { get; }

        #endregion

        #region Catalog Assembly & Filtering

        private void BuildCatalog(string? currentlySelected)
        {
            _masterCatalog.Clear();

            // 1. Host Machine Entry (if present)
            if (_hostProfile != null && !string.IsNullOrWhiteSpace(_hostProfile.Model))
            {
                string hostName = $"{_hostProfile.Manufacturer} {_hostProfile.Model}".Trim();
                var (isCached, count, _) = OemDriverService.CheckModelCache(hostName);

                var hostItem = new ModelBrowserItemViewModel
                {
                    ModelName = hostName,
                    Manufacturer = _hostProfile.Manufacturer,
                    Series = "Depot Host Machine (Auto-Detected)",
                    OsVersion = "Windows 11 / 10 64-bit",
                    FileName = "Online Driver Store Export",
                    SizeFormatted = "Local Hardware",
                    IsHostUnit = true,
                    IsCached = isCached,
                    InfCount = count,
                    StatusBadgeText = isCached
                        ? $"✓ Ready in Vault ({count} drivers)"
                        : "📥 Captured Drivers Pending"
                };
                _masterCatalog.Add(hostItem);
            }

            // 2. Official OEM Models from Catalog
            foreach (var def in OemDriverService.Catalog)
            {
                var (isCached, count, _) = OemDriverService.CheckModelCache(def.ModelName);

                var item = new ModelBrowserItemViewModel
                {
                    ModelName = def.ModelName,
                    Manufacturer = def.Manufacturer,
                    Series = def.Series,
                    OsVersion = def.OsVersion,
                    FileName = def.FileName,
                    SizeFormatted = def.SizeFormatted,
                    DownloadUrl = def.DownloadUrl,
                    IsCached = isCached,
                    InfCount = count,
                    StatusBadgeText = isCached
                        ? $"✓ Ready in Vault ({count} drivers)"
                        : $"☁ 1-Click OEM Download ({def.SizeFormatted})"
                };
                _masterCatalog.Add(item);
            }

            // 3. Local DriverVault folders that might not be in the OEM Catalog
            var cachedFolders = OemDriverService.GetLocallyCachedModels();
            foreach (var cached in cachedFolders)
            {
                bool alreadyInCatalog = _masterCatalog.Any(x =>
                    x.ModelName.Equals(cached.ModelName, StringComparison.OrdinalIgnoreCase) ||
                    x.ModelName.EndsWith(cached.ModelName, StringComparison.OrdinalIgnoreCase) ||
                    cached.ModelName.EndsWith(x.ModelName, StringComparison.OrdinalIgnoreCase));

                if (!alreadyInCatalog)
                {
                    var item = new ModelBrowserItemViewModel
                    {
                        ModelName = cached.ModelName,
                        Manufacturer = cached.Manufacturer,
                        Series = "Custom Captured Driver Pack",
                        OsVersion = "Windows 10/11 64-bit",
                        FileName = "Local Driver Repository",
                        SizeFormatted = "Local Vault",
                        IsCached = true,
                        InfCount = cached.DriverCount,
                        StatusBadgeText = $"✓ Ready in Vault ({cached.DriverCount} drivers)"
                    };
                    _masterCatalog.Add(item);
                }
            }

            RefreshCounts();
            ApplyFilter();

            // Reselect the active model
            if (!string.IsNullOrWhiteSpace(currentlySelected))
            {
                SelectedItem = FilteredItems.FirstOrDefault(x => x.ModelName.Equals(currentlySelected, StringComparison.OrdinalIgnoreCase))
                               ?? _masterCatalog.FirstOrDefault(x => x.ModelName.Equals(currentlySelected, StringComparison.OrdinalIgnoreCase));
            }
            if (SelectedItem == null && FilteredItems.Count > 0)
            {
                SelectedItem = FilteredItems[0];
            }
        }

        private void RefreshCounts()
        {
            OnPropertyChanged(nameof(CountAll));
            OnPropertyChanged(nameof(CountDell));
            OnPropertyChanged(nameof(CountLenovo));
            OnPropertyChanged(nameof(CountHP));
            OnPropertyChanged(nameof(CountVault));
        }

        private void ApplyFilter()
        {
            FilteredItems.Clear();

            IEnumerable<ModelBrowserItemViewModel> query = _masterCatalog;

            // Brand Filter
            if (SelectedBrand == "Dell")
            {
                query = query.Where(x => string.Equals(x.Manufacturer, "Dell", StringComparison.OrdinalIgnoreCase));
            }
            else if (SelectedBrand == "Lenovo")
            {
                query = query.Where(x => string.Equals(x.Manufacturer, "Lenovo", StringComparison.OrdinalIgnoreCase));
            }
            else if (SelectedBrand == "HP")
            {
                query = query.Where(x => string.Equals(x.Manufacturer, "HP", StringComparison.OrdinalIgnoreCase));
            }
            else if (SelectedBrand == "Vault")
            {
                query = query.Where(x => x.IsCached);
            }

            // Text Search Filter
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string s = SearchText.Trim();
                query = query.Where(x =>
                    x.ModelName.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    x.Manufacturer.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    x.Series.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    x.FileName.Contains(s, StringComparison.OrdinalIgnoreCase));
            }

            // Sort: Vault cached first, then alphabetical
            var sorted = query.OrderByDescending(x => x.IsCached).ThenBy(x => x.ModelName).ToList();

            foreach (var item in sorted)
            {
                FilteredItems.Add(item);
            }

            int totalCached = sorted.Count(x => x.IsCached);
            SummaryText = $"Showing {sorted.Count} of {CountAll} models ({totalCached} ready in local DriverVault)";

            if (SelectedItem == null || !FilteredItems.Contains(SelectedItem))
            {
                SelectedItem = FilteredItems.FirstOrDefault();
            }
        }

        #endregion

        #region Actions

        private void ExecuteSelect()
        {
            if (SelectedItem != null)
            {
                RequestClose?.Invoke(true);
            }
        }

        private async Task ExecuteDownloadPackAsync(ModelBrowserItemViewModel item)
        {
            if (item.IsDownloading || item.IsCached || string.IsNullOrWhiteSpace(item.DownloadUrl)) return;

            item.IsDownloading = true;
            item.DownloadPercent = 5.0;
            item.DownloadStatus = "Connecting...";

            IsBusy = true;
            BusyStatus = $"Downloading OEM driver pack for {item.ModelName}...";
            BusyProgress = 5.0;

            using var cts = new CancellationTokenSource();
            bool success = await OemDriverService.DownloadAndExtractAsync(
                item.ModelName,
                (pct, status) =>
                {
                    _dispatcher.InvokeAsync(() =>
                    {
                        item.DownloadPercent = pct;
                        item.DownloadStatus = status;
                        BusyProgress = pct;
                        BusyStatus = $"{item.ModelName}: {status}";
                    });
                },
                msg => { },
                cts.Token);

            item.IsDownloading = false;
            IsBusy = false;

            if (success)
            {
                var (isCached, count, _) = OemDriverService.CheckModelCache(item.ModelName);
                item.IsCached = isCached;
                item.InfCount = count;
                item.StatusBadgeText = $"✓ Ready in Vault ({count} drivers)";
                RefreshCounts();
                ApplyFilter();
            }
            else
            {
                item.DownloadStatus = "Download failed";
            }
        }

        private async Task ExecuteCaptureHostAsync()
        {
            if (_hostProfile == null || string.IsNullOrWhiteSpace(_hostProfile.Model)) return;

            IsBusy = true;
            BusyStatus = $"Capturing drivers from host machine ({_hostProfile.Manufacturer} {_hostProfile.Model})...";
            BusyProgress = 10.0;

            using var cts = new CancellationTokenSource();
            bool success = await OemDriverService.CaptureLocalMachineDriversAsync(
                _hostProfile.Manufacturer,
                _hostProfile.Model,
                (pct, status) =>
                {
                    _dispatcher.InvokeAsync(() =>
                    {
                        BusyProgress = pct;
                        BusyStatus = status;
                    });
                },
                msg => { },
                cts.Token);

            IsBusy = false;

            if (success)
            {
                BuildCatalog(SelectedItem?.ModelName);
            }
        }

        private void ExecuteOpenFolder()
        {
            try
            {
                string vaultRoot = DriverVaultManager.ResolveDriverVaultRoot("DriverVault");
                Directory.CreateDirectory(vaultRoot);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{vaultRoot}\"") { UseShellExecute = true });
            }
            catch { }
        }

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

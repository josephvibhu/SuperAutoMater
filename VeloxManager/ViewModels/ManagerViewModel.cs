using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Velox.Shared.Dtos;
using Velox.Shared.Models;
using VeloxManager.Services;

namespace VeloxManager.ViewModels;

public class ManagerViewModel : INotifyPropertyChanged
{
    private static ManagerViewModel? _instance;
    public static ManagerViewModel Instance => _instance ??= new ManagerViewModel();

    private readonly ManagerApiClient _api = ManagerApiClient.Instance;
    private readonly LanMirrorServer _lan = LanMirrorServer.Instance;

    // ── Navigation ─────────────────────────────────────────────────────────────
    private int _selectedTabIndex = 0;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); }
    }

    // ── Collections ────────────────────────────────────────────────────────────
    public ObservableCollection<AssetItemViewModel> Assets { get; } = new();
    public ObservableCollection<AssetItemViewModel> FilteredAssets { get; } = new();
    public ObservableCollection<EmployeeDto> Employees { get; } = new();
    public ObservableCollection<string> LiveActivityFeed { get; } = new();
    public ObservableCollection<Invoice> Invoices { get; } = new();

    // ── Active Selection ───────────────────────────────────────────────────────
    private AssetItemViewModel? _selectedAsset;
    public AssetItemViewModel? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            _selectedAsset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedAsset));
        }
    }
    public bool HasSelectedAsset => SelectedAsset != null;

    private EmployeeDto? _selectedEmployee;
    public EmployeeDto? SelectedEmployee
    {
        get => _selectedEmployee;
        set { _selectedEmployee = value; OnPropertyChanged(); }
    }

    // ── Search & Filters ───────────────────────────────────────────────────────
    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }
    }

    private string _statusFilter = "ALL";
    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (_statusFilter != value)
            {
                _statusFilter = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }
    }

    // ── Metrics & Badges ───────────────────────────────────────────────────────
    public int TotalAssetsCount => Assets.Count;
    public int WipCount => Assets.Count(a => a.Status != LifecycleQueue.ReadyForSale && a.Status != LifecycleQueue.Sold);
    public int ReadyForSaleCount => Assets.Count(a => a.Status == LifecycleQueue.ReadyForSale);
    public int SoldCount => Assets.Count(a => a.Status == LifecycleQueue.Sold);

    public string CloudStatusText => _api.IsConnected ? "☁ CLOUD LIVE (Railway)" : "⚠ CONNECTING TO CLOUD...";
    public string CloudStatusColor => _api.IsConnected ? "#3FB950" : "#D29922";

    public string LanStatusText => _lan.IsRunning ? $"🌐 LAN MIRROR (Port {ManagerConfigService.Current.LanPort})" : "LAN SERVER INACTIVE";
    public string LanStatusColor => _lan.IsRunning ? "#58A6FF" : "#8B949E";

    // ── Invoicing Form Fields ───────────────────────────────────────────────────
    private string _newInvoiceParty = "Acme Corp Refurbishments";
    public string NewInvoiceParty
    {
        get => _newInvoiceParty;
        set { _newInvoiceParty = value; OnPropertyChanged(); }
    }

    private decimal _newInvoiceTax = 18m;
    public decimal NewInvoiceTax
    {
        get => _newInvoiceTax;
        set { _newInvoiceTax = value; OnPropertyChanged(); }
    }

    // ── New Employee Form Fields ────────────────────────────────────────────────
    private string _newEmployeeName = "";
    public string NewEmployeeName
    {
        get => _newEmployeeName;
        set { _newEmployeeName = value; OnPropertyChanged(); }
    }

    private string _newEmployeeRole = "Technician";
    public string NewEmployeeRole
    {
        get => _newEmployeeRole;
        set { _newEmployeeRole = value; OnPropertyChanged(); }
    }

    // ── Commands ───────────────────────────────────────────────────────────────
    public ICommand RefreshAssetsCommand { get; }
    public ICommand UpdateStatusCommand { get; }
    public ICommand AssignToEmployeeCommand { get; }
    public ICommand CreateEmployeeCommand { get; }
    public ICommand GenerateInvoiceCommand { get; }
    public ICommand PreviewInvoiceCommand { get; }
    public ICommand ToggleLanServerCommand { get; }

    public ManagerViewModel()
    {
        _instance = this;

        RefreshAssetsCommand = new RelayCommand(async _ => await LoadAssetsAndEmployeesAsync());
        UpdateStatusCommand = new RelayCommand(async p => await UpdateSelectedAssetStatusAsync(p as string));
        AssignToEmployeeCommand = new RelayCommand(async _ => await AssignSelectedAssetAsync());
        CreateEmployeeCommand = new RelayCommand(async _ => await CreateNewEmployeeAsync());
        GenerateInvoiceCommand = new RelayCommand(_ => GenerateInvoiceForSelectedAssets());
        PreviewInvoiceCommand = new RelayCommand(p => PreviewInvoice(p as Invoice));
        ToggleLanServerCommand = new RelayCommand(_ => ToggleLanServer());

        // Wire up Real-Time Events
        _api.CloudConnectionChanged += connected =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(CloudStatusText));
                OnPropertyChanged(nameof(CloudStatusColor));
            });
        };

        _api.AssetUpdated += sseData =>
        {
            Application.Current?.Dispatcher.Invoke(async () =>
            {
                LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Cloud Event: {sseData}");
                if (LiveActivityFeed.Count > 100) LiveActivityFeed.RemoveAt(LiveActivityFeed.Count - 1);
                await LoadAssetsAndEmployeesAsync();
            });
        };

        _lan.LocalBenchActivityReceived += msg =>
        {
            Application.Current?.Dispatcher.Invoke(async () =>
            {
                LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] LAN Activity: {msg}");
                if (LiveActivityFeed.Count > 100) LiveActivityFeed.RemoveAt(LiveActivityFeed.Count - 1);
                await LoadAssetsAndEmployeesAsync();
            });
        };

        // Start LAN mirror server if enabled
        if (ManagerConfigService.Current.EnableLanServer)
        {
            _lan.Start(ManagerConfigService.Current.LanPort);
        }

        // Start SSE stream
        _api.StartRealtimeEvents();

        // Initial Data Load
        _ = LoadAssetsAndEmployeesAsync();
    }

    public async Task LoadAssetsAndEmployeesAsync()
    {
        try
        {
            var rawAssets = await _api.SearchAssetsAsync(limit: 500);
            var rawEmployees = await _api.GetEmployeesAsync();

            Application.Current?.Dispatcher.Invoke(() =>
            {
                Assets.Clear();
                foreach (var a in rawAssets)
                {
                    Assets.Add(new AssetItemViewModel(a));
                }

                Employees.Clear();
                foreach (var emp in rawEmployees)
                {
                    Employees.Add(emp);
                }

                ApplyFilter();
                RefreshMetrics();
            });
        }
        catch { }
    }

    private void ApplyFilter()
    {
        FilteredAssets.Clear();
        var q = SearchText.Trim().ToLowerInvariant();

        foreach (var item in Assets)
        {
            // Status check
            if (StatusFilter != "ALL")
            {
                if (StatusFilter == "READY_FOR_SALE" && item.Status != LifecycleQueue.ReadyForSale) continue;
                if (StatusFilter == "WIP" && (item.Status == LifecycleQueue.ReadyForSale || item.Status == LifecycleQueue.Sold)) continue;
                if (StatusFilter == "TESTING" && item.Status != LifecycleQueue.ActiveTesting) continue;
                if (StatusFilter == "REPAIR" && item.Status != LifecycleQueue.ExternalRepair) continue;
                if (StatusFilter == "SOLD" && item.Status != LifecycleQueue.Sold) continue;
            }

            // Search text check
            if (!string.IsNullOrWhiteSpace(q))
            {
                var matches = item.SerialNumber.ToLowerInvariant().Contains(q) ||
                              item.AssetTag.ToLowerInvariant().Contains(q) ||
                              item.ModelName.ToLowerInvariant().Contains(q) ||
                              item.AssignedEmployeeName.ToLowerInvariant().Contains(q) ||
                              item.Supplier.ToLowerInvariant().Contains(q) ||
                              item.Customer.ToLowerInvariant().Contains(q);

                if (!matches) continue;
            }

            FilteredAssets.Add(item);
        }
    }

    private void RefreshMetrics()
    {
        OnPropertyChanged(nameof(TotalAssetsCount));
        OnPropertyChanged(nameof(WipCount));
        OnPropertyChanged(nameof(ReadyForSaleCount));
        OnPropertyChanged(nameof(SoldCount));
        OnPropertyChanged(nameof(CloudStatusText));
        OnPropertyChanged(nameof(CloudStatusColor));
    }

    public async Task UpdateSelectedAssetStatusAsync(string? newStatusStr)
    {
        if (SelectedAsset == null || string.IsNullOrWhiteSpace(newStatusStr)) return;

        if (Enum.TryParse<LifecycleQueue>(newStatusStr, true, out var newStatus))
        {
            var success = await _api.UpdateAssetStatusAsync(SelectedAsset.Id, newStatus, $"Manual status update via VeloxManager");
            if (success)
            {
                SelectedAsset.Status = newStatus;
                RefreshMetrics();
                LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Status of {SelectedAsset.SerialNumber} updated to {newStatus}");
            }
        }
    }

    public async Task AssignSelectedAssetAsync()
    {
        if (SelectedAsset == null || SelectedEmployee == null) return;

        var success = await _api.AssignAssetAsync(SelectedAsset.Id, SelectedEmployee.Id);
        if (success)
        {
            SelectedAsset.AssignedEmployeeName = SelectedEmployee.Name;
            LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Assigned {SelectedAsset.SerialNumber} to {SelectedEmployee.Name}");
        }
    }

    public async Task CreateNewEmployeeAsync()
    {
        if (string.IsNullOrWhiteSpace(NewEmployeeName)) return;

        var role = Enum.TryParse<EmployeeRole>(NewEmployeeRole, true, out var r) ? r : EmployeeRole.Technician;
        var emp = await _api.CreateEmployeeAsync(NewEmployeeName.Trim(), role);
        if (emp != null)
        {
            Employees.Add(emp);
            NewEmployeeName = "";
            LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] New employee registered: {emp.Name} ({emp.Role})");
        }
    }

    public void GenerateInvoiceForSelectedAssets()
    {
        var inv = new Invoice
        {
            PartyName = string.IsNullOrWhiteSpace(NewInvoiceParty) ? "Customer" : NewInvoiceParty,
            TaxPercent = NewInvoiceTax
        };

        if (SelectedAsset != null)
        {
            inv.Items.Add(new InvoiceItem
            {
                LineNumber = 1,
                AssetId = SelectedAsset.Id,
                SerialNumber = SelectedAsset.SerialNumber,
                Model = SelectedAsset.ModelName,
                Grade = SelectedAsset.GradeBadge,
                Description = $"Refurbished Laptop / PC (Storage Health: {SelectedAsset.StorageHealth}%)",
                UnitPrice = 450.00m,
                Quantity = 1
            });
        }
        else
        {
            // Add top 5 ready for sale assets if none explicitly selected
            var readyUnits = Assets.Where(a => a.Status == LifecycleQueue.ReadyForSale).Take(5).ToList();
            int line = 1;
            foreach (var u in readyUnits)
            {
                inv.Items.Add(new InvoiceItem
                {
                    LineNumber = line++,
                    AssetId = u.Id,
                    SerialNumber = u.SerialNumber,
                    Model = u.ModelName,
                    Grade = u.GradeBadge,
                    Description = "Certified Refurbished Unit",
                    UnitPrice = 450.00m,
                    Quantity = 1
                });
            }
        }

        Invoices.Insert(0, inv);
        PreviewInvoice(inv);
    }

    public void PreviewInvoice(Invoice? inv)
    {
        if (inv == null) return;

        try
        {
            var html = InvoiceService.GenerateHtml(inv);
            var tempFile = Path.Combine(Path.GetTempPath(), $"{inv.InvoiceNumber}.html");
            File.WriteAllText(tempFile, html);
            Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open invoice preview: {ex.Message}", "Velox Invoice Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ToggleLanServer()
    {
        if (_lan.IsRunning)
        {
            _lan.Stop();
        }
        else
        {
            _lan.Start(ManagerConfigService.Current.LanPort);
        }
        OnPropertyChanged(nameof(LanStatusText));
        OnPropertyChanged(nameof(LanStatusColor));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

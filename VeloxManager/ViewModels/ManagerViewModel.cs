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
            AutoFillInvoiceDetailsFromSelectedAsset();
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
    private InvoiceType _selectedInvoiceType = InvoiceType.SalesInvoice;
    public InvoiceType SelectedInvoiceType
    {
        get => _selectedInvoiceType;
        set
        {
            if (_selectedInvoiceType != value)
            {
                _selectedInvoiceType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedInvoiceTypeString));
                OnPropertyChanged(nameof(IsSalesInvoice));
                OnPropertyChanged(nameof(InvoicePartyLabel));
                OnPropertyChanged(nameof(InvoicePriceLabel));
                OnPropertyChanged(nameof(GenerateInvoiceButtonText));
                AutoFillInvoiceDetailsFromSelectedAsset();
            }
        }
    }

    public string SelectedInvoiceTypeString
    {
        get => SelectedInvoiceType == InvoiceType.SalesInvoice ? "Sales Tax Invoice" : "Purchase Inward Bill";
        set
        {
            SelectedInvoiceType = value != null && value.Contains("Purchase") ? InvoiceType.PurchaseBill : InvoiceType.SalesInvoice;
        }
    }

    public bool IsSalesInvoice => SelectedInvoiceType == InvoiceType.SalesInvoice;
    public string InvoicePartyLabel => IsSalesInvoice ? "CUSTOMER / CLIENT NAME" : "SUPPLIER / VENDOR NAME";
    public string InvoicePriceLabel => IsSalesInvoice ? "UNIT SALE PRICE ($)" : "UNIT PURCHASE COST ($)";
    public string GenerateInvoiceButtonText => IsSalesInvoice ? "🧾 Generate & Open Sales Bill" : "📥 Generate & Open Purchase Bill";

    private string _newInvoiceParty = "Acme Corp Refurbishments";
    public string NewInvoiceParty
    {
        get => _newInvoiceParty;
        set { _newInvoiceParty = value; OnPropertyChanged(); }
    }

    private decimal _newInvoiceUnitPrice = 450.00m;
    public decimal NewInvoiceUnitPrice
    {
        get => _newInvoiceUnitPrice;
        set { _newInvoiceUnitPrice = value; OnPropertyChanged(); }
    }

    private decimal _newInvoiceTax = 18m;
    public decimal NewInvoiceTax
    {
        get => _newInvoiceTax;
        set { _newInvoiceTax = value; OnPropertyChanged(); }
    }

    private bool _markAsSoldOnInvoice = true;
    public bool MarkAsSoldOnInvoice
    {
        get => _markAsSoldOnInvoice;
        set { _markAsSoldOnInvoice = value; OnPropertyChanged(); }
    }

    private string _newInvoiceNotes = "Certified Refurbished Hardware. Net 30 days.";
    public string NewInvoiceNotes
    {
        get => _newInvoiceNotes;
        set { _newInvoiceNotes = value; OnPropertyChanged(); }
    }

    private void AutoFillInvoiceDetailsFromSelectedAsset()
    {
        if (SelectedAsset == null) return;

        if (IsSalesInvoice)
        {
            if (!string.IsNullOrWhiteSpace(SelectedAsset.Customer))
                NewInvoiceParty = SelectedAsset.Customer;
            if (SelectedAsset.SalePrice.HasValue && SelectedAsset.SalePrice.Value > 0)
                NewInvoiceUnitPrice = (decimal)SelectedAsset.SalePrice.Value;
            NewInvoiceNotes = "Certified Refurbished Hardware. Net 30 days.";
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(SelectedAsset.Supplier))
                NewInvoiceParty = SelectedAsset.Supplier;
            if (SelectedAsset.PurchasePrice.HasValue && SelectedAsset.PurchasePrice.Value > 0)
                NewInvoiceUnitPrice = (decimal)SelectedAsset.PurchasePrice.Value;
            NewInvoiceNotes = "Inward depot inventory acquisition. Received in good order.";
        }
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

    private string _newEmployeePin = "1234";
    public string NewEmployeePin
    {
        get => _newEmployeePin;
        set { _newEmployeePin = value; OnPropertyChanged(); }
    }

    private string _newEmployeeStationId = "BENCH-01";
    public string NewEmployeeStationId
    {
        get => _newEmployeeStationId;
        set { _newEmployeeStationId = value; OnPropertyChanged(); }
    }

    // ── Selected Employee Management ────────────────────────────────────────────
    private EmployeeDto? _selectedEmployeeForEdit;
    public EmployeeDto? SelectedEmployeeForEdit
    {
        get => _selectedEmployeeForEdit;
        set
        {
            _selectedEmployeeForEdit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedEmployeeForEdit));
            if (value != null)
            {
                EditEmployeeStationId = value.StationId;
            }
        }
    }
    public bool HasSelectedEmployeeForEdit => SelectedEmployeeForEdit != null;

    private string _editEmployeePin = "";
    public string EditEmployeePin
    {
        get => _editEmployeePin;
        set { _editEmployeePin = value; OnPropertyChanged(); }
    }

    private string _editEmployeeStationId = "BENCH-01";
    public string EditEmployeeStationId
    {
        get => _editEmployeeStationId;
        set { _editEmployeeStationId = value; OnPropertyChanged(); }
    }

    // ── Commands ───────────────────────────────────────────────────────────────
    public ICommand RefreshAssetsCommand { get; }
    public ICommand UpdateStatusCommand { get; }
    public ICommand AssignToEmployeeCommand { get; }
    public ICommand CreateEmployeeCommand { get; }
    public ICommand ResetEmployeePinCommand { get; }
    public ICommand UpdateEmployeeStationCommand { get; }
    public ICommand ToggleEmployeeActiveCommand { get; }
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
        ResetEmployeePinCommand = new RelayCommand(async _ => await ResetSelectedEmployeePinAsync());
        UpdateEmployeeStationCommand = new RelayCommand(async _ => await UpdateSelectedEmployeeStationAsync());
        ToggleEmployeeActiveCommand = new RelayCommand(async _ => await ToggleSelectedEmployeeActiveAsync());
        GenerateInvoiceCommand = new RelayCommand(async _ => await GenerateInvoiceForSelectedAssetsAsync());
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
        var pin = !string.IsNullOrWhiteSpace(NewEmployeePin) ? NewEmployeePin.Trim() : "1234";
        var station = !string.IsNullOrWhiteSpace(NewEmployeeStationId) ? NewEmployeeStationId.Trim() : "BENCH-01";

        var emp = await _api.CreateEmployeeAsync(NewEmployeeName.Trim(), role, pin, station);
        if (emp != null)
        {
            Employees.Add(emp);
            NewEmployeeName = "";
            NewEmployeePin = "1234";
            LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] New technician created: {emp.Name} (PIN: {pin}, Station: {emp.StationId})");
        }
    }

    public async Task ResetSelectedEmployeePinAsync(string? pinOverride = null)
    {
        if (SelectedEmployeeForEdit == null) return;
        var newPin = !string.IsNullOrWhiteSpace(pinOverride) ? pinOverride.Trim() : (!string.IsNullOrWhiteSpace(EditEmployeePin) ? EditEmployeePin.Trim() : "1234");
        if (newPin.Length < 4)
        {
            MessageBox.Show("PIN must be at least 4 digits.", "Invalid PIN", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ok = await _api.ResetEmployeePinAsync(SelectedEmployeeForEdit.Id, newPin);
        if (ok)
        {
            LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Station PIN updated for {SelectedEmployeeForEdit.Name} (PIN: {newPin})");
            EditEmployeePin = "";
            MessageBox.Show($"Station PIN successfully updated for {SelectedEmployeeForEdit.Name} to: {newPin}", "PIN Updated", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public async Task UpdateSelectedEmployeeStationAsync(string? stationOverride = null)
    {
        if (SelectedEmployeeForEdit == null) return;
        var newStation = !string.IsNullOrWhiteSpace(stationOverride) ? stationOverride.Trim() : (!string.IsNullOrWhiteSpace(EditEmployeeStationId) ? EditEmployeeStationId.Trim() : "BENCH-01");
        var ok = await _api.UpdateEmployeeStationAsync(SelectedEmployeeForEdit.Id, newStation);
        if (ok)
        {
            SelectedEmployeeForEdit.StationId = newStation;
            LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Station updated for {SelectedEmployeeForEdit.Name} to {newStation}");
            await LoadAssetsAndEmployeesAsync();
        }
    }

    public async Task ToggleSelectedEmployeeActiveAsync()
    {
        if (SelectedEmployeeForEdit == null) return;
        var newStatus = !SelectedEmployeeForEdit.IsActive;
        var updated = await _api.PatchEmployeeAsync(SelectedEmployeeForEdit.Id, new PatchEmployeeRequest { IsActive = newStatus });
        if (updated != null)
        {
            LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {SelectedEmployeeForEdit.Name} status toggled: {(newStatus ? "ACTIVE" : "INACTIVE")}");
            await LoadAssetsAndEmployeesAsync();
        }
    }

    public async Task GenerateInvoiceForSelectedAssetsAsync()
    {
        var inv = new Invoice
        {
            Type = SelectedInvoiceType,
            PartyName = string.IsNullOrWhiteSpace(NewInvoiceParty)
                ? (IsSalesInvoice ? "Customer / Retail Client" : "Inward Hardware Supplier")
                : NewInvoiceParty.Trim(),
            TaxPercent = NewInvoiceTax,
            Notes = string.IsNullOrWhiteSpace(NewInvoiceNotes)
                ? (IsSalesInvoice ? "Thank you for your business. Certified Refurbished Hardware. Net 30." : "Depot inward inventory acquisition.")
                : NewInvoiceNotes.Trim()
        };

        var unitPrice = NewInvoiceUnitPrice > 0 ? NewInvoiceUnitPrice : 450.00m;
        var affectedAssets = new List<AssetItemViewModel>();

        if (SelectedAsset != null)
        {
            inv.Items.Add(new InvoiceItem
            {
                LineNumber = 1,
                AssetId = SelectedAsset.Id,
                SerialNumber = SelectedAsset.SerialNumber,
                Model = SelectedAsset.ModelName,
                Grade = SelectedAsset.GradeBadge,
                Description = $"Certified Hardware (Disk Health: {SelectedAsset.StorageHealth}%)",
                UnitPrice = unitPrice,
                Quantity = 1
            });
            affectedAssets.Add(SelectedAsset);
        }
        else
        {
            // If no single asset selected, bundle candidate units
            var candidates = IsSalesInvoice
                ? Assets.Where(a => a.Status == LifecycleQueue.ReadyForSale).Take(5).ToList()
                : Assets.Take(5).ToList();

            int line = 1;
            foreach (var u in candidates)
            {
                var price = IsSalesInvoice
                    ? (u.SalePrice.HasValue && u.SalePrice.Value > 0 ? (decimal)u.SalePrice.Value : unitPrice)
                    : (u.PurchasePrice.HasValue && u.PurchasePrice.Value > 0 ? (decimal)u.PurchasePrice.Value : unitPrice);

                inv.Items.Add(new InvoiceItem
                {
                    LineNumber = line++,
                    AssetId = u.Id,
                    SerialNumber = u.SerialNumber,
                    Model = u.ModelName,
                    Grade = u.GradeBadge,
                    Description = "Certified Refurbished Hardware Unit",
                    UnitPrice = price,
                    Quantity = 1
                });
                affectedAssets.Add(u);
            }
        }

        if (inv.Items.Count == 0)
        {
            MessageBox.Show("No eligible assets available to generate invoice.", "Velox Billing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Invoices.Insert(0, inv);
        PreviewInvoice(inv);
        LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Generated {inv.Type} #{inv.InvoiceNumber} for {inv.PartyName} (Total: ${inv.TotalAmount:N2})");

        // Automatically update asset lifecycle to SOLD if sales invoice with flag checked
        if (IsSalesInvoice && MarkAsSoldOnInvoice)
        {
            foreach (var asset in affectedAssets)
            {
                if (asset.Status != LifecycleQueue.Sold)
                {
                    var success = await _api.UpdateAssetStatusAsync(asset.Id, LifecycleQueue.Sold, $"Sold via Invoice #{inv.InvoiceNumber}");
                    if (success)
                    {
                        asset.Status = LifecycleQueue.Sold;
                        LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Asset {asset.SerialNumber} updated to SOLD via Invoice #{inv.InvoiceNumber}");
                    }
                }
            }
            RefreshMetrics();
        }
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

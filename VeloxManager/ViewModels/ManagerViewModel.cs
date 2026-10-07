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
using VeloxManager.Controls;
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

    // Vyapar Stock Collections for Invoicing
    public ObservableCollection<AssetItemViewModel> AvailableBaseUnits { get; } = new();
    public ObservableCollection<AssetItemViewModel> ComponentStock { get; } = new();
    public ObservableCollection<InvoiceItem> CurrentInvoiceItems { get; } = new();
    public ObservableCollection<FittedComponentItemViewModel> SelectedAssetFittedComponents { get; } = new();
    public bool HasFittedComponents => SelectedAssetFittedComponents.Count > 0;

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
            _ = LoadFittedComponentsForSelectedAssetAsync();
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

    private string _categoryFilter = "ALL";
    public string CategoryFilter
    {
        get => _categoryFilter;
        set
        {
            if (_categoryFilter != value)
            {
                _categoryFilter = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }
    }

    // ── Quick Intake Form Fields ────────────────────────────────────────────────
    public ObservableCollection<string> QuickIntakeCategories { get; } = new()
    {
        "Laptop", "Desktop", "RAM", "Storage", "Charger", "Peripheral", "RepairSpare"
    };

    private string _quickIntakeCategory = "Laptop";
    public string QuickIntakeCategory
    {
        get => _quickIntakeCategory;
        set { _quickIntakeCategory = value; OnPropertyChanged(); }
    }

    private string _quickIntakeModel = "";
    public string QuickIntakeModel
    {
        get => _quickIntakeModel;
        set { _quickIntakeModel = value; OnPropertyChanged(); }
    }

    private string _quickIntakeSerial = "";
    public string QuickIntakeSerial
    {
        get => _quickIntakeSerial;
        set { _quickIntakeSerial = value; OnPropertyChanged(); }
    }

    private int _quickIntakeQuantity = 1;
    public int QuickIntakeQuantity
    {
        get => _quickIntakeQuantity;
        set { _quickIntakeQuantity = value; OnPropertyChanged(); }
    }

    private double _quickIntakePurchasePrice = 0;
    public double QuickIntakePurchasePrice
    {
        get => _quickIntakePurchasePrice;
        set { _quickIntakePurchasePrice = value; OnPropertyChanged(); }
    }

    private double _quickIntakeSalePrice = 0;
    public double QuickIntakeSalePrice
    {
        get => _quickIntakeSalePrice;
        set { _quickIntakeSalePrice = value; OnPropertyChanged(); }
    }

    private string _quickIntakeNotes = "";
    public string QuickIntakeNotes
    {
        get => _quickIntakeNotes;
        set { _quickIntakeNotes = value; OnPropertyChanged(); }
    }

    // ── Fitted Components (BOM) Form Fields ─────────────────────────────────────
    private bool _attachFromStock = false;
    public bool AttachFromStock
    {
        get => _attachFromStock;
        set { _attachFromStock = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> BomCategoryOptions { get; } = new()
    {
        "RAM", "Storage", "Battery", "Display", "Charger", "Peripheral", "RepairSpare", "Other"
    };

    private string _newBomCategory = "RAM";
    public string NewBomCategory
    {
        get => _newBomCategory;
        set { _newBomCategory = value; OnPropertyChanged(); }
    }

    private string _newBomDescription = "";
    public string NewBomDescription
    {
        get => _newBomDescription;
        set { _newBomDescription = value; OnPropertyChanged(); }
    }

    private string _newBomSerial = "";
    public string NewBomSerial
    {
        get => _newBomSerial;
        set { _newBomSerial = value; OnPropertyChanged(); }
    }

    private string _newBomCostDelta = "0";
    public string NewBomCostDelta
    {
        get => _newBomCostDelta;
        set { _newBomCostDelta = value; OnPropertyChanged(); }
    }

    private string _newBomPriceDelta = "0";
    public string NewBomPriceDelta
    {
        get => _newBomPriceDelta;
        set { _newBomPriceDelta = value; OnPropertyChanged(); }
    }

    private AssetItemViewModel? _selectedBomStockItem;
    public AssetItemViewModel? SelectedBomStockItem
    {
        get => _selectedBomStockItem;
        set
        {
            _selectedBomStockItem = value;
            OnPropertyChanged();
            if (value != null)
            {
                NewBomDescription = value.ModelName;
                NewBomSerial = value.SerialNumber;
                NewBomCategory = value.Category.ToString();
                NewBomPriceDelta = value.SalePrice?.ToString("N0") ?? "0";
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

    // ── Invoicing Form Fields & Vyapar Suite ─────────────────────────────────────
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
                OnPropertyChanged(nameof(GenerateInvoiceButtonText));
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
    public string GenerateInvoiceButtonText => IsSalesInvoice ? "🧾 Finalize & Print Tax Invoice" : "📥 Finalize & Print Inward Bill";

    // Customer / Party Details
    private string _draftPartyName = "Walk-in Retail Client";
    public string DraftPartyName
    {
        get => _draftPartyName;
        set { _draftPartyName = value; OnPropertyChanged(); }
    }

    private string _draftPartyPhone = "";
    public string DraftPartyPhone
    {
        get => _draftPartyPhone;
        set { _draftPartyPhone = value; OnPropertyChanged(); }
    }

    private string _draftPartyAddress = "Retail Depot Counter";
    public string DraftPartyAddress
    {
        get => _draftPartyAddress;
        set { _draftPartyAddress = value; OnPropertyChanged(); }
    }

    private string _draftPaymentMode = "UPI / Cash / Card";
    public string DraftPaymentMode
    {
        get => _draftPaymentMode;
        set { _draftPaymentMode = value; OnPropertyChanged(); }
    }

    private decimal _draftTaxPercent = 18m;
    public decimal DraftTaxPercent
    {
        get => _draftTaxPercent;
        set
        {
            _draftTaxPercent = value;
            OnPropertyChanged();
            RecalculateDraftTotals();
        }
    }

    private string _draftNotes = "Certified Refurbished Hardware. 30 Days Depot Warranty Included.";
    public string DraftNotes
    {
        get => _draftNotes;
        set { _draftNotes = value; OnPropertyChanged(); }
    }

    private bool _markAsSoldOnInvoice = true;
    public bool MarkAsSoldOnInvoice
    {
        get => _markAsSoldOnInvoice;
        set { _markAsSoldOnInvoice = value; OnPropertyChanged(); }
    }

    // Totals
    public decimal DraftSubTotal => CurrentInvoiceItems.Sum(i => i.LineTotal);
    public decimal DraftTaxAmount => Math.Round(DraftSubTotal * (DraftTaxPercent / 100m), 2);
    public decimal DraftGrandTotal => DraftSubTotal + DraftTaxAmount;

    public void RecalculateDraftTotals()
    {
        OnPropertyChanged(nameof(DraftSubTotal));
        OnPropertyChanged(nameof(DraftTaxAmount));
        OnPropertyChanged(nameof(DraftGrandTotal));
    }

    // Base Machine Picker for Invoice
    private AssetItemViewModel? _selectedInvoiceBaseAsset;
    public AssetItemViewModel? SelectedInvoiceBaseAsset
    {
        get => _selectedInvoiceBaseAsset;
        set { _selectedInvoiceBaseAsset = value; OnPropertyChanged(); }
    }

    // Component Picker for Invoicing
    private AssetItemViewModel? _selectedComponentForInvoice;
    public AssetItemViewModel? SelectedComponentForInvoice
    {
        get => _selectedComponentForInvoice;
        set
        {
            _selectedComponentForInvoice = value;
            OnPropertyChanged();
            if (value != null)
            {
                ComponentItemPrice = IsSalesInvoice
                    ? (decimal)(value.SalePrice ?? 0)
                    : (decimal)(value.PurchasePrice ?? 0);
                ComponentItemNote = $"{value.CategoryBadge}: {value.ModelName}";
            }
        }
    }

    public ObservableCollection<string> ComponentActionOptions { get; } = new()
    {
        "Upgrade (+)",
        "Downgrade / Deduction (-)",
        "Bundled / Included (₹0)",
        "Replacement Spare Part"
    };

    private string _selectedComponentAction = "Upgrade (+)";
    public string SelectedComponentAction
    {
        get => _selectedComponentAction;
        set
        {
            _selectedComponentAction = value;
            OnPropertyChanged();
            if (value != null && value.Contains("Included"))
            {
                ComponentItemPrice = 0m;
            }
        }
    }

    private int _componentItemQty = 1;
    public int ComponentItemQty
    {
        get => _componentItemQty;
        set { _componentItemQty = value <= 0 ? 1 : value; OnPropertyChanged(); }
    }

    private decimal _componentItemPrice = 0m;
    public decimal ComponentItemPrice
    {
        get => _componentItemPrice;
        set { _componentItemPrice = value; OnPropertyChanged(); }
    }

    private string _componentItemNote = "";
    public string ComponentItemNote
    {
        get => _componentItemNote;
        set { _componentItemNote = value; OnPropertyChanged(); }
    }

    // Ad-hoc Custom Item
    private string _customLineDesc = "";
    public string CustomLineDesc
    {
        get => _customLineDesc;
        set { _customLineDesc = value; OnPropertyChanged(); }
    }

    private int _customLineQty = 1;
    public int CustomLineQty
    {
        get => _customLineQty;
        set { _customLineQty = value; OnPropertyChanged(); }
    }

    private decimal _customLinePrice = 0m;
    public decimal CustomLinePrice
    {
        get => _customLinePrice;
        set { _customLinePrice = value; OnPropertyChanged(); }
    }

    private void AutoFillInvoiceDetailsFromSelectedAsset()
    {
        if (SelectedAsset == null) return;
        SelectedInvoiceBaseAsset = SelectedAsset;
        if (!string.IsNullOrWhiteSpace(SelectedAsset.Customer) && IsSalesInvoice)
            DraftPartyName = SelectedAsset.Customer;
        else if (!string.IsNullOrWhiteSpace(SelectedAsset.Supplier) && !IsSalesInvoice)
            DraftPartyName = SelectedAsset.Supplier;
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
    public ICommand QuickIntakeCommand { get; }
    public ICommand AddBaseToDraftCommand { get; }
    public ICommand AddComponentToDraftCommand { get; }
    public ICommand AddCustomLineItemCommand { get; }
    public ICommand RemoveInvoiceItemCommand { get; }
    public ICommand ClearInvoiceDraftCommand { get; }
    public ICommand GenerateInvoiceCommand { get; }
    public ICommand PreviewInvoiceCommand { get; }
    public ICommand ToggleLanServerCommand { get; }
    public ICommand AttachBomComponentCommand { get; }
    public ICommand DetachBomComponentCommand { get; }
    public ICommand ImportFittedToInvoiceCommand { get; }

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
        QuickIntakeCommand = new RelayCommand(async _ => await QuickIntakeStockAsync());
        AddBaseToDraftCommand = new RelayCommand(_ => AddBaseMachineToInvoice());
        AddComponentToDraftCommand = new RelayCommand(_ => AddComponentToInvoice());
        AddCustomLineItemCommand = new RelayCommand(_ => AddCustomLineItem());
        RemoveInvoiceItemCommand = new RelayCommand(p => RemoveInvoiceItem(p as InvoiceItem));
        ClearInvoiceDraftCommand = new RelayCommand(_ => ClearInvoiceDraft());
        GenerateInvoiceCommand = new RelayCommand(async _ => await GenerateInvoiceForSelectedAssetsAsync());
        PreviewInvoiceCommand = new RelayCommand(p => PreviewInvoice(p as Invoice));
        ToggleLanServerCommand = new RelayCommand(_ => ToggleLanServer());
        AttachBomComponentCommand = new RelayCommand(async _ => await AttachBomComponentAsync());
        DetachBomComponentCommand = new RelayCommand(async p => await DetachBomComponentAsync(p as FittedComponentItemViewModel));
        ImportFittedToInvoiceCommand = new RelayCommand(_ => ImportFittedToInvoice());

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

        if (ManagerConfigService.Current.EnableLanServer)
        {
            _lan.Start(ManagerConfigService.Current.LanPort);
        }

        _api.StartRealtimeEvents();
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
                AvailableBaseUnits.Clear();
                ComponentStock.Clear();

                foreach (var a in rawAssets)
                {
                    var vm = new AssetItemViewModel(a);
                    Assets.Add(vm);

                    if (vm.Category == AssetCategory.Laptop || vm.Category == AssetCategory.Desktop)
                    {
                        if (vm.Status != LifecycleQueue.Sold && vm.Status != LifecycleQueue.Disposed)
                        {
                            AvailableBaseUnits.Add(vm);
                        }
                    }
                    else
                    {
                        if (vm.Quantity > 0 && vm.Status != LifecycleQueue.Sold && vm.Status != LifecycleQueue.Disposed)
                        {
                            ComponentStock.Add(vm);
                        }
                    }
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
            // Category check
            if (CategoryFilter != "ALL")
            {
                var matchesCategory = CategoryFilter switch
                {
                    "LAPTOPS" => item.Category == AssetCategory.Laptop || item.Category == AssetCategory.Desktop,
                    "RAM" => item.Category == AssetCategory.RAM,
                    "STORAGE" => item.Category == AssetCategory.Storage,
                    "CHARGERS" => item.Category == AssetCategory.Charger,
                    "PERIPHERALS" => item.Category == AssetCategory.Peripheral,
                    "REPAIR SPARES" => item.Category == AssetCategory.RepairSpare,
                    _ => true
                };
                if (!matchesCategory) continue;
            }

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

    // ── Quick Stock Intake ───────────────────────────────────────────────────────
    public async Task QuickIntakeStockAsync()
    {
        if (string.IsNullOrWhiteSpace(QuickIntakeModel))
        {
            VeloxMessageBox.Show("Please enter a Model Description or Name for the stock item.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var parsedCategory = Enum.TryParse<AssetCategory>(QuickIntakeCategory, true, out var cat) ? cat : AssetCategory.Laptop;
        var qty = QuickIntakeQuantity <= 0 ? 1 : QuickIntakeQuantity;

        var serial = !string.IsNullOrWhiteSpace(QuickIntakeSerial)
            ? QuickIntakeSerial.Trim().ToUpperInvariant()
            : $"{parsedCategory.ToString().ToUpperInvariant()[..3]}-{DateTime.UtcNow:yyMMdd}-{new Random().Next(1000, 9999)}";

        var tag = $"TAG-{new Random().Next(100000, 999999)}";

        var req = new IntakeRequest
        {
            SerialNumber = serial,
            AssetTag = tag,
            Model = QuickIntakeModel.Trim(),
            Category = parsedCategory,
            Quantity = qty,
            PurchasePrice = QuickIntakePurchasePrice > 0 ? QuickIntakePurchasePrice : null,
            SalePrice = QuickIntakeSalePrice > 0 ? QuickIntakeSalePrice : null,
            Notes = string.IsNullOrWhiteSpace(QuickIntakeNotes) ? $"Batch intake via VeloxManager ({parsedCategory})" : QuickIntakeNotes.Trim(),
            Actor = ManagerConfigService.Current.ManagerName,
            Location = "MAIN-DEPOT"
        };

        var created = await _api.IntakeAssetAsync(req);
        if (created != null)
        {
            LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Intaked {qty}x {parsedCategory}: {created.Model} ({serial})");
            VeloxMessageBox.Show($"Successfully intaked into stock:\n• {qty}x {parsedCategory}: {created.Model}\n• Identifier: {serial}\n• Unit Sale Price: ₹{QuickIntakeSalePrice:N2}", "Stock Intaked", MessageBoxButton.OK, MessageBoxImage.Information);

            QuickIntakeModel = "";
            QuickIntakeSerial = "";
            QuickIntakeQuantity = 1;
            QuickIntakePurchasePrice = 0;
            QuickIntakeSalePrice = 0;
            QuickIntakeNotes = "";

            await LoadAssetsAndEmployeesAsync();
        }
        else
        {
            VeloxMessageBox.Show("Failed to intake item to cloud inventory. Please check cloud connection.", "Intake Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Fitted Components (BOM) Actions ──────────────────────────────────────────
    public async Task LoadFittedComponentsForSelectedAssetAsync()
    {
        if (SelectedAsset == null)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                SelectedAssetFittedComponents.Clear();
                OnPropertyChanged(nameof(HasFittedComponents));
            });
            return;
        }

        var list = await _api.GetFittedComponentsAsync(SelectedAsset.Id);
        Application.Current?.Dispatcher.Invoke(() =>
        {
            SelectedAssetFittedComponents.Clear();
            foreach (var item in list)
            {
                SelectedAssetFittedComponents.Add(new FittedComponentItemViewModel(item));
            }
            OnPropertyChanged(nameof(SelectedAssetFittedComponents));
            OnPropertyChanged(nameof(HasFittedComponents));
        });
    }

    public async Task AttachBomComponentAsync()
    {
        if (SelectedAsset == null)
        {
            VeloxMessageBox.Show("Please select an asset from inventory first.", "No Asset Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (AttachFromStock && SelectedBomStockItem == null)
        {
            VeloxMessageBox.Show("Please select a component from stock, or switch to 'Free-Form Quick Spec'.", "Stock Item Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!AttachFromStock && string.IsNullOrWhiteSpace(NewBomDescription))
        {
            VeloxMessageBox.Show("Please enter a component description (e.g. '16GB DDR4 3200MHz').", "Description Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        double.TryParse(NewBomCostDelta, out var cost);
        double.TryParse(NewBomPriceDelta, out var price);
        Enum.TryParse<AssetCategory>(NewBomCategory, true, out var cat);

        var req = new AttachComponentRequest
        {
            ComponentAssetId = AttachFromStock ? SelectedBomStockItem?.Id : null,
            Category = cat,
            Description = AttachFromStock ? SelectedBomStockItem!.ModelName : NewBomDescription.Trim(),
            SerialNumber = AttachFromStock ? SelectedBomStockItem?.SerialNumber : (string.IsNullOrWhiteSpace(NewBomSerial) ? null : NewBomSerial.Trim()),
            CostDelta = cost,
            PriceDelta = price,
            InstalledBy = ManagerConfigService.Current.ManagerName
        };

        var attached = await _api.AttachComponentAsync(SelectedAsset.Id, req);
        if (attached != null)
        {
            // Reset form
            NewBomDescription = "";
            NewBomSerial = "";
            NewBomCostDelta = "0";
            NewBomPriceDelta = "0";
            SelectedBomStockItem = null;

            await LoadFittedComponentsForSelectedAssetAsync();
            await LoadAssetsAndEmployeesAsync();
            VeloxMessageBox.Show($"Component attached to {SelectedAsset.ModelName} successfully.", "Component Fitted", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            VeloxMessageBox.Show("Failed to attach component to cloud.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public async Task DetachBomComponentAsync(FittedComponentItemViewModel? item)
    {
        if (SelectedAsset == null || item == null) return;

        var result = VeloxMessageBox.Show(
            $"Do you want to return '{item.Description}' to inventory as a reusable/tested spare?\n\nClick [OK] to return to stock, or [Cancel] to detach without restocking.",
            "Return Part to Inventory?",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        bool returnToStock = (result == MessageBoxResult.OK);

        var ok = await _api.DetachComponentAsync(SelectedAsset.Id, item.Id, returnToStock: returnToStock, condition: "Used / Tested");
        if (ok)
        {
            await LoadFittedComponentsForSelectedAssetAsync();
            await LoadAssetsAndEmployeesAsync();
            VeloxMessageBox.Show("Component detached successfully.", "Component Removed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public void ImportFittedToInvoice()
    {
        if (SelectedAssetFittedComponents.Count == 0)
        {
            VeloxMessageBox.Show("No fitted components recorded on this asset.", "No Components", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int count = 0;
        foreach (var c in SelectedAssetFittedComponents)
        {
            var cat = c.Category switch
            {
                AssetCategory.RAM => InvoiceItemCategory.RamUpgrade,
                AssetCategory.Storage => InvoiceItemCategory.StorageUpgrade,
                AssetCategory.Charger => InvoiceItemCategory.Charger,
                AssetCategory.Peripheral => InvoiceItemCategory.Peripheral,
                AssetCategory.RepairSpare => InvoiceItemCategory.SparePart,
                _ => InvoiceItemCategory.SparePart
            };

            var invoiceItem = new InvoiceItem
            {
                LineNumber = CurrentInvoiceItems.Count + 1,
                ItemCategory = cat,
                Model = c.Description,
                Description = $"{c.CategoryIcon} {c.Description}",
                SerialNumber = c.SerialNumber != "Untracked" ? c.SerialNumber : "",
                Quantity = 1,
                UnitPrice = (decimal)c.PriceDelta,
                AssetId = c.Model.ComponentAssetId ?? ""
            };
            CurrentInvoiceItems.Add(invoiceItem);
            count++;
        }
        RecalculateDraftTotals();
        VeloxMessageBox.Show($"Imported {count} fitted upgrade(s) into current invoice draft.", "Upgrades Imported", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── Vyapar Refurbishment Billing Actions ─────────────────────────────────────
    public void AddBaseMachineToInvoice()
    {
        var target = SelectedInvoiceBaseAsset ?? SelectedAsset;
        if (target == null)
        {
            VeloxMessageBox.Show("Please select a base machine from the available stock list.", "No Machine Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var unitPrice = (decimal)(target.SalePrice ?? (IsSalesInvoice ? 350.0 : 250.0));

        // Check if already in items
        if (CurrentInvoiceItems.Any(i => i.AssetId == target.Id))
        {
            VeloxMessageBox.Show($"Unit {target.SerialNumber} is already added to this invoice draft.", "Already Added", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var item = new InvoiceItem
        {
            LineNumber = CurrentInvoiceItems.Count + 1,
            AssetId = target.Id,
            SerialNumber = target.SerialNumber,
            Model = target.ModelName,
            Grade = target.GradeBadge,
            Description = $"Refurbished Unit (Disk Health: {target.StorageHealth}%)",
            ItemCategory = InvoiceItemCategory.BaseMachine,
            UnitPrice = unitPrice,
            Quantity = 1
        };

        CurrentInvoiceItems.Add(item);
        RecalculateDraftTotals();
        LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Added Base Machine: {target.SerialNumber} to invoice draft");
    }

    public void AddComponentToInvoice()
    {
        if (SelectedComponentForInvoice == null)
        {
            VeloxMessageBox.Show("Please select a component (RAM, Storage, Charger, Peripheral, Spare) from the stock list.", "No Component Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var cat = SelectedComponentForInvoice.Category switch
        {
            AssetCategory.RAM => InvoiceItemCategory.RamUpgrade,
            AssetCategory.Storage => SelectedComponentAction.Contains("Downgrade") ? InvoiceItemCategory.StorageDowngrade : InvoiceItemCategory.StorageUpgrade,
            AssetCategory.Charger => InvoiceItemCategory.Charger,
            AssetCategory.Peripheral => InvoiceItemCategory.Peripheral,
            AssetCategory.RepairSpare => InvoiceItemCategory.SparePart,
            _ => InvoiceItemCategory.CustomLaborOrDiscount
        };

        var price = ComponentItemPrice;
        if (SelectedComponentAction.Contains("Downgrade"))
        {
            price = -Math.Abs(price); // Negative line total for deduction!
        }
        else if (SelectedComponentAction.Contains("Included"))
        {
            price = 0m;
        }

        var item = new InvoiceItem
        {
            LineNumber = CurrentInvoiceItems.Count + 1,
            AssetId = SelectedComponentForInvoice.Id,
            SerialNumber = SelectedComponentForInvoice.SerialNumber,
            Model = SelectedComponentForInvoice.ModelName,
            Grade = SelectedComponentForInvoice.GradeBadge,
            Description = string.IsNullOrWhiteSpace(ComponentItemNote) ? $"{SelectedComponentForInvoice.CategoryBadge} Refurbishment Component" : ComponentItemNote.Trim(),
            ItemCategory = cat,
            UnitPrice = price,
            Quantity = ComponentItemQty <= 0 ? 1 : ComponentItemQty
        };

        CurrentInvoiceItems.Add(item);
        RecalculateDraftTotals();
        LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Attached {item.Quantity}x {item.Model} to invoice draft");
    }

    public void AddCustomLineItem()
    {
        if (string.IsNullOrWhiteSpace(CustomLineDesc))
        {
            VeloxMessageBox.Show("Please enter a description for the custom item or labor fee.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var item = new InvoiceItem
        {
            LineNumber = CurrentInvoiceItems.Count + 1,
            AssetId = "",
            SerialNumber = "—",
            Model = CustomLineDesc.Trim(),
            Grade = "N/A",
            Description = "Custom Line Item / Labor / Refurbishment Service",
            ItemCategory = InvoiceItemCategory.CustomLaborOrDiscount,
            UnitPrice = CustomLinePrice,
            Quantity = CustomLineQty <= 0 ? 1 : CustomLineQty
        };

        CurrentInvoiceItems.Add(item);
        RecalculateDraftTotals();

        CustomLineDesc = "";
        CustomLineQty = 1;
        CustomLinePrice = 0m;
    }

    public void RemoveInvoiceItem(InvoiceItem? item)
    {
        if (item == null) return;
        CurrentInvoiceItems.Remove(item);

        int line = 1;
        foreach (var it in CurrentInvoiceItems)
        {
            it.LineNumber = line++;
        }
        RecalculateDraftTotals();
    }

    public void ClearInvoiceDraft()
    {
        CurrentInvoiceItems.Clear();
        RecalculateDraftTotals();
    }

    public async Task GenerateInvoiceForSelectedAssetsAsync()
    {
        // If current items list is empty, try falling back to currently selected asset
        if (CurrentInvoiceItems.Count == 0 && SelectedAsset != null)
        {
            AddBaseMachineToInvoice();
        }

        if (CurrentInvoiceItems.Count == 0)
        {
            VeloxMessageBox.Show("No items added to invoice draft.\nPlease select a base machine or add components to proceed.", "Empty Invoice", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var inv = new Invoice
        {
            Type = SelectedInvoiceType,
            PartyName = string.IsNullOrWhiteSpace(DraftPartyName) ? (IsSalesInvoice ? "Walk-in Retail Client" : "Hardware Vendor") : DraftPartyName.Trim(),
            PartyPhone = DraftPartyPhone?.Trim() ?? "",
            PartyEmail = DraftPartyAddress?.Trim() ?? "",
            PartyAddress = DraftPartyAddress?.Trim() ?? "Counter Sale",
            PaymentMode = DraftPaymentMode,
            TaxPercent = DraftTaxPercent,
            Notes = string.IsNullOrWhiteSpace(DraftNotes) ? "Certified Refurbished Hardware. 30 Days Depot Warranty Included." : DraftNotes.Trim(),
            Items = CurrentInvoiceItems.ToList()
        };

        Invoices.Insert(0, inv);
        PreviewInvoice(inv);
        LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Generated {inv.Type} #{inv.InvoiceNumber} for {inv.PartyName} (Total: ₹{inv.TotalAmount:N2})");

        // Automatically update asset lifecycle and decrement stock if sales invoice
        if (IsSalesInvoice && MarkAsSoldOnInvoice)
        {
            foreach (var item in inv.Items)
            {
                if (string.IsNullOrWhiteSpace(item.AssetId)) continue;

                var asset = Assets.FirstOrDefault(a => a.Id == item.AssetId);
                if (asset == null) continue;

                if (item.ItemCategory == InvoiceItemCategory.BaseMachine || asset.Category == AssetCategory.Laptop || asset.Category == AssetCategory.Desktop)
                {
                    var success = await _api.UpdateAssetStatusAsync(asset.Id, LifecycleQueue.Sold, $"Sold via Invoice #{inv.InvoiceNumber}");
                    if (success)
                    {
                        asset.Status = LifecycleQueue.Sold;
                        LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Asset {asset.SerialNumber} updated to SOLD via Invoice #{inv.InvoiceNumber}");
                    }
                }
                else
                {
                    // Component stock decrement
                    var remQty = Math.Max(0, asset.Quantity - item.Quantity);
                    var newStatus = remQty == 0 ? LifecycleQueue.Sold : asset.Status;
                    var success = await _api.PatchAssetAsync(asset.Id, new PatchAssetRequest
                    {
                        Quantity = remQty,
                        LifecycleQueue = newStatus,
                        Notes = $"{asset.Notes} | Consumed {item.Quantity} in Invoice #{inv.InvoiceNumber}"
                    });

                    if (success)
                    {
                        asset.Quantity = remQty;
                        asset.Status = newStatus;
                        LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Stock adjusted for {asset.ModelName}: -{item.Quantity} (Remaining: {remQty})");
                    }
                }
            }

            await LoadAssetsAndEmployeesAsync();
        }

        VeloxMessageBox.Show($"Commercial Invoice #{inv.InvoiceNumber} generated successfully!\nGrand Total: ₹{inv.TotalAmount:N2}\nAll consumed inventory stock has been adjusted.", "Invoice Generated", MessageBoxButton.OK, MessageBoxImage.Information);

        ClearInvoiceDraft();
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
            VeloxMessageBox.Show($"Could not open invoice preview: {ex.Message}", "Velox Invoice Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Asset Status Mutation ──────────────────────────────────────────────────
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

    // ── Employee Management ────────────────────────────────────────────────────
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
            VeloxMessageBox.Show($"Technician {emp.Name} registered successfully with Station ID: {emp.StationId}", "Technician Created", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public async Task ResetSelectedEmployeePinAsync(string? pinOverride = null)
    {
        if (SelectedEmployeeForEdit == null) return;
        var newPin = !string.IsNullOrWhiteSpace(pinOverride) ? pinOverride.Trim() : (!string.IsNullOrWhiteSpace(EditEmployeePin) ? EditEmployeePin.Trim() : "1234");
        if (newPin.Length < 4)
        {
            VeloxMessageBox.Show("PIN must be at least 4 digits.", "Invalid PIN", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ok = await _api.ResetEmployeePinAsync(SelectedEmployeeForEdit.Id, newPin);
        if (ok)
        {
            LiveActivityFeed.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Station PIN updated for {SelectedEmployeeForEdit.Name} (PIN: {newPin})");
            EditEmployeePin = "";
            VeloxMessageBox.Show($"Station PIN successfully updated for {SelectedEmployeeForEdit.Name} to: {newPin}", "PIN Updated", MessageBoxButton.OK, MessageBoxImage.Information);
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
            VeloxMessageBox.Show($"Station successfully assigned: {newStation}", "Station Updated", MessageBoxButton.OK, MessageBoxImage.Information);
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

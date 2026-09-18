using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using SuperAutoMater.Wpf.Core;

namespace SuperManager.ViewModels
{
    /// <summary>
    /// ViewModel driving the 10-queue Warehouse WIP Kanban Board in SuperManager across 3 operational zones,
    /// backed by the canonical SQLite database and domain orchestrator.
    /// </summary>
    public sealed class WipBoardViewModel : INotifyPropertyChanged
    {
        private readonly WarehouseJourneyService _warehouseService;
        private readonly QcRunStore _store;

        // Zone 1: Intake & Testing
        public ObservableCollection<AssetWipRecord> IntakeStagingLane { get; } = new ObservableCollection<AssetWipRecord>();
        public ObservableCollection<AssetWipRecord> ReadyForTestLane => IntakeStagingLane;

        public ObservableCollection<AssetWipRecord> ActiveTestingLane { get; } = new ObservableCollection<AssetWipRecord>();
        public ObservableCollection<AssetWipRecord> InTestLane => ActiveTestingLane;

        public ObservableCollection<AssetWipRecord> ReadyForRetestLane { get; } = new ObservableCollection<AssetWipRecord>();
        public ObservableCollection<AssetWipRecord> RetestLane => ReadyForRetestLane;

        // Zone 2: Triage & Repair
        public ObservableCollection<AssetWipRecord> AwaitingPartsLane { get; } = new ObservableCollection<AssetWipRecord>();
        public ObservableCollection<AssetWipRecord> HoldLane => AwaitingPartsLane;

        public ObservableCollection<AssetWipRecord> InHouseRepairLane { get; } = new ObservableCollection<AssetWipRecord>();
        public ObservableCollection<AssetWipRecord> RepairLane => InHouseRepairLane;

        public ObservableCollection<AssetWipRecord> AdvancedIcExternalLane { get; } = new ObservableCollection<AssetWipRecord>();

        // Zone 3: Commercial Release & Harvest
        public ObservableCollection<AssetWipRecord> ReadyForSaleLane { get; } = new ObservableCollection<AssetWipRecord>();
        public ObservableCollection<AssetWipRecord> ReadyForReleaseLane => ReadyForSaleLane;

        public ObservableCollection<AssetWipRecord> ReadyForRentalLane { get; } = new ObservableCollection<AssetWipRecord>();
        public ObservableCollection<AssetWipRecord> DemoStockLane { get; } = new ObservableCollection<AssetWipRecord>();

        public ObservableCollection<AssetWipRecord> ScrapHarvestLane { get; } = new ObservableCollection<AssetWipRecord>();
        public ObservableCollection<AssetWipRecord> DisposedLane => ScrapHarvestLane;

        public ObservableCollection<AgingWipUnit> TenOldestWipUnits { get; } = new ObservableCollection<AgingWipUnit>();

        private DepotKpiSummary _kpiSummary = new DepotKpiSummary();
        public DepotKpiSummary KpiSummary { get => _kpiSummary; set { _kpiSummary = value; OnPropertyChanged(); } }

        private double _firstTimePassRate;
        public double FirstTimePassRate { get => _firstTimePassRate; set { _firstTimePassRate = value; OnPropertyChanged(); } }

        private int _dailyThroughput;
        public int DailyThroughput { get => _dailyThroughput; set { _dailyThroughput = value; OnPropertyChanged(); } }

        private int _activeExceptionsCount;
        public int ActiveExceptionsCount { get => _activeExceptionsCount; set { _activeExceptionsCount = value; OnPropertyChanged(); } }

        private string _oldestWipAgeDisplay = "0h";
        public string OldestWipAgeDisplay { get => _oldestWipAgeDisplay; set { _oldestWipAgeDisplay = value; OnPropertyChanged(); } }

        private int _totalAssets;
        public int TotalAssets { get => _totalAssets; set { _totalAssets = value; OnPropertyChanged(); } }

        private string _searchQuery = "";
        public string SearchQuery { get => _searchQuery; set { _searchQuery = value; OnPropertyChanged(); } }

        private AssetWipRecord _selectedAsset;
        public AssetWipRecord SelectedAsset { get => _selectedAsset; set { _selectedAsset = value; OnPropertyChanged(); LoadSelectedJourney(); } }

        private AssetJourneySummary _selectedJourney;
        public AssetJourneySummary SelectedJourney { get => _selectedJourney; set { _selectedJourney = value; OnPropertyChanged(); } }

        private string _actionMessage = "";
        public string ActionMessage { get => _actionMessage; set { _actionMessage = value; OnPropertyChanged(); } }

        // Intake Form fields
        private string _intakeTag = "";
        public string IntakeTag { get => _intakeTag; set { _intakeTag = value; OnPropertyChanged(); } }

        private string _intakeSerial = "";
        public string IntakeSerial { get => _intakeSerial; set { _intakeSerial = value; OnPropertyChanged(); } }

        private bool _isSerialMissing = false;
        public bool IsSerialMissing { get => _isSerialMissing; set { _isSerialMissing = value; OnPropertyChanged(); } }

        private string _intakeModel = "";
        public string IntakeModel { get => _intakeModel; set { _intakeModel = value; OnPropertyChanged(); } }

        private string _intakeBatch = "BATCH-" + DateTime.UtcNow.ToString("yyyyMMdd");
        public string IntakeBatch { get => _intakeBatch; set { _intakeBatch = value; OnPropertyChanged(); } }

        private string _intakeLocation = "INTAKE-STAGING";
        public string IntakeLocation { get => _intakeLocation; set { _intakeLocation = value; OnPropertyChanged(); } }

        private string _intakeSupplier = "";
        public string IntakeSupplier { get => _intakeSupplier; set { _intakeSupplier = value; OnPropertyChanged(); } }

        private string _intakeAssignedTo = "";
        public string IntakeAssignedTo { get => _intakeAssignedTo; set { _intakeAssignedTo = value; OnPropertyChanged(); } }

        private string _intakeMissingComponents = "";
        public string IntakeMissingComponents { get => _intakeMissingComponents; set { _intakeMissingComponents = value; OnPropertyChanged(); } }

        public WipBoardViewModel(WarehouseJourneyService warehouseService = null, QcRunStore store = null)
        {
            _store = store ?? new QcRunStore();
            _warehouseService = warehouseService ?? new WarehouseJourneyService(_store);
            RefreshBoard();
        }

        public void AutoGenerateTag()
        {
            IntakeTag = $"TAG-{DateTime.UtcNow:yyMM}-{new Random().Next(100000, 999999)}";
        }

        public void RefreshBoard()
        {
            try
            {
                var board = _warehouseService.GetWipBoard();
                UpdateLane(IntakeStagingLane, board[AssetQueueStatus.IntakeStaging]);
                UpdateLane(ActiveTestingLane, board[AssetQueueStatus.ActiveTesting]);
                UpdateLane(ReadyForRetestLane, board[AssetQueueStatus.ReadyForRetest]);
                UpdateLane(AwaitingPartsLane, board[AssetQueueStatus.AwaitingParts]);
                UpdateLane(InHouseRepairLane, board[AssetQueueStatus.InHouseRepair]);
                UpdateLane(AdvancedIcExternalLane, board[AssetQueueStatus.AdvancedIcExternal]);
                UpdateLane(ReadyForSaleLane, board[AssetQueueStatus.ReadyForSale]);
                UpdateLane(ReadyForRentalLane, board[AssetQueueStatus.ReadyForRental]);
                UpdateLane(DemoStockLane, board[AssetQueueStatus.DemoStock]);
                UpdateLane(ScrapHarvestLane, board[AssetQueueStatus.ScrapHarvest]);

                TotalAssets = IntakeStagingLane.Count + ActiveTestingLane.Count + ReadyForRetestLane.Count +
                              AwaitingPartsLane.Count + InHouseRepairLane.Count + AdvancedIcExternalLane.Count +
                              ReadyForSaleLane.Count + ReadyForRentalLane.Count + DemoStockLane.Count + ScrapHarvestLane.Count;

                // Load Depot OS operational KPIs & Aging WIP
                var kpi = _store.GetDepotKpis();
                KpiSummary = kpi;
                FirstTimePassRate = kpi.FirstTimePassRatePercent;
                DailyThroughput = kpi.DailyThroughput;
                ActiveExceptionsCount = kpi.ActiveExceptionsCount;
                OldestWipAgeDisplay = kpi.OldestWipUnitAgeHours < 24
                    ? $"{Math.Round(kpi.OldestWipUnitAgeHours, 1)}h"
                    : $"{Math.Round(kpi.OldestWipUnitAgeHours / 24.0, 1)}d";

                var aging = _store.GetAgingWip(10);
                TenOldestWipUnits.Clear();
                foreach (var unit in aging)
                {
                    TenOldestWipUnits.Add(unit);
                }
            }
            catch (Exception ex)
            {
                ActionMessage = $"Refresh failed: {ex.Message}";
            }
        }

        private void UpdateLane(ObservableCollection<AssetWipRecord> collection, List<AssetWipRecord> items)
        {
            collection.Clear();
            if (items != null)
            {
                foreach (var item in items)
                {
                    collection.Add(item);
                }
            }
        }

        public void ExecuteIntake()
        {
            try
            {
                var req = new AssetIntakeRequest
                {
                    AssetTag = string.IsNullOrWhiteSpace(IntakeTag) ? null : IntakeTag.Trim(),
                    SerialNumber = IsSerialMissing ? "" : IntakeSerial?.Trim(),
                    IsSerialMissing = IsSerialMissing || string.IsNullOrWhiteSpace(IntakeSerial),
                    Model = IntakeModel?.Trim(),
                    IntakeBatchId = IntakeBatch?.Trim(),
                    InitialLocation = IntakeLocation?.Trim(),
                    Technician = Environment.UserName,
                    Supplier = IntakeSupplier?.Trim(),
                    AssignedTo = IntakeAssignedTo?.Trim(),
                    MissingComponents = IntakeMissingComponents?.Trim()
                };
                string id = _warehouseService.Intake(req);
                ActionMessage = $"✓ Successfully intaked device {req.AssetTag ?? id} ({id})";
                IntakeTag = "";
                IntakeSerial = "";
                IntakeMissingComponents = "";
                IsSerialMissing = false;
                RefreshBoard();
            }
            catch (Exception ex)
            {
                ActionMessage = $"❌ Intake failed: {ex.Message}";
            }
        }

        public void AssignTechnician(AssetWipRecord asset, string technician)
        {
            if (asset == null) return;
            try
            {
                _warehouseService.AssignTechnician(asset.AssetId, technician, Environment.UserName);
                ActionMessage = $"✓ Assigned {asset.AssetTag} to {technician}";
                RefreshBoard();
            }
            catch (Exception ex)
            {
                ActionMessage = $"❌ Assignment failed: {ex.Message}";
            }
        }

        public void TransitionAsset(AssetWipRecord asset, AssetQueueStatus targetQueue, string reasonCode = null, string extraParam = null)
        {
            if (asset == null) return;
            if (asset.LifecycleQueue == targetQueue) return;

            try
            {
                if (targetQueue == AssetQueueStatus.ReadyForSale)
                {
                    try
                    {
                        _warehouseService.ReleaseToSale(asset.AssetId, Environment.UserName);
                    }
                    catch
                    {
                        // Fallback: supervisor manual move
                        _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.ReadyForSale, "DISPATCH-RTS", Environment.UserName, reasonCode ?? "MANUAL_RELEASE_MOVE", "Manual drag-and-drop to release for sale", true, null, "RTS");
                    }
                }
                else if (targetQueue == AssetQueueStatus.ReadyForRental)
                {
                    try
                    {
                        _warehouseService.ReleaseToRental(asset.AssetId, Environment.UserName);
                    }
                    catch
                    {
                        // Fallback: supervisor manual move
                        _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.ReadyForRental, "DISPATCH-RFR", Environment.UserName, reasonCode ?? "MANUAL_RENTAL_MOVE", "Manual move to rental fleet", true, null, "RFR");
                    }
                }
                else if (targetQueue == AssetQueueStatus.DemoStock)
                {
                    _warehouseService.ReleaseToDemo(asset.AssetId, Environment.UserName);
                }
                else if (targetQueue == AssetQueueStatus.ScrapHarvest)
                {
                    _warehouseService.ReleaseToScrap(asset.AssetId, reasonCode ?? "BEYOND_ECONOMIC_REPAIR", Environment.UserName);
                }
                else if (targetQueue == AssetQueueStatus.InHouseRepair)
                {
                    _warehouseService.SendToInHouseRepair(asset.AssetId, reasonCode ?? "BENCH_FAILURE", Environment.UserName);
                }
                else if (targetQueue == AssetQueueStatus.AdvancedIcExternal)
                {
                    string vendor = !string.IsNullOrWhiteSpace(extraParam) ? extraParam : (!string.IsNullOrWhiteSpace(asset.ExternalVendor) ? asset.ExternalVendor : "ExternalMicroSoldering");
                    _warehouseService.SendToExternalIcRepair(asset.AssetId, vendor, reasonCode ?? "Advanced IC / BGA rework dispatched", Environment.UserName);
                }
                else if (targetQueue == AssetQueueStatus.AwaitingParts)
                {
                    _warehouseService.SendToAwaitingParts(asset.AssetId, reasonCode ?? "PARTS_NEEDED", Environment.UserName, extraParam ?? asset.MissingComponents);
                }
                else if (targetQueue == AssetQueueStatus.ReadyForRetest)
                {
                    _warehouseService.SendToRetest(asset.AssetId, Environment.UserName);
                }
                else if (targetQueue == AssetQueueStatus.ActiveTesting)
                {
                    _warehouseService.SendToActiveTesting(asset.AssetId, Environment.UserName);
                }
                else
                {
                    _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.IntakeStaging, "INTAKE-STAGING", Environment.UserName, reasonCode ?? "READY_FOR_TEST", "Manual move to intake staging");
                }

                ActionMessage = $"✓ Moved {asset.AssetTag} ({asset.SerialNumber}) → {targetQueue}";
                RefreshBoard();
            }
            catch (Exception ex)
            {
                ActionMessage = $"❌ Transition error: {ex.Message}";
            }
        }

        public void TransitionSelected(AssetQueueStatus targetQueue, string reasonCode = null)
        {
            if (SelectedAsset == null) return;
            TransitionAsset(SelectedAsset, targetQueue, reasonCode);
        }

        public void SearchAsset()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return;
            var asset = _store.FindAssetByAnyIdentifier(SearchQuery);
            if (asset != null)
            {
                SelectedAsset = asset;
                ActionMessage = $"Found asset {asset.AssetTag} in queue {asset.LifecycleQueue}";
            }
            else
            {
                ActionMessage = $"No asset matching '{SearchQuery}'";
            }
        }

        private void LoadSelectedJourney()
        {
            if (SelectedAsset == null)
            {
                SelectedJourney = null;
                return;
            }
            SelectedJourney = _store.GetAssetJourney(SelectedAsset.AssetId);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}


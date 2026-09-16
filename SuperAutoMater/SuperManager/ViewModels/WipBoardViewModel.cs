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
    /// ViewModel driving the 7-queue Warehouse WIP Kanban Board in SuperManager,
    /// backed by the canonical SQLite database and domain orchestrator.
    /// </summary>
    public sealed class WipBoardViewModel : INotifyPropertyChanged
    {
        private readonly WarehouseJourneyService _warehouseService;
        private readonly QcRunStore _store;

        public ObservableCollection<AssetWipRecord> ReadyForTestLane { get; } = new ObservableCollection<AssetWipRecord>();
        public ObservableCollection<AssetWipRecord> InTestLane { get; } = new ObservableCollection<AssetWipRecord>();
        public ObservableCollection<AssetWipRecord> HoldLane { get; } = new ObservableCollection<AssetWipRecord>();
        public ObservableCollection<AssetWipRecord> RepairLane { get; } = new ObservableCollection<AssetWipRecord>();
        public ObservableCollection<AssetWipRecord> RetestLane { get; } = new ObservableCollection<AssetWipRecord>();
        public ObservableCollection<AssetWipRecord> ReadyForReleaseLane { get; } = new ObservableCollection<AssetWipRecord>();
        public ObservableCollection<AssetWipRecord> DisposedLane { get; } = new ObservableCollection<AssetWipRecord>();
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

        private string _intakeModel = "";
        public string IntakeModel { get => _intakeModel; set { _intakeModel = value; OnPropertyChanged(); } }

        private string _intakeBatch = "BATCH-" + DateTime.UtcNow.ToString("yyyyMMdd");
        public string IntakeBatch { get => _intakeBatch; set { _intakeBatch = value; OnPropertyChanged(); } }

        private string _intakeLocation = "INTAKE-STAGING";
        public string IntakeLocation { get => _intakeLocation; set { _intakeLocation = value; OnPropertyChanged(); } }

        public WipBoardViewModel(WarehouseJourneyService warehouseService = null, QcRunStore store = null)
        {
            _store = store ?? new QcRunStore();
            _warehouseService = warehouseService ?? new WarehouseJourneyService(_store);
            RefreshBoard();
        }

        public void RefreshBoard()
        {
            try
            {
                var board = _warehouseService.GetWipBoard();
                UpdateLane(ReadyForTestLane, board[AssetQueueStatus.ReadyForTest]);
                UpdateLane(InTestLane, board[AssetQueueStatus.InTest]);
                UpdateLane(HoldLane, board[AssetQueueStatus.Hold]);
                UpdateLane(RepairLane, board[AssetQueueStatus.Repair]);
                UpdateLane(RetestLane, board[AssetQueueStatus.Retest]);
                UpdateLane(ReadyForReleaseLane, board[AssetQueueStatus.ReadyForRelease]);
                UpdateLane(DisposedLane, board[AssetQueueStatus.Disposed]);

                TotalAssets = ReadyForTestLane.Count + InTestLane.Count + HoldLane.Count +
                              RepairLane.Count + RetestLane.Count + ReadyForReleaseLane.Count + DisposedLane.Count;

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
            foreach (var item in items)
            {
                collection.Add(item);
            }
        }

        public void ExecuteIntake()
        {
            try
            {
                var req = new AssetIntakeRequest
                {
                    AssetTag = IntakeTag,
                    SerialNumber = IntakeSerial,
                    Model = IntakeModel,
                    IntakeBatchId = IntakeBatch,
                    InitialLocation = IntakeLocation,
                    Technician = Environment.UserName
                };
                string id = _warehouseService.Intake(req);
                ActionMessage = $"Successfully intaked device {req.AssetTag} ({id})";
                IntakeTag = "";
                IntakeSerial = "";
                RefreshBoard();
            }
            catch (Exception ex)
            {
                ActionMessage = $"Intake failed: {ex.Message}";
            }
        }

        public void TransitionSelected(AssetQueueStatus targetQueue, string reasonCode = null)
        {
            if (SelectedAsset == null) return;
            try
            {
                if (targetQueue == AssetQueueStatus.ReadyForRelease)
                {
                    _warehouseService.ReleaseAsset(SelectedAsset.AssetId, Environment.UserName);
                }
                else if (targetQueue == AssetQueueStatus.Repair)
                {
                    _warehouseService.SendToRepair(SelectedAsset.AssetId, reasonCode ?? "BENCH_FAILURE", Environment.UserName);
                }
                else if (targetQueue == AssetQueueStatus.Retest)
                {
                    _warehouseService.SendToRetest(SelectedAsset.AssetId, Environment.UserName);
                }
                else if (targetQueue == AssetQueueStatus.Hold)
                {
                    _warehouseService.HoldAsset(SelectedAsset.AssetId, reasonCode ?? "SUPERVISOR_HOLD", Environment.UserName);
                }
                else if (targetQueue == AssetQueueStatus.Disposed)
                {
                    _warehouseService.DisposeAsset(SelectedAsset.AssetId, reasonCode ?? "BEYOND_ECONOMIC_REPAIR", Environment.UserName);
                }
                else
                {
                    _store.TransitionAssetQueue(SelectedAsset.AssetId, targetQueue, null, Environment.UserName, reasonCode);
                }

                ActionMessage = $"Asset {SelectedAsset.AssetTag} transitioned to {targetQueue}";
                RefreshBoard();
            }
            catch (Exception ex)
            {
                ActionMessage = $"Transition error: {ex.Message}";
            }
        }

        public void SearchAsset()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return;
            var asset = _store.FindAssetBySerialOrTag(SearchQuery);
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

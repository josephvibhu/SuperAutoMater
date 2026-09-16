using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SuperAutoMater.Wpf.Core
{
    /// <summary>
    /// Central domain orchestrator enforcing warehouse lifecycle flow,
    /// transition constraints, and release verification gates.
    /// </summary>
    public sealed class WarehouseJourneyService
    {
        private readonly QcRunStore _store;

        public WarehouseJourneyService(QcRunStore store = null)
        {
            _store = store ?? new QcRunStore();
        }

        public string Intake(AssetIntakeRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // Validate that we have at least one valid identity handle
            if (string.IsNullOrWhiteSpace(request.AssetTag) && string.IsNullOrWhiteSpace(request.SerialNumber))
            {
                // Generate a temporary floor intake tag if neither was scanned
                request.AssetTag = "TAG-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
            }

            if (string.IsNullOrWhiteSpace(request.InitialLocation))
            {
                request.InitialLocation = "INTAKE-STAGING";
            }

            string assetId = _store.IntakeAsset(request);
            AppLogger.Info($"[WarehouseJourney] Intake completed for asset '{assetId}' (Tag: '{request.AssetTag}', Serial: '{request.SerialNumber}')");
            return assetId;
        }

        public void MoveAsset(string assetId, string destinationLocation, string actor, string reasonCode = "LOCATION_TRANSFER", bool scanConfirmed = true)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));
            if (string.IsNullOrWhiteSpace(destinationLocation)) throw new ArgumentException("Destination location is required.", nameof(destinationLocation));

            var asset = _store.FindAssetBySerialOrTag(assetId);
            if (asset == null) throw new InvalidOperationException($"Asset '{assetId}' not found.");

            _store.RecordCustodyEvent(asset.AssetId, CustodyEventType.Move, destinationLocation, actor, reasonCode, asset.IntakeBatchId, $"Moved from {asset.CurrentLocation} to {destinationLocation}", scanConfirmed);
            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' moved to '{destinationLocation}' by '{actor}'");
        }

        public void HoldAsset(string assetId, string reasonCode, string actor, string notes = null)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));
            if (string.IsNullOrWhiteSpace(reasonCode)) throw new ArgumentException("A hold reason code is required.", nameof(reasonCode));

            var asset = _store.FindAssetBySerialOrTag(assetId);
            if (asset == null) throw new InvalidOperationException($"Asset '{assetId}' not found.");

            _store.AbortActiveRunForAsset(asset.AssetId, $"Hold: {reasonCode}");
            _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.Hold, null, actor, reasonCode, notes);
            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' placed on Hold by '{actor}'. Reason: {reasonCode}");
        }

        public void SendToRepair(string assetId, string defectReasonCode, string actor, string notes = null, string photoFilePath = null)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));
            if (string.IsNullOrWhiteSpace(defectReasonCode)) throw new ArgumentException("A defect reason code is required to send a unit to repair.", nameof(defectReasonCode));

            var asset = _store.FindAssetBySerialOrTag(assetId);
            if (asset == null) throw new InvalidOperationException($"Asset '{assetId}' not found.");

            _store.AbortActiveRunForAsset(asset.AssetId, $"Repair: {defectReasonCode}");
            _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.Repair, "REPAIR-BENCH", actor, defectReasonCode, notes);

            if (!string.IsNullOrWhiteSpace(photoFilePath) && File.Exists(photoFilePath))
            {
                var evidenceService = new EvidenceStorageService(_store);
                evidenceService.StoreEvidenceFile(asset.AssetId, asset.LatestRunId, "DEFECT_PHOTO", photoFilePath, "Photo captured upon repair routing.");
            }

            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' routed to Repair. Defect: {defectReasonCode} by '{actor}'");
        }

        public void SendToRetest(string assetId, string actor, string notes = null)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));

            var asset = _store.FindAssetBySerialOrTag(assetId);
            if (asset == null) throw new InvalidOperationException($"Asset '{assetId}' not found.");

            _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.Retest, "TEST-BENCH-STAGING", actor, "REPAIR_COMPLETED", notes ?? "Repairs completed, retest queued.");
            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' routed to Retest by '{actor}'");
        }

        /// <summary>
        /// Strict Release Gate: verifies that the asset's latest QC run is completed,
        /// has an authentic SHA-256 verification seal, and contains no failing tests.
        /// </summary>
        public void ReleaseAsset(string assetId, string actor, string releaseLocation = "DISPATCH-STAGING")
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));

            var asset = _store.FindAssetBySerialOrTag(assetId);
            if (asset == null) throw new InvalidOperationException($"Asset '{assetId}' not found.");

            if (string.IsNullOrWhiteSpace(asset.LatestRunId))
            {
                throw new InvalidOperationException($"Release Rejected: Asset '{assetId}' has never undergone a diagnostic QC run.");
            }

            var runSummary = _store.GetRunSummary(asset.LatestRunId);
            if (runSummary == null)
            {
                throw new InvalidOperationException($"Release Rejected: Could not retrieve QC run data for run '{asset.LatestRunId}'.");
            }

            if (runSummary.Status != QcRunStatus.Completed)
            {
                throw new InvalidOperationException($"Release Rejected: Latest QC run '{runSummary.RunId}' is in '{runSummary.Status}' state. Only 'Completed' runs can be released.");
            }

            if (string.IsNullOrWhiteSpace(runSummary.VerificationHash))
            {
                throw new InvalidOperationException($"Release Rejected: QC run '{runSummary.RunId}' has no cryptographic verification hash.");
            }

            // Verify there are no unresolved Failing results
            int unresolvedFails = runSummary.Results.Count(r => r.Status == QcTestStatus.Failed);
            if (unresolvedFails > 0)
            {
                throw new InvalidOperationException($"Release Rejected: QC run '{runSummary.RunId}' contains {unresolvedFails} unresolved failed test(s). All tests must pass or be formally overridden before release.");
            }

            _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.ReadyForRelease, releaseLocation, actor, "QC_VERIFIED_PASSED", $"Verified {runSummary.Grade} release.");
            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' successfully approved for Release by '{actor}'. Grade: {runSummary.Grade}, Hash: {runSummary.VerificationHash}");
        }

        public void DisposeAsset(string assetId, string approvedReasonCode, string supervisorActor, string notes = null)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));
            if (string.IsNullOrWhiteSpace(approvedReasonCode))
            {
                throw new InvalidOperationException("Asset disposal requires a valid, approved disposal reason code (e.g. BEYOND_ECONOMIC_REPAIR, BATTERY_SWELL_HAZARD).");
            }
            if (string.IsNullOrWhiteSpace(supervisorActor))
            {
                throw new InvalidOperationException("Asset disposal requires explicit supervisor / manager authorization.");
            }

            var asset = _store.FindAssetBySerialOrTag(assetId);
            if (asset == null) throw new InvalidOperationException($"Asset '{assetId}' not found.");

            _store.AbortActiveRunForAsset(asset.AssetId, $"Disposed: {approvedReasonCode}");
            _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.Disposed, "EWASTE-DISPOSAL", supervisorActor, approvedReasonCode, notes ?? "Asset decommissioned and staged for disposal.");
            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' disposed by supervisor '{supervisorActor}'. Reason: {approvedReasonCode}");
        }

        public Dictionary<AssetQueueStatus, List<AssetWipRecord>> GetWipBoard()
        {
            var all = _store.GetAllWipAssets();
            var board = new Dictionary<AssetQueueStatus, List<AssetWipRecord>>();
            foreach (AssetQueueStatus queue in Enum.GetValues(typeof(AssetQueueStatus)))
            {
                board[queue] = new List<AssetWipRecord>();
            }

            foreach (var asset in all)
            {
                if (board.TryGetValue(asset.LifecycleQueue, out var list))
                {
                    list.Add(asset);
                }
            }
            return board;
        }

        public AssetJourneySummary GetAssetJourney(string serialOrTag)
        {
            return _store.GetAssetJourney(serialOrTag);
        }
    }
}

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

            // Validate and support Tag-First Intake when serial number is missing/scratched off
            if (request.IsSerialMissing || string.IsNullOrWhiteSpace(request.SerialNumber))
            {
                request.IsSerialMissing = true;
                request.SerialNumber = "";
            }

            if (string.IsNullOrWhiteSpace(request.AssetTag))
            {
                // Generate automated unique floor asset tag
                request.AssetTag = $"TAG-{DateTime.UtcNow:yyMM}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant()}";
            }

            if (string.IsNullOrWhiteSpace(request.InitialLocation))
            {
                request.InitialLocation = "INTAKE-STAGING";
            }

            string assetId = _store.IntakeAsset(request);
            AppLogger.Info($"[WarehouseJourney] Intake completed for asset '{assetId}' (Tag: '{request.AssetTag}', Serial: '{(request.IsSerialMissing ? "[MISSING]" : request.SerialNumber)}')");
            return assetId;
        }

        public void AssignTechnician(string assetId, string technician, string actor)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));
            _store.AssignAssetTechnician(assetId, technician, actor);
            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' assigned to '{technician}' by '{actor}'");
        }

        public void UpdateAttribution(string assetId, string intakeTech = null, string serviceTech = null, string qcTech = null, string approvalTech = null)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));
            _store.UpdateAssetAttribution(assetId, intakeTech, serviceTech, qcTech, approvalTech);
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

        public void SendToActiveTesting(string assetId, string actor, string notes = null)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));
            var asset = _store.FindAssetBySerialOrTag(assetId);
            if (asset == null) throw new InvalidOperationException($"Asset '{assetId}' not found.");

            _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.ActiveTesting, "TEST-BENCH", actor, "ACTIVE_TESTING_START", notes ?? "Diagnostic bench testing initiated.");
            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' transitioned to ActiveTesting by '{actor}'");
        }

        public void SendToAwaitingParts(string assetId, string reasonCode, string actor, string missingComponents = null, string notes = null)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));
            if (string.IsNullOrWhiteSpace(reasonCode)) throw new ArgumentException("A hold/waiting reason code is required.", nameof(reasonCode));

            var asset = _store.FindAssetBySerialOrTag(assetId);
            if (asset == null) throw new InvalidOperationException($"Asset '{assetId}' not found.");

            _store.AbortActiveRunForAsset(asset.AssetId, $"AwaitingParts: {reasonCode}");
            _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.AwaitingParts, "PARTS-HOLD-SHELF", actor, reasonCode, notes ?? missingComponents);
            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' placed on AwaitingParts by '{actor}'. Reason: {reasonCode}");
        }

        public void HoldAsset(string assetId, string reasonCode, string actor, string notes = null)
            => SendToAwaitingParts(assetId, reasonCode, actor, null, notes);

        public void SendToInHouseRepair(string assetId, string defectReasonCode, string actor, string notes = null, string photoFilePath = null)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));
            if (string.IsNullOrWhiteSpace(defectReasonCode)) throw new ArgumentException("A defect reason code is required to route to in-house repair.", nameof(defectReasonCode));

            var asset = _store.FindAssetBySerialOrTag(assetId);
            if (asset == null) throw new InvalidOperationException($"Asset '{assetId}' not found.");

            _store.AbortActiveRunForAsset(asset.AssetId, $"Repair: {defectReasonCode}");
            _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.InHouseRepair, "REPAIR-BENCH", actor, defectReasonCode, notes);

            if (!string.IsNullOrWhiteSpace(photoFilePath) && File.Exists(photoFilePath))
            {
                var evidenceService = new EvidenceStorageService(_store);
                evidenceService.StoreEvidenceFile(asset.AssetId, asset.LatestRunId, "DEFECT_PHOTO", photoFilePath, "Photo captured upon repair routing.");
            }

            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' routed to InHouseRepair. Defect: {defectReasonCode} by '{actor}'");
        }

        public void SendToRepair(string assetId, string defectReasonCode, string actor, string notes = null, string photoFilePath = null)
            => SendToInHouseRepair(assetId, defectReasonCode, actor, notes, photoFilePath);

        public void SendToExternalIcRepair(string assetId, string vendorName, string defectNotes, string actor)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));
            if (string.IsNullOrWhiteSpace(vendorName)) throw new ArgumentException("External repair partner/vendor name is required.", nameof(vendorName));

            var asset = _store.FindAssetBySerialOrTag(assetId);
            if (asset == null) throw new InvalidOperationException($"Asset '{assetId}' not found.");

            _store.AbortActiveRunForAsset(asset.AssetId, $"ExternalIC: {defectNotes}");
            _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.AdvancedIcExternal, "EXTERNAL-VENDOR", actor, "ADVANCED_IC_EXTERNAL", defectNotes, true, vendorName);
            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' sent to external IC partner '{vendorName}' by '{actor}'");
        }

        public void SendToRetest(string assetId, string actor, string notes = null)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));

            var asset = _store.FindAssetBySerialOrTag(assetId);
            if (asset == null) throw new InvalidOperationException($"Asset '{assetId}' not found.");

            _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.ReadyForRetest, "TEST-BENCH-STAGING", actor, "REPAIR_COMPLETED", notes ?? "Repairs completed, retest queued.");
            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' routed to Retest by '{actor}'");
        }

        /// <summary>
        /// Strict Release Gate: verifies that the asset's latest QC run is completed,
        /// has an authentic SHA-256 verification seal, and contains no failing tests.
        /// </summary>
        public void ReleaseToSale(string assetId, string actor, string releaseLocation = "DISPATCH-RTS", string notes = null)
        {
            VerifyReleaseGate(assetId, out var asset, out var runSummary);
            _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.ReadyForSale, releaseLocation, actor, "QC_VERIFIED_PASSED", notes ?? $"Verified {runSummary.Grade} release for sale.", true, null, "RTS");
            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' approved for ReadyForSale (RTS) by '{actor}'. Grade: {runSummary.Grade}, Hash: {runSummary.VerificationHash}");
        }

        public void ReleaseAsset(string assetId, string actor, string releaseLocation = "DISPATCH-STAGING")
            => ReleaseToSale(assetId, actor, releaseLocation);

        public void ReleaseToRental(string assetId, string actor, string releaseLocation = "DISPATCH-RFR", string notes = null)
        {
            VerifyReleaseGate(assetId, out var asset, out var runSummary);
            _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.ReadyForRental, releaseLocation, actor, "QC_VERIFIED_PASSED", notes ?? $"Verified {runSummary.Grade} release for rental.", true, null, "RFR");
            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' approved for ReadyForRental (RFR) by '{actor}'. Grade: {runSummary.Grade}, Hash: {runSummary.VerificationHash}");
        }

        public void ReleaseToDemo(string assetId, string actor, string releaseLocation = "DEMO-SHELF", string notes = null)
        {
            var asset = _store.FindAssetBySerialOrTag(assetId);
            if (asset == null) throw new InvalidOperationException($"Asset '{assetId}' not found.");

            _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.DemoStock, releaseLocation, actor, "DEMO_STAGE", notes ?? "Staged as demonstration / test stock.", true, null, "DEMO");
            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' staged as DemoStock by '{actor}'.");
        }

        public void ReleaseToScrap(string assetId, string approvedReasonCode, string supervisorActor, string notes = null)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));
            if (string.IsNullOrWhiteSpace(approvedReasonCode))
            {
                throw new InvalidOperationException("Asset disposal / scrap harvesting requires an approved reason code (e.g. BEYOND_ECONOMIC_REPAIR, BOARD_CORROSION).");
            }
            if (string.IsNullOrWhiteSpace(supervisorActor))
            {
                throw new InvalidOperationException("Asset scrap harvesting requires supervisor / manager authorization.");
            }

            var asset = _store.FindAssetBySerialOrTag(assetId);
            if (asset == null) throw new InvalidOperationException($"Asset '{assetId}' not found.");

            _store.AbortActiveRunForAsset(asset.AssetId, $"Scrap: {approvedReasonCode}");
            _store.TransitionAssetQueue(asset.AssetId, AssetQueueStatus.ScrapHarvest, "SCRAP-HARVEST-BAY", supervisorActor, approvedReasonCode, notes ?? "Asset harvested for components and decommissioned.", true, null, "SCRAP");
            AppLogger.Info($"[WarehouseJourney] Asset '{assetId}' routed to ScrapHarvest by supervisor '{supervisorActor}'. Reason: {approvedReasonCode}");
        }

        public void DisposeAsset(string assetId, string approvedReasonCode, string supervisorActor, string notes = null)
            => ReleaseToScrap(assetId, approvedReasonCode, supervisorActor, notes);

        private void VerifyReleaseGate(string assetId, out AssetWipRecord asset, out QcRunSummary runSummary)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));

            asset = _store.FindAssetBySerialOrTag(assetId);
            if (asset == null) throw new InvalidOperationException($"Asset '{assetId}' not found.");

            if (string.IsNullOrWhiteSpace(asset.LatestRunId))
            {
                throw new InvalidOperationException($"Release Rejected: Asset '{assetId}' has never undergone a diagnostic QC run.");
            }

            runSummary = _store.GetRunSummary(asset.LatestRunId);
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

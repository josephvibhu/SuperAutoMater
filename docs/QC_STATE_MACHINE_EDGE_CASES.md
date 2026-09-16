# QC State Machine Edge Cases & Resilience Architecture

This document provides a comprehensive analysis of the state machine, failure modes, and recovery behaviors for SuperAutoMater's Trusted QC Core.

---

## 1. Interrupted Run Recovery

### Scenario
A technician is midway through a 10-point diagnostic run (e.g. Display, Audio, and Keyboard passed; CPU stress test running) when the application crashes, the device loses power, or Windows updates trigger an unexpected reboot.

### Potential Failure Modes
1. **False Completion / Ghost Run:** On reboot, the application might start a new run with an empty state, leaving the database with orphaned records, or worse, generating a certificate assuming prior tests passed.
2. **Premature Certification:** An incomplete run might be queried by an external fleet agent or technician and certified without testing remaining hardware.
3. **Data Overwrite:** Restarting tests might overwrite previously collected SMART radar metrics or battery voltage sag telemetry.

### Architectural Solution
- **State Persistence in SQLite:** The run state is persistently stamped in SQLite as `InProgress` on initialization.
- **Boot Detection:** When `QcRunOrchestrator.InitializeRun` is called upon relaunch, it queries `qc_runs` for an existing `InProgress` run matching the device's Authoritative Serial or Fallback UUID.
- **Resume Protocol:** If an in-progress run is detected, the orchestrator attaches to `existingRunId` without wiping test results already persisted in `qc_test_results`.
- **Hard Certification Gate:** `QcRunStore.TryCompleteRun` executes an atomic SQL constraint check:
  ```sql
  SELECT COUNT(*) FROM qc_test_results
  WHERE qc_run_id = $runId AND status IN ('NotStarted', 'Running', 'Failed', 'Skipped');
  ```
  If this count > 0, `TryCompleteRun` returns `false` and rolls back the transaction. An interrupted run cannot produce a valid certificate under any circumstances.

---

## 2. Missing & Duplicate Generic Serial Numbers

### Scenario
Many refurbished laptops, white-label desktop motherboards, or units after BIOS flashing report generic placeholder serials such as:
- `"To be filled by O.E.M."`
- `"Default string"`
- `"System Serial Number"`
- `"None"` / `"00000000"` / `""`

### Potential Failure Modes
1. **Cross-Device Collisions:** Device B with `"To be filled by O.E.M."` connects to the bench, matches Device A's record in the database, and overwrites Device A's test results, custody history, and grading status.
2. **Sync Loop Corruption:** Cloud sheets receive conflicting records for the same "serial number", corrupting warehouse inventory.

### Architectural Solution
- **Generic Serial Blacklist:** `QcRunStore.IsGenericSerial()` checks candidate serials against known OEM placeholders.
- **Authoritative vs. Fallback Confidence:**
  - If a valid, non-generic serial is present, `AssetIdentifierConfidence.Authoritative` is assigned.
  - If generic or missing, `AssetIdentifierConfidence.Fallback` is assigned.
- **Deterministic Hardware Fingerprint:**
  - When generic, `FindOrCreateAsset` constructs a stable fallback UUID:
    `FALLBACK-SHA256(Model + AssetTag + HardwareSeed)[0..12]`
  - Lookup queries in SQLite index on `asset_uuid` rather than `serial_number`.
  - Multiple devices with identical generic BIOS strings maintain distinct, non-overlapping records in SQLite.

---

## 3. Non-Touch Hardware & Subsystem Applicability

### Scenario
A technician runs SuperAutoMater on a traditional clamshell workstation laptop (e.g. ThinkPad T14 or Latitude 5420) without a capacitive touchscreen or digitizer.

### Potential Failure Modes
1. **Blocked Certification:** If Touchscreen is a mandatory test, the technician is blocked from issuing a certificate unless they click "Skip" or fake a pass.
2. **False Claims on Certificate:** The generated customer report states `[PASS] Touchscreen & Digitizer`, which is a false declaration of hardware capability.

### Architectural Solution
- **Hardware Applicability Probe:** At startup, `HardwareDiagnosticsService` probes `GetSystemMetrics(SM_DIGITIZER)` and Windows PnP digitizer descriptors.
- **Dynamic Policy Classification:**
  - If hardware is detected: `Touchscreen` test is flagged `IsApplicable = true`.
  - If hardware is absent: `Touchscreen` test is flagged `IsApplicable = false`, and `QcRunOrchestrator` records status `NotApplicable` with reason `"Non-touch display hardware"`.
- **Policy Compliance:** `QcPolicy` permits `NotApplicable` status for conditional tests, satisfying the completion gate.
- **Truthful Certificate Copy:** The certificate renders `[N/A - Non-Applicable] Touchscreen / Digitizer (Non-touch display hardware)` with distinct amber styling, never claiming the device was touch-tested.

---

## 4. Failed Repair & Retest Loop

### Scenario
A unit fails the RAM memory stress test (bit flips detected in memory bank 1). The technician swaps the defective SO-DIMM module and re-tests the device.

### Potential Failure Modes
1. **History Eradication:** Overwriting the failed run hides the defective module incident from reliability and MTBF tracking.
2. **Outbox Inconsistency:** The cloud sync already dispatched the `Failed` event; updating it in-place breaks event sourcing.

### Architectural Solution
- **Immutable Runs:** Each diagnostic attempt is a discrete `qc_runs` aggregate with its own UUID (`RunId`).
- **Asset Association:** Multiple runs are linked to the same `asset_id` in SQLite, creating a chronological audit trail (`qc_runs.started_at_utc DESC`).
- **Retest Protocol:** When a device returns from repair, a fresh run is initialized (`forceFreshRun: true`). The previous run remains sealed with `Failed` or `Aborted` status, providing full traceability for warehouse leads and warranty audits.

---

## 5. Supervisor vs. Technician Override Permissions

### Scenario
A device has a battery reporting 78% health. The warehouse policy requires >= 80% for Grade A. The customer agreed to accept 78% battery with a \$20 discount. A junior technician attempts to override the battery failure unilaterally.

### Potential Failure Modes
1. **Unauthorized Quality Downgrades:** Technicians bypassing core safety or wear standards without managerial approval.
2. **Lack of Attribution:** Overrides recorded anonymously without recording who authorized the deviation.

### Architectural Solution
- **Tiered Override Classification in `qc_policy.json`:**
  - Standard Overrides: Cosmetic blemishes, station acoustic interference (technician justification required).
  - Critical Overrides: `Cpu`, `Battery`, `Storage` (supervisor sign-off and PIN required).
- **Enforcement at Orchestrator Level:**
  ```csharp
  if (_policy.RequiresSupervisorApproval(testKey) && string.IsNullOrWhiteSpace(approver))
  {
      error = $"Policy requires supervisor sign-off to override test '{testKey}'.";
      return false;
  }
  ```
- **Permanent Override Record:** The `overrides` table captures `(run_id, test_key, actor, reason, approver, recorded_at_utc)`. This information is permanently incorporated into the certificate and SHA-256 verification hash.

---

## 6. Offline Queue Accumulation & Idempotency

### Scenario
A mobile refurbishing team operates in a warehouse with intermittent Wi-Fi. 50 devices are tested and certified entirely offline. When Wi-Fi reconnects, the sync worker attempts to flush all 50 records.

### Potential Failure Modes
1. **Data Loss on Network Timeout:** Discarding records upon first HTTP failure.
2. **Duplicate Records:** Retrying a batch causes Google Sheets or the central ERP to receive 3 duplicate rows for the same run.
3. **Queue Head-of-Line Blocking:** A single malformed record prevents all subsequent valid records from syncing.

### Architectural Solution
- **Persistent SQLite Outbox:** Events are written into `sync_outbox` within the same SQLite transaction as the diagnostic run (`ACID guarantee`).
- **Immutable Idempotency Keys:** Every outbox record includes a cryptographically unique `idempotency_key` (e.g. `runId`, `legacy-queue-hash`).
- **Independent Event Processing:** `SyncOutboxDispatcher` flushes events with individual try/catch blocks. If event 3 fails, events 4..50 continue syncing.
- **Idempotent Webhook Receiver:** The receiving server deduplicates incoming events based on `idempotency_key`, preventing duplicate inventory rows.

---

## 7. Rejected Sync Handling (4xx Permanent vs. 5xx Transient)

### Scenario
A cloud endpoint rejects a sync payload due to an invalid token, schema change, or upstream server outage.

### Potential Failure Modes
1. **Infinite Rapid Retry Loop:** Pegging the CPU and network bandwidth attempting to resend an unparseable payload.
2. **Silent Failure:** Swallowing the error and leaving the operator believing records synced.

### Architectural Solution
- **Categorized Error Responses:**
  - HTTP 4xx (Client Error / Unauthorized / Bad Request): Marked as permanent failure. Increments `attempts`, records `last_error` in SQLite, logs `AppLogger.Warn`, and does NOT immediately retry.
  - HTTP 5xx / Network Timeout: Marked as transient. Increments `attempts`, applies exponential backoff, and retries on next sync cycle.
- **Operator Visibility:** The UI status badge reflects pending outbox count (`SyncOutboxDispatcher.PendingCount`), and structured logs detail exact HTTP error codes without leaking authorization tokens.

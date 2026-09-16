# SuperAutoMater Trusted QC Core — QA Test Cases (BDD Format)

This document specifies the Behavioral-Driven Development (Given / When / Then) test cases validating the Trusted QC Core, grading algorithms, and manual override audit gates.

---

### Scenario 1: Clean Baseline Run with Grade A Qualification
- **Given** a physical device with Battery Health at 92% and Storage SMART Health at 100%
- **And** all 10 mandatory diagnostic tests (`Display`, `Audio`, `Camera`, `Keyboard`, `Cpu`, `Battery`, `Gpu`, `Storage`, `Usb`, `Bluetooth`) complete with status `Passed`
- **When** the technician attempts to complete the QC run and generate the PDF certificate
- **Then** `QcRunOrchestrator.TryCompleteRun` succeeds with status `Completed`
- **And** the policy engine assigns verified `GRADE A`
- **And** an immutable SHA-256 verification hash is generated and stamped onto the PDF certificate
- **And** a `QcRunCompleted` event is enqueued into the SQLite `sync_outbox`.

---

### Scenario 2: Grade Degradation on Marginal Battery Health
- **Given** a physical device with Storage SMART Health at 98%
- **And** Battery Health is measured at 68% (below the Grade A threshold of 80%, but above the Grade B threshold of 60%)
- **And** all 10 mandatory diagnostic tests pass
- **When** the QC run is evaluated for completion
- **Then** the policy engine assigns verified `GRADE B`
- **And** the certificate reflects `GRADE B` along with the exact measured battery capacity
- **And** the system never fabricates an unearned `GRADE A+` badge.

---

### Scenario 3: Rejection of Premature Certificate Issuance on Incomplete Run
- **Given** a newly initiated QC run on a bench station
- **And** only 3 tests (`Display`, `Audio`, `Keyboard`) have been executed and passed
- **And** remaining mandatory tests (`Cpu`, `Battery`, `Storage`, etc.) are in status `NotStarted`
- **When** the technician clicks "1-Click PDF Certificate" or the fleet manager requests `/certificate`
- **Then** `QcRunOrchestrator.TryCompleteRun` rejects completion with reason `"Mandatory test 'Camera' has not been executed."`
- **And** `PdfCertificateService.GenerateCertificate` throws `InvalidOperationException`
- **And** the operator is alerted with an actionable checklist of incomplete tests.

---

### Scenario 4: Authorized Technician Manual Override on Environmental Block
- **Given** an active QC run where the ambient noise of the refurbishment facility causes the automated `Audio` microphone test to fail
- **When** the technician selects "Record Manual Override..." from the test context menu
- **And** selects justification category `"Ambient environment prevented acoustic/optical sweep"`
- **And** submits their technician ID (`TECH-04`)
- **Then** the override is accepted and stored in the SQLite `overrides` table
- **And** the test item status in the pipeline transitions to `OVERRIDDEN` (`StatusBadge = "⚠"`)
- **And** the run completes successfully
- **And** the PDF certificate explicitly marks `[OVERRIDE - Audited] AUDIO / STEREO SWEEP (Ambient environment...)`.

---

### Scenario 5: Policy Rejection of Critical Override Without Supervisor PIN
- **Given** an active QC run where the `Cpu` AVX stress test failed due to thermal throttling
- **And** the active `qc_policy.json` specifies `Cpu` in `SupervisorRequiredOverrides`
- **When** a technician attempts to override the `Cpu` test failure without providing a supervisor name and PIN
- **Then** `QcRunOrchestrator.RecordTestOverride` returns `false` with error `"Policy requires supervisor sign-off to override test 'Cpu'."`
- **And** the test status remains `FAILED` (`StatusBadge = "✗"`)
- **And** completion of the QC run remains strictly blocked until supervisor credentials are authenticated.

---

### Scenario 6: Power Interruption Mid-Run and Cold Boot Recovery
- **Given** an active QC run (`RunId: RUN-ALPHA`) where 4 tests have completed and recorded in SQLite
- **When** the device suffers an unexpected shutdown or process termination mid-test
- **And** the application is relaunched on the same physical device
- **Then** `QcRunOrchestrator.InitializeRun` locates the active `InProgress` run in `qc_runs`
- **And** attaches to `RUN-ALPHA` without overwriting previously completed tests
- **And** the UI pipeline restores `StatusBadge = "✓"` for the 4 previously passed tests
- **And** certificate generation remains disabled until the remaining tests are executed.

---

### Scenario 7: Fallback Identifier Generation for Generic OEM Serials
- **Given** two distinct physical refurbished laptops
- **And** both devices report BIOS SerialNumber as `"To be filled by O.E.M."`
- **When** SuperAutoMater starts and initializes a QC run on each machine
- **Then** `QcRunStore.IsGenericSerial` detects the placeholder string
- **And** the system assigns `Confidence = Fallback` and `Source = HardwareFingerprintFallback`
- **And** generates distinct fallback UUIDs based on hardware seeds (`FALLBACK-XXXXXX`)
- **And** Device 2's diagnostic run is stored in a separate asset row without overwriting Device 1's records.

---

### Scenario 8: Offline Outbox Accumulation and Legacy Queue Migration
- **Given** legacy queue file `offline_sync_queue.json` containing 3 un-dispatched inventory records
- **And** legacy ledger `superautomater_ledger.jsonl` containing 2 un-synced audit records
- **When** `SyncOutboxDispatcher` initializes on startup
- **Then** all 5 records are imported into SQLite `sync_outbox`
- **And** each record is assigned an immutable, deterministic `idempotency_key`
- **And** legacy files are renamed to `.migrated`
- **And** restarting the application a second time does not insert duplicate rows into `sync_outbox`.

---

### Scenario 9: Tamper Detection via Cryptographic Verification Hash Mismatch
- **Given** a successfully completed QC run with verified `GRADE A` and verification hash `HASH-1234`
- **When** an unauthorized actor directly edits the SQLite database file and changes a test metric or modifies `grade` from `"GRADE B"` to `"GRADE A"`
- **And** an application or audit service loads the record and calls `QcRunOrchestrator.VerifyRunTamper`
- **Then** the recalculated SHA-256 hash does not match `HASH-1234`
- **And** `VerifyRunTamper` returns `false`
- **And** certificate generation is refused with error `"Verification hash mismatch! The recorded run data has been modified."`

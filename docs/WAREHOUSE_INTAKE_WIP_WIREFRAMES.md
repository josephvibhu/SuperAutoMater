# Warehouse Intake & WIP Board UI Wireframes (Phase 3)

## 1. Ergonomic Principles for Refurbishing & Warehouse Floors

Technicians and intake operators in high-throughput ITAD/refurbishing facilities operate under severe ergonomic constraints:
- **PPE & Gloves:** Thick nitrile or cut-resistant gloves reduce touch precision and make tiny UI targets frustrating.
- **Physical Workflow Velocity:** Intake operators handle 40–120 units/hour. Reaching for a mouse or trackpad adds 4–8 seconds per device (~16% throughput penalty).
- **Acoustic Environment:** Warehouse conveyor noise demands clear, multi-frequency acoustic audio feedback for scan accept/reject alongside visual badges.
- **Lighting Variability:** Harsh overhead fluorescent lighting requires high-contrast UI (contrast ratio > 7:1) with dark mode and large typography.

---

## 2. Screen Wireframe: Barcode-First Intake & Triage Screen

### 2.1 Visual ASCII Wireframe

```text
+--------------------------------------------------------------------------------------------------------------------+
|  SUPERMANAGER -- WAREHOUSE INTAKE STATION [BAY 04]                     OPERATOR: J. DOE  |  BATCH: BATCH-20260916  |
+--------------------------------------------------------------------------------------------------------------------+
|                                                                                                                    |
|  [F1] BATCH SETUP     [F2] QUICK INTAKE (ACTIVE)     [F3] BULK SCAN MODE     [F4] ROUTING LABELS     [ESC] CLEAR   |
|                                                                                                                    |
|  +--------------------------------------------------------------------------------------------------------------+  |
|  |  SCAN ASSET IDENTIFIER (AUTO-FOCUSED)                                                                        |  |
|  |  +--------------------------------------------------------------------------------------------------------+  |  |
|  |  |  >> SCAN HERE: [ DL5300-8849202                  ]  [ENTER: COMMIT]                                   |  |  |
|  |  +--------------------------------------------------------------------------------------------------------+  |  |
|  |     AUTO-DETECTED: Serial Number (Confidence: Authoritative 99.8% via Win32_BIOS)                           |  |
|  +--------------------------------------------------------------------------------------------------------------+  |
|                                                                                                                    |
|  +----------------------------------------------------+  +-----------------------------------------------------+  |
|  |  UNIT INTAKE ATTRIBUTES                            |  |  CHARGER & ACCESSORY CONFIRMATION                   |  |
|  |----------------------------------------------------|  |-----------------------------------------------------|  |
|  |  1. Asset Tag:        [ AT-994021       ] (ALT+T)  |  |  ( ) [1] OEM Charger Included & Verified            |  |
|  |  2. Hardware Model:   [ Dell Latitude 5420 ]       |  |  ( ) [2] Third-Party Power Adapter                  |  |
|  |  3. Source Stream:    [ Corporate Lease  v] (ALT+S)|  |  (*) [3] Missing / No Charger (Bench Test Only)     |  |
|  |  4. Target Shelf/Loc: [ SHELF-A-04-02    ] (ALT+L) |  |  ( ) [4] USB-C PD Powered On Bench                  |  |
|  |  5. Test Profile:     [ Enterprise High   v] (ALT+P)|  |-----------------------------------------------------|  |
|  |  6. Operator Notes:   [ Scratched top lid] (ALT+N) |  |  THERMAL LABEL PREVIEW                              |  |
|  +----------------------------------------------------+  |  +-----------------------------------------------+  |  |
|                                                          |  | [QR]  TAG: AT-994021 | MDL: Dell Latitude 5420  |  |  |
|                                                          |  |       LOC: SHELF-A04 | BATCH: BATCH-20260916    |  |  |
|                                                          |  |       [★ QUEUE: ReadyForTest · LOC: A-04 ★]   |  |  |
|                                                          |  +-----------------------------------------------+  |  |
|                                                          |  [SPACE / CTRL+P] PRINT & ADVANCE TO NEXT UNIT       |  |
|                                                          +-----------------------------------------------------+  |
|                                                                                                                    |
|  RECENT INTAKE LOG (TODAY: 142 UNITS · AVG CYCLE: 11.2s)                                                           |
|  +-------------+---------------+-----------------------+-------------------+----------------+-------------------+  |
|  | TIME (UTC)  | ASSET TAG     | SERIAL NUMBER         | LOCATION          | CHARGER        | INITIAL QUEUE     |  |
|  |-------------+---------------+-----------------------+-------------------+----------------+-------------------|  |
|  | 09:44:12    | AT-994020     | 8849201               | SHELF-A-04-01     | OEM Charger    | ReadyForTest      |  |
|  | 09:43:58    | AT-994019     | 8849200               | SHELF-A-04-01     | Missing        | ReadyForTest      |  |
|  +-------------+---------------+-----------------------+-------------------+----------------+-------------------+  |
+--------------------------------------------------------------------------------------------------------------------+
```

### 2.2 Gloved-Operator Keyboard-Only Shortcut Contract
- `RETURN` / Barcode Scanner Carriage Return: Commits current scan into the authoritative field.
- `SPACEBAR` or `CTRL+P`: Triggers immediate thermal label generation and prints the 3"x2" routing sticker.
- `1` / `2` / `3` / `4`: Quick numeric single-key toggles for charger confirmation status.
- `ESC`: Resets input buffer and refocuses the scan box without saving.
- `F1`–`F4`: Mode switching (Batch config, Intake, Bulk Audit, Thermal Setup).

---

## 3. Screen Wireframe: 7-Lane Kanban WIP Board

### 3.1 Visual ASCII Wireframe

```text
+--------------------------------------------------------------------------------------------------------------------+
|  SUPERMANAGER -- WAREHOUSE WIP BOARD (CANONICAL SQLITE PERSISTENCE)                 TOTAL WIP UNITS: 318           |
+--------------------------------------------------------------------------------------------------------------------+
|  SEARCH: [ AT-994021             ] [FILTER: ALL MODELS v]  AUTO-REFRESH: [ON 2.5s]  [RELOAD NOW]                   |
+--------------------------------------------------------------------------------------------------------------------+
|  READY FOR TEST   |  IN TEST          |  HOLD             |  REPAIR           |  RETEST           |  READY RELEASE    |
|  [ 84 UNITS ]     |  [ 16 UNITS ]     |  [ 9 UNITS ]      |  [ 23 UNITS ]     |  [ 12 UNITS ]     |  [ 174 UNITS ]    |
|-------------------+-------------------+-------------------+-------------------+-------------------+-------------------|
| +---------------+ | +---------------+ | +---------------+ | +---------------+ | +---------------+ | +---------------+ |
| | AT-994021     | | | BENCH-04      | | | AT-993810     | | | AT-993701     | | | AT-993690     | | | AT-993512     | |
| | Dell 5420     | | | AT-993950     | | | Dell 7490     | | | ThinkPad T14  | | | HP EliteBook  | | | Dell 5420     | |
| | Loc: SHELF-A04| | | Progress: 8/10| | | REASON: BIOS_ | | | DEFECT: PIXEL | | | Retest #1     | | | GRADE A       | |
| | Batch: 202609 | | | Batt: 94%     | | | LOCKED        | | | PROOF_FAIL    | | | Prior: Batt   | | | SEAL: 4F9B2C..| |
| | [START BENCH] | | | [VIEW HUD]    | | | By: Supervisor| | | Tech: R. Patel| | | Loc: RETEST-01| | | [DISPATCH]    | |
| +---------------+ | +---------------+ | +---------------+ | +---------------+ | +---------------+ | +---------------+ |
| +---------------+ | +---------------+ |                   | +---------------+ |                   | +---------------+ |
| | AT-994018     | | | BENCH-07      | |                   | | AT-993705     | |                   | | AT-993511     | |
| | HP 840 G8     | | | AT-993948     | |                   | | MacBook Pro   | |                   | | HP 840 G8     | |
| | Loc: SHELF-A02| | | Progress: 3/10| |                   | | DEFECT: KEYBRD| |                   | | GRADE B+      | |
| | [START BENCH] | | | [VIEW HUD]    | |                   | | Loc: BENCH-R02| |                   | | SEAL: 991E0A..| |
| +---------------+ | +---------------+ |                   | +---------------+ |                   | +---------------+ |
+--------------------------------------------------------------------------------------------------------------------+
|  SELECTED ASSET TIMELINE: AT-993701 (ThinkPad T14)                                                                 |
|  09:12 UTC: Received -> 09:18 UTC: Test Failed (Pixel Proof) -> 09:25 UTC: Moved to REPAIR (Defect: PANEL_REPLACE)|
|  ATTACHED EVIDENCE: [1 Photo - SHA256: D7A8F31...] | ACTIONS: [MARK REPAIRED (RETEST)]  [DISPOSE / E-WASTE]       |
+--------------------------------------------------------------------------------------------------------------------+
```

### 3.2 Lane State Constraints
1. **ReadyForTest:** Device intaked, location assigned, waiting for diagnostic bench assignment.
2. **InTest:** Diagnostics actively executing on bench; synchronized with bench HUD heartbeat.
3. **Hold:** Locked due to compliance issues (e.g. Computrace active, BitLocker locked, missing authorization).
4. **Repair:** Bench failure detected. Requires defect reason code and optional photo before moving.
5. **Retest:** Post-repair validation stage. Must pass entire QC profile from scratch.
6. **ReadyForRelease:** Gated strictly by `QcRunStatus.Completed` with authentic cryptographic SHA-256 seal.
7. **Disposed:** Final e-waste or parts salvage queue; requires supervisor sign-off.

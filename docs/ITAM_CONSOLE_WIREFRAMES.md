# ITAM Console & Return Report Wireframes

## Design Philosophy: Manager Decision Over Data Density
The Central Console and Return Reports are intentionally designed for rapid decision-making:
1. **Prominent Verdict First:** Is the unit ready for redeployment, or does it have an unresolved discrepancy?
2. **Neutral Discrepancy Language:** Uses `Difference Detected` and `Approved Replacement` instead of accusatory terminology.
3. **Audit Immutability:** Clear provenance (Source, Time, Actor, Bench ID, Cryptographic Hash).

---

## 1. Central Console: Asset Detail & Transition View

```
+-----------------------------------------------------------------------------------------------+
| SUPERMANAGER ITAM CONSOLE  [ Depot: SFO-HUB-01 ]              [ User: alex.mgr (Supervisor) ] |
+-----------------------------------------------------------------------------------------------+
| <- Back to Fleet Queue                                                                        |
|                                                                                               |
| ASSET: ThinkPad T14s Gen 3     SERIAL: PF-3X992A      TAG: AST-2024-8841      STATUS: READY   |
+-----------------------------------------------------------------------------------------------+
| LIFECYCLE TIMELINE (INGESTION TRAIL)                                                          |
|                                                                                               |
|  [2026-09-14 09:12 UTC]  INTAKE               Station: DOCK-A        Actor: j.intake          |
|  [2026-09-14 10:45 UTC]  QC_TEST_PASS         Bench:   BENCH-02      Actor: r.technician      |
|  [2026-09-15 14:20 UTC]  LOCATION_TRANSFER    Shelf:   RESTAGE-B4    Actor: m.warehouse       |
|  [2026-09-16 08:30 UTC]  RETURN_AUDIT         Bench:   BENCH-05      Actor: a.supervisor      |
+-----------------------------------------------------------------------------------------------+
| COMPONENT OBSERVATION & RECONCILIATION                                                        |
|                                                                                               |
| [!] 1 DIFFERENCE DETECTED (0 UNAPPROVED)                                                      |
|                                                                                               |
| +-----------------+---------------------+---------------------+------------------+----------+ |
| | Component       | Baseline (Intake)   | Observed (QC/Audit) | Status           | Action   | |
| +-----------------+---------------------+---------------------+------------------+----------+ |
| | Motherboard     | UUID: 4C4C4544-3X99 | UUID: 4C4C4544-3X99 | MATCH (High)     | [OK]     | |
| | Memory (RAM)    | 16 GB DDR5 4800MHz  | 16 GB DDR5 4800MHz  | MATCH (High)     | [OK]     | |
| | Storage (NVMe)  | SN: WDC-SN750-10928 | SN: CT1000P3-882190 | DIFFERENCE       | [RESOLV] | |
| |                 | (512 GB)            | (1000 GB NVMe)      | Approved Repl.   | View Log | |
| | Battery         | Health: 94% (OEM)   | Health: 93% (OEM)   | EXPECTED (Norm)  | [OK]     | |
| +-----------------+---------------------+---------------------+------------------+----------+ |
|                                                                                               |
| RECONCILIATION AUDIT NOTE:                                                                    |
| "Storage upgrade to 1TB Crucial P3 completed under Work Order #WO-8910 by tech.j on 09/15."   |
| Approved By: alex.mgr (Supervisor) at 2026-09-15 11:04 UTC                                    |
+-----------------------------------------------------------------------------------------------+
| ACTIONS:                                                                                      |
| [ Generate Return Report (HTML/JSON) ]   [ Copy Timeline URL ]   [ Export ITAM Manifest CSV ] |
+-----------------------------------------------------------------------------------------------+
```

---

## 2. Customer Return / Redeployment Report Wireframe

```
+-----------------------------------------------------------------------------------------------+
|                                SUPERAUTOMATER DEPOT OS                                        |
|                          ASSET VERIFICATION & RETURN REPORT                                   |
+-----------------------------------------------------------------------------------------------+
| Report ID:   rpt-9a8f21c0b34e4a77                     Generated: 2026-09-16 09:30:15 UTC      |
| Depot Site:  SFO-MAIN-DEPOT                           Client:    Acme Cloud Systems           |
+-----------------------------------------------------------------------------------------------+
| ASSET IDENTIFICATION                                                                          |
| Make & Model:     Lenovo ThinkPad T14s Gen 3          Asset Tag:    AST-2024-8841             |
| Serial Number:    PF-3X992A                           System UUID:  4C4C4544-3X99-1052-8041   |
| Final Disposition: Certified for Redeployment (Grade A)                                       |
+-----------------------------------------------------------------------------------------------+
| PHYSICAL TRANSITION TIMELINE                                                                  |
| Time (UTC)           Transition Event       Location           Actor          Result          |
| --------------------------------------------------------------------------------------------- |
| 2026-09-14 09:12:00  Intake Scan            Dock A             j.intake       Logged          |
| 2026-09-14 10:45:12  Diagnostic QC Pass     Bench 02           r.tech         Pass (10/10)    |
| 2026-09-15 11:00:00  Approved Hardware Swap Repair Bench       r.tech         Storage Upgrade |
| 2026-09-16 08:30:45  Final Release Audit    Bench 05           a.super        Verified        |
+-----------------------------------------------------------------------------------------------+
| COMPONENT INTEGRITY BASELINE                                                                  |
| Subsystem       Observed Specification                Source         Confidence  Integrity    |
| --------------------------------------------------------------------------------------------- |
| System Board    Lenovo 21BR0014US / UUID: 4C4C4544    DmiDecode      High        Original     |
| Processor       Intel Core i7-1260P (12 Cores)        Win32_CPU      High        Original     |
| Memory          16 GB LPDDR5-4800 Soldered            WmiMemory      High        Original     |
| Storage         1 TB Crucial P3 NVMe (SN: CT1000...)  SmartReport    High        Approved Rep |
| Battery         57 Wh Li-Ion (Health: 93%, 41 cycles) AcpiBattery    Medium      Original     |
| Display Panel   14.0" 1920x1200 IPS Non-Touch         EdidParser     High        Original     |
+-----------------------------------------------------------------------------------------------+
| DISCREPANCY & REPLACEMENT DECLARATIONS                                                        |
| [Approved Replacement] NVMe SSD swapped from 512GB WD to 1TB Crucial per WO-8910.              |
| Verified & Authorized by Supervisor alex.mgr on 2026-09-15 11:04 UTC.                         |
| No unapproved discrepancies detected.                                                         |
+-----------------------------------------------------------------------------------------------+
| CRYPTOGRAPHIC INTEGRITY VERIFICATION                                                          |
| Signature Scheme: HMAC-SHA256                                                                 |
| Verification Key: tenant-site-auth-v1                                                         |
| Digest: e9b42cf8910e588147d3c0e1762193b2a8d3434685ff87e148e6587c679a9412                     |
|                                                                                               |
| * This report is generated from immutable physical diagnostic observations recorded at bench. |
+-----------------------------------------------------------------------------------------------+
```

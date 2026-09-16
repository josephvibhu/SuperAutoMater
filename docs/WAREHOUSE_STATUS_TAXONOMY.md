# Warehouse Status Taxonomy & Information Hierarchy (Phase 3)

## 1. Controlled Status Taxonomy

To eliminate ambiguity across bench technicians, floor supervisors, and shipping coordinators, SuperAutoMater establishes a 7-stage normalized lifecycle queue taxonomy.

| Queue State | Color Code | Hex | Floor Description | Allowable Transitions |
| :--- | :--- | :--- | :--- | :--- |
| **ReadyForTest** | Neutral Slate | `#8B949E` | Intaked, labeled, location assigned. Awaiting bench placement. | `InTest`, `Hold`, `Disposed` |
| **InTest** | Electric Cyan | `#58A6FF` | Mounted on diagnostic bench; automated tests executing. | `ReadyForTest` (aborted), `Repair`, `Hold`, `ReadyForRelease` |
| **Hold** | Caution Amber | `#D29922` | Quarantined due to BIOS lock, Computrace/MDM, missing customer approval. | `ReadyForTest`, `Repair`, `Disposed` |
| **Repair** | Alert Crimson | `#F85149` | Hardware/cosmetic failure confirmed. Staged for technician parts swap. | `Retest`, `Hold`, `Disposed` |
| **Retest** | Purple Violet | `#A371F7` | Post-repair re-verification. Requires clean pass across entire QC suite. | `InTest`, `Repair`, `Hold` |
| **ReadyForRelease** | Emerald Green| `#3FB950` | Passed all QC tests with cryptographic SHA-256 seal. Ready for packing. | `Disposed` (rare damage), Dispatch |
| **Disposed** | Charcoal Gray | `#30363D` | Decommissioned, sanitized, harvested for parts, or sent to certified e-waste. | *Terminal state* |

---

## 2. Information Hierarchy: Bench Screen vs. Supervisor Screen

A primary failure mode in warehouse software is **information overload** on bench displays and **information starvation** on supervisor dashboards. We partition the hierarchy into two targeted views:

### 2.1 Bench Technician Screen (Action-Oriented, Real-Time)

The bench screen is designed for operators focused on 1 to 4 physical laptops right in front of them:
- **Primary Focus (Instant Glancability):**
  1. **Big Status Pill:** Large color badge (`TESTING`, `PASSED`, `FAILED`).
  2. **Active Step Progress:** Progress ring/bar (e.g. `Step 7 of 10: NVMe Smart Integrity`).
  3. **Actionable Call-to-Action:** Clear physical instruction (e.g. "Unplug AC Adapter to verify battery discharge rate", "Press all highlighted keyboard keys").
- **Secondary Focus (Diagnostic Telemetry):**
  - CPU Temperature, Battery Wear %, SSD Health % (HD Sentinel).
- **Suppressed / Hidden on Bench:**
  - Fleet averages, financial trade-in values, pallet routing algorithms, company-wide KPIs.

### 2.2 Supervisor / Floor Manager Cockpit (Exception-Oriented, Strategic)

The supervisor screen in SuperManager focuses on bottlenecks, yields, and compliance:
- **Primary Focus (Exceptions & Throughput):**
  1. **Bottleneck Counts:** Number of units trapped in `Repair` (> 24h) or `Hold` (> 48h).
  2. **First-Time Pass Rate (FPY):** Real-time percentage of devices passing without repair.
  3. **WIP Dwell Time:** Aging distribution across the 7 lanes.
  4. **Active Bench Grid:** Live telemetry across all benches on the subnet, alerting on thermal runaway (> 90°C) or dead stalls.
- **Drill-Down Capability:**
  - Clicking any card reveals the full immutable timeline: intake scan, technician, test results, override approver, and photo evidence.
- **Supervisor-Only Actions:**
  - Overriding high-severity test failures with signed reason code.
  - Authorizing terminal `Disposed` transitions.

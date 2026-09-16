# Depot OS Operational KPI Definitions & Mathematical Specifications

This document defines the canonical operational metrics and mathematical formulations for **SuperAutoMater** and **SuperManager** Depot OS. These definitions eliminate ambiguity and prevent dashboards from misleading operators, supervisors, or executive management.

---

## 1. First-Time Pass Rate (FTPR)

### 1.1 Objective
Measure the intrinsic defect-free quality of inbound batches before any depot rework, part swaps, or retests take place.

### 1.2 Mathematical Formulation
Let:
- $\mathcal{A}$ be the set of all unique physical assets registered in the depot database.
- $\text{Runs}(a) = [r_{a,1}, r_{a,2}, \dots, r_{a,k}]$ be the chronological list of all QC runs performed on asset $a \in \mathcal{A}$.
- $r_{a,1}$ denotes the **initial bench diagnostic run** (Run #1) for asset $a$.
- $\text{Status}(r) \in \{\text{InProgress}, \text{Passed}, \text{Failed}, \text{Aborted}\}$.
- $\text{Completed}(r) \iff \text{Status}(r) \in \{\text{Passed}, \text{Failed}\}$.

The **First-Time Pass Rate (FTPR)** is strictly defined as:

$$\text{FTPR} = \frac{|\{a \in \mathcal{A} \mid \text{Completed}(r_{a,1}) \land \text{Status}(r_{a,1}) = \text{Passed}\}|}{|\{a \in \mathcal{A} \mid \text{Completed}(r_{a,1})\}|} \times 100\%$$

```text
       Count of Unique Assets whose Run #1 Status == 'Passed'
FTPR = ------------------------------------------------------ * 100%
       Count of Unique Assets with at least one Completed Run
```

### 1.3 Strict Rules & Anti-Gaming Invariants
1. **Retest Inflation Prohibition**: Runs where $k > 1$ (Retests #2, #3, etc.) **must NEVER** be included in either the numerator or denominator of FTPR. A device that failed Run #1, was repaired, and then passed Run #2 remains a Run #1 failure.
2. **Handling of `NotApplicable` Tests**: If a hardware component is physically absent (e.g. No Secondary Battery, No Fingerprint Sensor), the test result is `NotApplicable`. An asset where all applicable tests pass and optional tests are `NotApplicable` is evaluated as **`Passed`**.
3. **Handling of `Skipped` Tests**: A test explicitly skipped by an operator without an approved supervisor override causes the run to be flagged as incomplete or **`Failed`**. It cannot evaluate to `Passed`.
4. **Handling of `ManualOverride`**: A test manually overridden requires an authorized supervisor or manager credentials with documented reason. The run status evaluates to `PassedWithOverride`, which is counted in the numerator **only** if the facility policy permits supervisor-approved overrides in FTPR. In strict mode, it is counted as a failure for initial intake calculations.

---

## 2. Aging WIP & Dwell Time Analysis

### 2.1 Objective
Track the velocity of inventory through depot queues, pinpointing bottlenecked devices and identifying defect blockers before SLA breach occurs.

### 2.2 Mathematical Formulations
Let $T_{\text{now}}$ be the current UTC timestamp.

#### Total Dwell Time ($D_{\text{total}}$)
The elapsed duration since device intake:
$$D_{\text{total}}(a) = T_{\text{now}} - T_{\text{intake}}(a)$$

#### Queue Dwell Time ($D_{\text{queue}}$)
The elapsed duration in the unit's current active lifecycle queue $q \in \{\text{ReadyForTest}, \text{InTest}, \text{Hold}, \text{Repair}, \text{Retest}\}$:
$$D_{\text{queue}}(a) = T_{\text{now}} - T_{\text{transition}}(a, q)$$

### 2.3 Aging SLA Severity Tiers
| Tier | Dwell Duration ($D_{\text{queue}}$) | Visual Indicator | Depot Action Required |
| :--- | :--- | :--- | :--- |
| **Nominal** | $< 24\text{ hours}$ | 🟢 Green | Standard bench queue FIFO dispatch |
| **Elevated** | $24\text{ to }72\text{ hours}$ | 🟡 Amber | Supervisor review; expediting bench allocation |
| **Critical SLA Breach** | $> 72\text{ hours}$ | 🔴 Red | Immediate escalation; parts requisition check |

### 2.4 Defect Blocker Identification
Every aging WIP unit is automatically attributed with a **Primary Blocker**:
1. If the asset has a diagnosed WIP issue (e.g. `Keyboard issue`, `Battery health < 50%`), the blocker is categorized as `Defect: {work_in_progress}`.
2. If the asset failed a bench test, the blocker is mapped as `QC Failure: {test_name}` (e.g. `QC Failure: DisplayTiming`).
3. If unassigned or pending parts, the blocker falls back to the queue state:
   - `Hold` $\rightarrow$ `Administrative / Parts Hold`
   - `Repair` $\rightarrow$ `Awaiting Technician Repair`
   - `Retest` $\rightarrow$ `Awaiting Retest Bench Run`

---

## 3. Throughput & Volume Metrics

### 3.1 24h QC Bench Throughput
$$N_{\text{QC, 24h}} = |\{r \in \mathcal{R} \mid \text{Status}(r) = \text{Passed} \land T_{\text{completed}}(r) \ge T_{\text{now}} - 24\text{h}\}|$$
Counts all bench test passes across all benches within the sliding 24-hour window.

### 3.2 7-Day Weekly Throughput
$$N_{\text{QC, 7d}} = |\{r \in \mathcal{R} \mid \text{Status}(r) = \text{Passed} \land T_{\text{completed}}(r) \ge T_{\text{now}} - 7\text{d}\}|$$

### 3.3 Outbound Release Velocity
Total units transitioned to `ReadyForRelease` and verified by tamper-evident cryptographic seal within the period.

---

## 4. Active Exceptions

### 4.1 Definition
Active exceptions represent inventory requiring supervisor intervention or operational remediation:
$$\text{Exceptions} = |\text{Assets in 'Hold' Queue}| + |\text{Active Overrides Awaiting Re-inspection}|$$

---

## 5. Bench Utilization & Concurrency

### 5.1 Active Concurrency
The instantaneous number of benches actively executing test suites:
$$C_{\text{bench}} = |\{b \in \mathcal{B} \mid \text{Status}(b) = \text{TestingNow}\}|$$

### 5.2 Station Efficiency
$$\text{Efficiency} = \frac{\text{Actual QC Test Execution Time}}{\text{Total Shift Station Uptime}} \times 100\%$$

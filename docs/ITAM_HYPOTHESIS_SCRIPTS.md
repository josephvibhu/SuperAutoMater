# ITAM Hypothesis Validation: Customer Hypotheses & Interview Scripts

## Executive Summary
This document establishes the research foundation for testing the core ITAM hypothesis: **Do IT leaders and depot operators value a verifiable, physical-transition lifecycle timeline and component verification baseline enough to purchase a focused solution?**

We test this hypothesis without building generic ITAM (no software asset management, no cloud contracts, no depreciation schedules).

---

## 1. Core Hypotheses

### Hypothesis A: Offboarding Recovery & Hardware Integrity
* **Belief:** Enterprise IT departments lose visibility into hardware condition when employees return devices remotely. Third-party logistics (3PL) and depot intake desks often miss swapped SSDs, degraded batteries, or downgraded RAM before restaging.
* **Target Persona:** Director of IT Operations / End-User Computing (EUC) Lead.
* **Value Metric:** Reduction in surprise failure rates on redeployed laptops; prevention of unrecorded hardware downgrade.

### Hypothesis B: Repair-Component Accountability & Depot Quality
* **Belief:** Refurbishment facilities, ITADs, and multi-depot operations lack an immutable verification baseline between intake, internal repair benches, and outbound release, creating friction over which party swapped or failed a component.
* **Target Persona:** Depot Operations Manager / Head of Refurbishment & QA.
* **Value Metric:** Zero-dispute warranty handoffs; proof that replaced parts (e.g. SSD swap) were authorized maintenance rather than unverified discrepancies.

### Hypothesis C: Redeployment-Quality Decisions
* **Belief:** IT managers spend excessive labor determining whether an offboarded laptop can be redeployed to a VIP/developer or must be retired. A single verifiable transition report showing disk health, battery health, and original vs. current component alignment eliminates manual diagnostic triage.
* **Target Persona:** IT Asset Manager / Fleet Operations Supervisor.
* **Value Metric:** Turnaround time per returned laptop cut from 45 minutes to under 5 minutes with certified decision documentation.

---

## 2. Customer Interview Scripts

### Section 1: Qualification & Current Friction (5–7 mins)
1. *"When a remote employee ships a laptop back or when a batch arrives at your depot, how do you verify that what came back inside the chassis matches what was originally issued?"*
2. *"How often do you discover that an SSD, RAM stick, or battery is different or degraded after a machine has already been marked 'Ready for Restaging'?"*
3. *"When components are replaced during service or repair, how is that documented in your current ITAM or ticketing tool? Does the ticket link directly to physical hardware telemetry?"*

### Section 2: Neutral Discrepancy Reaction (10 mins)
*Show wireframe of the Central Transition Timeline and Component Difference card (showing neutral "Difference detected: Disk Serial WD-1234 -> CT-5678" with Approved Replacement toggle).*
4. *"If your intake station automatically generated this timeline without manual data entry, what would your team do with this alert?"*
5. *"Notice the wording 'Difference detected' with an option to mark 'Approved Replacement'. How would this fit into your vendor warranty or employee offboarding workflow?"*
6. *"Does having an HMAC-signed return certificate give your clients or internal finance team sufficient audit evidence?"*

### Section 3: Commercial Gate & Decision Criteria (8–10 mins)
7. *"We are not building a replacement for ServiceNow or Snipe-IT; we integrate as the physical verification engine at handoff transitions. If this tool was available next month to verify 500 assets/month across your benches, what metric would you need to see to approve a $300/month pilot?"*
8. *"Would you be willing to run a 30-day paid pilot on 50 incoming devices next sprint?"*

---

## 3. Decision Gate Rules

* **Pass Gate:** At least 2 of 3 interviewees confirm that physical component verification at transition solves an urgent operational bottleneck, and at least 1 commits to a paid pilot agreement.
* **Pivot/Stop Gate:** If respondents state that generic MDM (Intune/Jamf) or manual tickets are "good enough" and show no willingness to budget for transition verification, Track B expansion is stopped and investment reverts entirely to bench diagnostics.

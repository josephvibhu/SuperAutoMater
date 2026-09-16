# Scan-to-Verify Customer Experience & Data Redaction Specification (Phase 3)

## 1. Objective: High-Trust Refurbished Buyer Assurance

When an enterprise IT buyer, MSP purchaser, or consumer buys a refurbished laptop, their primary anxieties are:
1. *Is the battery degraded or worn out?*
2. *Is the SSD worn down or nearing failure?*
3. *Was the screen replaced with a defective or third-party panel?*
4. *Are the specs advertised (CPU, RAM, GPU) authentic or counterfeit?*
5. *Is this certification genuine, or just a generic static PDF sticker?*

The **Scan-to-Verify** feature (`/verify/{runId}`) resolves these anxieties by providing an instant, mobile-optimized public verification report accessed via QR code on the physical chassis label.

---

## 2. Trust-Building Data Inclusions vs. Mandatory Redactions

| Data Field | Buyer Visibility | Rationale |
| :--- | :---: | :--- |
| **Certified Cosmetic Grade** | **EXPOSED** | Validates that the device matches the marketplace listing (`GRADE A`, `GRADE B+`). |
| **Model & Manufacturer** | **EXPOSED** | Confirms identity reported directly from motherboard BIOS. |
| **Serial Number / Service Tag** | **EXPOSED** | Allows buyer to cross-check warranty status and chassis markings. |
| **Cryptographic SHA-256 Seal** | **EXPOSED** | Proves tamper-proof audit trail; immutable ledger guarantee. |
| **Diagnostic Test Matrix** | **EXPOSED** | Shows green "PASSED" badge for CPU, RAM, NVMe, Battery, Display, Audio, Keyboard. |
| **Battery Health % & Capacities**| **EXPOSED** | High-value proof: proves remaining battery integrity without opening the chassis. |
| **Storage NVMe Wear & SMART** | **EXPOSED** | Validates SSD health, power-on hours, and nominal controller state. |
| **Audit Completion Date/Time** | **EXPOSED** | Proves freshness of refurbishing validation. |
| **Technician Full Name / ID** | **REDACTED** | Employee privacy and social engineering protection. Replaced with verified organizational seal. |
| **Internal Bench IP / MAC** | **REDACTED** | Corporate network reconnaissance prevention; prevents LAN topology discovery. |
| **Session / Bearer Tokens** | **REDACTED** | High security risk; zero credentials or access tokens exposed in public HTML. |
| **Prior Customer / Lease ID** | **REDACTED** | Strict data privacy compliance (GDPR, CCPA, NDA). |
| **Internal Purchase Cost / Margins**| **REDACTED** | Proprietary commercial financial data. |

---

## 3. Buyer Verification Experience Flow

```text
[ Physical Unit on Desk ]
        |
        v
[ Scan QR on Thermal Label ]
        |
        v
[ Mobile Browser opens: https://verify.domain.com/verify/a1b2c3d4... ]
        |
        +---> Verified Green Banner: "CRYPTOGRAPHICALLY VERIFIED"
        |
        +---> Identity Verification: Dell Latitude 5420 · S/N: 8849202
        |
        +---> Quality Grade: GRADE A (Passed 100% Diagnostic Suite)
        |
        +---> Detailed Sensor Pass Matrix:
        |       ✓ Intel Core i7-1185G7 @ 3.00GHz (Stress Nominal)
        |       ✓ 16GB DDR4 Dual-Channel (MemTest Verified)
        |       ✓ 512GB NVMe SSD (Health: 98% · Power-on: 420h)
        |       ✓ Primary Battery (Health: 92% · 53,200 mWh)
        |       ✓ Full Key Matrix & Multi-Touch Precision Clickpad
        |       ✓ IPS Display: Zero dead/stuck sub-pixels detected
        |
        +---> Cryptographic Seal Box:
                SHA-256: 7F83B1657FF1FC53B92DC18148A1D65DFC2D4B1FA3D677284ADDD200126D9069
```

---

## 4. Mobile Responsiveness & Security Architecture

1. **Lightweight Native HTML/CSS:**
   - Zero external JavaScript frameworks (no React, no jQuery, no remote CDN scripts).
   - Zero tracking cookies or third-party analytics pixels.
   - Renders in < 150 ms on 4G/5G mobile browsers.
2. **Strict Rate Limiting & DoS Protection:**
   - Public endpoint `/verify/{runId}` is read-only.
   - Run ID lookups query indexed SQLite key in < 2 ms.
   - Malformed or unknown IDs return a clean HTTP 404 "Verification Pending / Invalid Run" page with no stack traces or server version headers.

# ITAM Security, Enrollment & Risk Governance Specification

## 1. Multi-Tenant Scoping & Architectural Isolation

### Design Principles
1. **Zero Cross-Tenant Leakage:** Every database query against assets, custody events, component observations, and differences is filtered by `tenant_id`.
2. **Tenant Scoping at the Edge:** `ApiKeyMiddleware` authenticates incoming requests via the `X-Api-Key` header and immediately injects a scoped `TenantContext` into DI. If a tenant attempts to inspect or mutate an asset belonging to another tenant, the API responds with `404 Not Found` to prevent asset existence discovery.
3. **Dedicated Central DB:** Central `ItamService` runs on its own isolated SQLite engine (`itam.db`), physically separated from individual bench diagnostic databases (`superautomater-qc.db`).

---

## 2. Credential Enrollment & Key Lifecycle

### Enrollment Workflow
1. **Master Admin Provisioning:** System administrators supply the master key defined via the `ITAM_ADMIN_KEY` environment variable to access `/api/v1/admin/*` endpoints.
2. **Tenant & Site Generation:** An administrator calls `POST /api/v1/admin/tenants` with `{ "name": "...", "site_code": "...", "first_key_label": "..." }`.
3. **Cryptographic Key Generation:**
   * Keys are generated using a cryptographically secure random number generator (`RandomNumberGenerator.GetBytes(32)` -> 256-bit entropy).
   * The plain-text key is returned **once and only once** upon creation.
   * Only the salted SHA-256 hash (`key_hash`) is persisted in the database.
4. **Immediate Revocation:** If a key is compromised, an admin invokes `DELETE /api/v1/admin/tenants/{tenantId}/keys/{keyId}`. The middleware rejects revoked keys immediately with `401 Unauthorized`.

---

## 3. Data Retention & Immutability Rules

### Observations Never Overwritten
* Diagnostic raw telemetry is append-only. When a bench submits new telemetry for an asset, a new row is appended to `component_observations` with a timestamp, bench identifier, and source confidence score.
* Existing observations are **never updated or deleted**.

### Discrepancy Reconciliation
* When a component attribute differs between sequential observations, a `component_differences` record is generated.
* Marking an approved replacement sets `approved_replacement = 1` and records `approved_by` and `approved_at_utc`. The alert resolves, but the full historical record of both the original and replaced components remains permanently queryable.

---

## 4. Legal & Operational Risk Mitigation: Neutral Wording

### Mandatory Terminology Governance
Accusations of employee or technician theft create severe operational, labor, and legal liability. All system interfaces, API responses, logs, and customer reports must adhere strictly to neutral observation terms:

| Forbidden Accusatory Term | Mandatory Neutral Operational Term |
| :--- | :--- |
| "Theft detected" / "Stolen part" | **"Difference detected"** / **"Discrepancy noted"** |
| "Unauthorized tampering" | **"Unreconciled component change"** |
| "Cryptographic proof of crime" | **"Observed hardware telemetry baseline"** |
| "Guilty technician" | **"Last logged transition actor"** |
| "Counterfeit part installed" | **"Non-OEM attribute observed"** |

---

## 5. Report Integrity & Export Verification

### HMAC-SHA256 Cryptographic Signatures
Customer Return and Redeployment Reports include an HMAC-SHA256 digest calculated over the canonical JSON representation of:
* Asset identity (Serial, Tag, UUID)
* Tenant & Site code
* Generation timestamp
* Event timeline count & difference resolution summary

Any offline tampering with the generated report JSON or HTML invalidates the signature, providing undeniable proof of transit integrity to external auditors and client stakeholders.

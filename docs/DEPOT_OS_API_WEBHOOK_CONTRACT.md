# Depot OS API & Signed Webhook Delivery Contract

This specification documents the REST API surface and cryptographic webhook contract implemented in **SuperAutoMater** and **SuperManager** (v1.6.6).

---

## 1. Cryptographic Webhook Security Specification

### 1.1 HTTP Headers
Every outbound webhook dispatch transmitted by Depot OS includes the following immutable headers:

| Header Name | Type | Description |
| :--- | :--- | :--- |
| `X-Depot-Signature` | String | HMAC-SHA256 signature in format: `sha256={hex}` |
| `X-Depot-Timestamp` | Integer | Epoch timestamp in seconds (UTC) at request signing |
| `X-Depot-Site-Id` | String | Configured facility identity (e.g. `DEPOT-US-EAST-01`) |
| `Idempotency-Key` | UUID / String | Unique payload identifier to prevent duplicate processing |

### 1.2 Signature Computation
The message signed by the HMAC key is constructed by concatenating the timestamp and the exact JSON payload:
$$\text{Signature} = \text{"sha256="} + \text{Hex}(\text{HMAC-SHA256}(\text{Key}, T_{\text{epoch}} + \text{"."} + \text{PayloadJson}))$$

### 1.3 Replay Attack Mitigation
Receiving endpoints MUST enforce a timestamp tolerance window of **$\pm 300$ seconds (5 minutes)**. Requests where $|T_{\text{server}} - T_{\text{header}}| > 300$ MUST be rejected with HTTP `401 Unauthorized` or `400 Bad Request`.

### 1.4 Idempotency Guarantees
- Outbound webhooks attach a globally unique `Idempotency-Key`.
- If a receiving endpoint receives a duplicate `Idempotency-Key` within 24 hours, it MUST return the cached successful response rather than re-executing inventory state changes or double-billing.

---

## 2. Webhook Event Schemas

### 2.1 `QcRunCompletedEvent`
Dispatched immediately upon completion of bench diagnostic testing.

```json
{
  "EventType": "QcRunCompletedEvent",
  "SiteId": "DEPOT-US-EAST-01",
  "IdempotencyKey": "a9d7b4e1832049e083c07e2ef309a47b",
  "Timestamp": 1789537200,
  "Data": {
    "RunId": "run-40285a73e6a94f1b",
    "AssetId": "asset-c91837ea92",
    "SerialNumber": "PF3X89AZ",
    "AssetTag": "TAG-2026-9042",
    "Model": "ThinkPad T14 Gen 3",
    "Status": "Passed",
    "Grade": "A+",
    "VerificationHash": "B4E12DF8097C23984E290AC7832B104F5E8098231CA6B70948D2094B8A019482",
    "Technician": "John Doe (TECH-01)",
    "StorageHealth": 98,
    "BatteryHealth": 94,
    "CompletedAtUtc": "2026-09-16T12:00:00Z"
  }
}
```

### 2.2 `AssetReleasedEvent`
Dispatched when an asset passes the release gate and is authorized for sale/distribution.

```json
{
  "EventType": "AssetReleasedEvent",
  "SiteId": "DEPOT-US-EAST-01",
  "IdempotencyKey": "f3b890a84e2098b10892acb8901237ef",
  "Timestamp": 1789537350,
  "Data": {
    "AssetId": "asset-c91837ea92",
    "AssetTag": "TAG-2026-9042",
    "SerialNumber": "PF3X89AZ",
    "AuthorizedBy": "Jane Smith (Floor Manager)",
    "Status": "RTS",
    "ReleasedAtUtc": "2026-09-16T12:02:30Z"
  }
}
```

---

## 3. SuperManager REST API Endpoints

All endpoints bind securely to `127.0.0.1:9000` (configurable) and require an admin token passed via `?token={token}` or `Authorization: Bearer {token}`.

### 3.1 `GET /api/depot/analytics`
Returns aggregate throughput, mathematically rigorous First-Time Pass Rate (FTPR), active exceptions count, and retest defect Pareto breakdown.

### 3.2 `GET /api/depot/wip/aging?limit=10`
Returns the 10 oldest WIP units across active queues with dwell durations and identified defect blockers.

### 3.3 `GET /api/depot/audit?limit=50`
Returns the immutable audit log trail recording all administrative mutations, user authentications, and access attempts.

---

## 4. Verification Code Samples

### C# / .NET 10 Verification
```csharp
using System.Security.Cryptography;
using System.Text;

public static bool VerifyWebhook(string payload, string signatureHeader, long timestamp, string secret)
{
    if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp) > 300) return false;
    string message = $"{timestamp}.{payload}";
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    string expected = "sha256=" + BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(message))).Replace("-", "").ToLowerInvariant();
    return string.Equals(expected, signatureHeader, StringComparison.OrdinalIgnoreCase);
}
```

### Python 3 Verification
```python
import hmac
import hashlib
import time

def verify_depot_webhook(payload: str, signature: str, timestamp: int, secret: str) -> bool:
    if abs(time.time() - timestamp) > 300:
        return False
    message = f"{timestamp}.{payload}".encode("utf-8")
    expected = "sha256=" + hmac.new(secret.encode("utf-8"), message, hashlib.sha256).hexdigest()
    return hmac.compare_digest(expected, signature)
```

### Node.js Verification
```javascript
const crypto = require('crypto');

function verifyDepotWebhook(payload, signature, timestamp, secret) {
  if (Math.abs(Math.floor(Date.now() / 1000) - timestamp) > 300) return false;
  const message = `${timestamp}.${payload}`;
  const expected = 'sha256=' + crypto.createHmac('sha256', secret).update(message).digest('hex');
  return crypto.timingSafeEqual(Buffer.from(expected), Buffer.from(signature));
}
```

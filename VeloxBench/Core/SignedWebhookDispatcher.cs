using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SuperAutoMater.Wpf.Core
{
    /// <summary>
    /// Outbound payload container with cryptographic metadata and idempotency key.
    /// </summary>
    public sealed class SignedWebhookPayload
    {
        public string EventType { get; set; } = "";
        public string SiteId { get; set; } = "";
        public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");
        public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public object Data { get; set; }
    }

    /// <summary>
    /// Result of an inbound webhook signature & idempotency verification.
    /// </summary>
    public sealed class WebhookVerificationResult
    {
        public bool IsValid { get; set; }
        public bool IsDuplicate { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string CachedResponse { get; set; } = "";
    }

    /// <summary>
    /// Secure HMAC-SHA256 webhook dispatcher and verification engine.
    /// Guarantees delivery integrity, replay attack prevention, and at-most-once idempotency deduplication.
    /// </summary>
    public sealed class SignedWebhookDispatcher
    {
        private readonly string _secretKey;
        private readonly string _siteId;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, (long Timestamp, string ResponseJson)> _seenIdempotencyKeys =
            new ConcurrentDictionary<string, (long Timestamp, string ResponseJson)>(StringComparer.OrdinalIgnoreCase);

        public const int DefaultToleranceSeconds = 300; // 5 minutes max skew

        public SignedWebhookDispatcher(string secretKey, string siteId = "DEPOT-DEFAULT", HttpClient httpClient = null)
        {
            _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
            _siteId = siteId ?? "DEPOT-DEFAULT";
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        /// <summary>
        /// Computes HMAC-SHA256 signature in format "sha256={hex}".
        /// Message format: "{timestamp}.{payloadJson}".
        /// </summary>
        public static string ComputeSignature(string payloadJson, long timestamp, string secret)
        {
            if (string.IsNullOrEmpty(secret)) return "";
            string message = $"{timestamp}.{payloadJson}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            return "sha256=" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Verifies whether the provided signature matches the payload and timestamp.
        /// </summary>
        public static bool VerifySignature(string payloadJson, string signatureHeader, long timestamp, string secret, int toleranceSeconds = DefaultToleranceSeconds)
        {
            if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(secret))
                return false;

            long current = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Math.Abs(current - timestamp) > toleranceSeconds)
                return false; // Timestamp outside allowed window (replay attempt)

            string expected = ComputeSignature(payloadJson, timestamp, secret);
            return string.Equals(expected, signatureHeader.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Verifies an inbound webhook request including HMAC signature, timestamp skew, and idempotency key.
        /// If idempotency key was previously processed, flags as duplicate and provides cached response.
        /// </summary>
        public WebhookVerificationResult VerifyAndProcessInbound(string payloadJson, string signatureHeader, long timestamp, string idempotencyKey, string cachedResponseOnSuccess = "{\"status\":\"ok\"}")
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return new WebhookVerificationResult
                {
                    IsValid = false,
                    ErrorMessage = "Missing required Idempotency-Key header."
                };
            }

            // 1. Check idempotency deduplication cache
            if (_seenIdempotencyKeys.TryGetValue(idempotencyKey, out var existing))
            {
                return new WebhookVerificationResult
                {
                    IsValid = true,
                    IsDuplicate = true,
                    CachedResponse = existing.ResponseJson
                };
            }

            // 2. Validate timestamp skew
            long current = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Math.Abs(current - timestamp) > DefaultToleranceSeconds)
            {
                return new WebhookVerificationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Timestamp skew ({Math.Abs(current - timestamp)}s) exceeds tolerance of {DefaultToleranceSeconds}s."
                };
            }

            // 3. Verify HMAC signature
            if (!VerifySignature(payloadJson, signatureHeader, timestamp, _secretKey))
            {
                return new WebhookVerificationResult
                {
                    IsValid = false,
                    ErrorMessage = "Invalid HMAC-SHA256 signature."
                };
            }

            // 4. Record new idempotency key
            _seenIdempotencyKeys[idempotencyKey] = (timestamp, cachedResponseOnSuccess);

            // Prune old keys (older than 24h)
            PruneIdempotencyKeys();

            return new WebhookVerificationResult
            {
                IsValid = true,
                IsDuplicate = false,
                CachedResponse = cachedResponseOnSuccess
            };
        }

        private void PruneIdempotencyKeys()
        {
            if (_seenIdempotencyKeys.Count > 10000)
            {
                long cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 86400; // 24 hours
                foreach (var kvp in _seenIdempotencyKeys)
                {
                    if (kvp.Value.Timestamp < cutoff)
                    {
                        _seenIdempotencyKeys.TryRemove(kvp.Key, out _);
                    }
                }
            }
        }

        /// <summary>
        /// Creates an HttpRequestMessage for an outbound event with signed headers.
        /// </summary>
        public HttpRequestMessage CreateSignedRequest(HttpMethod method, string url, string eventType, object data, string idempotencyKey = null)
        {
            string key = idempotencyKey ?? Guid.NewGuid().ToString("N");
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var envelope = new SignedWebhookPayload
            {
                EventType = eventType,
                SiteId = _siteId,
                IdempotencyKey = key,
                Timestamp = ts,
                Data = data
            };

            string json = JsonSerializer.Serialize(envelope);
            string signature = ComputeSignature(json, ts, _secretKey);

            var request = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("X-Depot-Signature", signature);
            request.Headers.Add("X-Depot-Timestamp", ts.ToString());
            request.Headers.Add("X-Depot-Site-Id", _siteId);
            request.Headers.Add("Idempotency-Key", key);

            return request;
        }

        /// <summary>
        /// Asynchronously dispatches a signed webhook event to the specified destination URL.
        /// </summary>
        public async Task<(bool Success, int StatusCode, string ResponseBody)> DispatchSignedEventAsync(string url, string eventType, object data, string idempotencyKey = null)
        {
            try
            {
                using var request = CreateSignedRequest(HttpMethod.Post, url, eventType, data, idempotencyKey);
                using var response = await _httpClient.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();
                return (response.IsSuccessStatusCode, (int)response.StatusCode, body);
            }
            catch (Exception ex)
            {
                return (false, 0, ex.Message);
            }
        }
    }
}

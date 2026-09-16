using System;
using System.IO;
using System.Security.Cryptography;

namespace SuperAutoMater.Wpf.Core
{
    /// <summary>
    /// Manages file-backed QC and warehouse evidence (photos, sensor dumps, diagnostic logs)
    /// with cryptographic SHA-256 hashing and lifecycle retention.
    /// </summary>
    public sealed class EvidenceStorageService
    {
        private readonly QcRunStore _store;
        private readonly string _evidenceRootDir;

        public string EvidenceDirectory => _evidenceRootDir;

        public EvidenceStorageService(QcRunStore store = null, string customDirectory = null)
        {
            _store = store ?? new QcRunStore();
            _evidenceRootDir = customDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuperAutoMater", "evidence");
            if (!Directory.Exists(_evidenceRootDir))
            {
                Directory.CreateDirectory(_evidenceRootDir);
            }
        }

        public QcEvidence StoreEvidenceFile(string assetId, string runId, string evidenceType, string sourceFilePath, string notes = null, string testKey = null)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));
            if (string.IsNullOrWhiteSpace(sourceFilePath)) throw new ArgumentException("Source file path is required.", nameof(sourceFilePath));
            if (!File.Exists(sourceFilePath)) throw new FileNotFoundException("Evidence source file not found.", sourceFilePath);

            byte[] fileBytes = File.ReadAllBytes(sourceFilePath);
            string fileHash = ComputeSha256(fileBytes);

            string assetDir = Path.Combine(_evidenceRootDir, assetId);
            if (!Directory.Exists(assetDir))
            {
                Directory.CreateDirectory(assetDir);
            }

            string ext = Path.GetExtension(sourceFilePath);
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{timestamp}_{Guid.NewGuid().ToString("N").Substring(0, 8)}{ext}";
            string destinationPath = Path.Combine(assetDir, fileName);

            File.Copy(sourceFilePath, destinationPath, overwrite: true);

            string metadataJson = "{\"originalName\":\"" + Path.GetFileName(sourceFilePath) +
                                  "\",\"sizeBytes\":" + fileBytes.Length +
                                  ",\"notes\":\"" + (notes ?? "").Replace("\"", "\\\"") + "\"}";

            var record = new QcEvidence
            {
                Id = Guid.NewGuid().ToString("N"),
                RunId = runId ?? "",
                TestKey = testKey ?? "",
                EvidenceType = evidenceType ?? "Photo",
                FilePath = destinationPath,
                FileHash = fileHash,
                MetadataJson = metadataJson,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };

            if (!string.IsNullOrWhiteSpace(runId))
            {
                _store.RecordEvidence(runId, record);
            }
            else
            {
                _store.AttachEvidenceToAsset(assetId, testKey, record.EvidenceType, record.FilePath, record.FileHash, record.MetadataJson);
            }

            AppLogger.Info($"[Evidence] Stored '{evidenceType}' evidence for asset '{assetId}' (Hash: {fileHash}, Path: {destinationPath})");
            return record;
        }

        public QcEvidence StoreEvidenceBytes(string assetId, string runId, string evidenceType, byte[] data, string fileExtension, string notes = null, string testKey = null)
        {
            if (string.IsNullOrWhiteSpace(assetId)) throw new ArgumentException("Asset ID is required.", nameof(assetId));
            if (data == null || data.Length == 0) throw new ArgumentException("Evidence data cannot be empty.", nameof(data));

            string fileHash = ComputeSha256(data);
            string assetDir = Path.Combine(_evidenceRootDir, assetId);
            if (!Directory.Exists(assetDir))
            {
                Directory.CreateDirectory(assetDir);
            }

            string ext = string.IsNullOrWhiteSpace(fileExtension) ? ".bin" : (fileExtension.StartsWith(".") ? fileExtension : "." + fileExtension);
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{timestamp}_{Guid.NewGuid().ToString("N").Substring(0, 8)}{ext}";
            string destinationPath = Path.Combine(assetDir, fileName);

            File.WriteAllBytes(destinationPath, data);

            string metadataJson = "{\"sizeBytes\":" + data.Length + ",\"notes\":\"" + (notes ?? "").Replace("\"", "\\\"") + "\"}";

            var record = new QcEvidence
            {
                Id = Guid.NewGuid().ToString("N"),
                RunId = runId ?? "",
                TestKey = testKey ?? "",
                EvidenceType = evidenceType ?? "Photo",
                FilePath = destinationPath,
                FileHash = fileHash,
                MetadataJson = metadataJson,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };

            if (!string.IsNullOrWhiteSpace(runId))
            {
                _store.RecordEvidence(runId, record);
            }
            else
            {
                _store.AttachEvidenceToAsset(assetId, testKey, record.EvidenceType, record.FilePath, record.FileHash, record.MetadataJson);
            }

            AppLogger.Info($"[Evidence] Stored binary evidence for asset '{assetId}' (Hash: {fileHash})");
            return record;
        }

        public int PurgeExpiredEvidence(TimeSpan retentionWindow)
        {
            int purged = 0;
            try
            {
                if (!Directory.Exists(_evidenceRootDir)) return 0;
                var cutoff = DateTime.UtcNow - retentionWindow;
                foreach (var file in Directory.EnumerateFiles(_evidenceRootDir, "*.*", SearchOption.AllDirectories))
                {
                    var fi = new FileInfo(file);
                    if (fi.CreationTimeUtc < cutoff)
                    {
                        fi.Delete();
                        purged++;
                    }
                }
                AppLogger.Info($"[Evidence] Purged {purged} expired evidence files older than {cutoff:yyyy-MM-dd}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("[Evidence] Failed during retention cleanup", ex);
            }
            return purged;
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
        }
    }
}

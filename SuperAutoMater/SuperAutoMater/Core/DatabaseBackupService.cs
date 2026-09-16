using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SuperAutoMater.Wpf.Core
{
    public sealed class BackupEvidenceItem
    {
        public string RelativePath { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long SizeBytes { get; set; }
    }

    public sealed class BackupManifest
    {
        public string SiteId { get; set; } = "";
        public string BackupId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public string DatabaseFileName { get; set; } = "database.db";
        public string DatabaseSha256 { get; set; } = "";
        public long DatabaseSizeBytes { get; set; }
        public List<BackupEvidenceItem> EvidenceFiles { get; set; } = new List<BackupEvidenceItem>();
    }

    public sealed class BackupResult
    {
        public bool Success { get; set; }
        public string BackupPath { get; set; } = "";
        public BackupManifest Manifest { get; set; }
        public string ErrorMessage { get; set; } = "";
    }

    public sealed class RestoreResult
    {
        public bool Success { get; set; }
        public int RestoredAssetsCount { get; set; }
        public int RestoredRunsCount { get; set; }
        public int RestoredEvidenceFilesCount { get; set; }
        public string ErrorMessage { get; set; } = "";
    }

    /// <summary>
    /// Automated backup and restoration engine for Depot OS.
    /// Employs atomic SQLite VACUUM INTO snapshotting, evidence directory bundling,
    /// cryptographic SHA-256 manifest verification, and clean-environment restoration checks.
    /// </summary>
    public sealed class DatabaseBackupService
    {
        private readonly QcRunStore _store;
        private readonly string _siteId;

        public DatabaseBackupService(QcRunStore store, string siteId = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _siteId = siteId ?? DepotConfigService.Instance.Config.SiteId;
        }

        /// <summary>
        /// Creates a complete atomic backup archive (including SQLite db and evidence) with SHA-256 manifest.
        /// </summary>
        public BackupResult CreateBackup(string targetDir, string evidenceDirectory = null, bool createZip = true)
        {
            if (string.IsNullOrWhiteSpace(targetDir))
                throw new ArgumentException("Target directory cannot be empty.", nameof(targetDir));

            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string backupId = $"backup_{timestamp}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            string stagingFolder = Path.Combine(targetDir, backupId + "_staging");

            try
            {
                if (Directory.Exists(stagingFolder)) Directory.Delete(stagingFolder, true);
                Directory.CreateDirectory(stagingFolder);

                // 1. Atomic SQLite snapshot using VACUUM INTO
                string dbBackupPath = Path.Combine(stagingFolder, "database.db");
                _store.BackupDatabase(dbBackupPath);

                string dbHash = ComputeFileSha256(dbBackupPath);
                long dbSize = new FileInfo(dbBackupPath).Length;

                // 2. Snapshot evidence directory if present
                var manifest = new BackupManifest
                {
                    SiteId = _siteId,
                    BackupId = backupId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    DatabaseFileName = "database.db",
                    DatabaseSha256 = dbHash,
                    DatabaseSizeBytes = dbSize
                };

                string evidenceStaging = Path.Combine(stagingFolder, "evidence");
                Directory.CreateDirectory(evidenceStaging);

                if (!string.IsNullOrEmpty(evidenceDirectory) && Directory.Exists(evidenceDirectory))
                {
                    foreach (var filePath in Directory.EnumerateFiles(evidenceDirectory, "*.*", SearchOption.AllDirectories))
                    {
                        string relative = Path.GetRelativePath(evidenceDirectory, filePath);
                        string destPath = Path.Combine(evidenceStaging, relative);
                        string dir = Path.GetDirectoryName(destPath);
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                        File.Copy(filePath, destPath, true);
                        string fileHash = ComputeFileSha256(destPath);
                        long fileSize = new FileInfo(destPath).Length;

                        manifest.EvidenceFiles.Add(new BackupEvidenceItem
                        {
                            RelativePath = relative,
                            Sha256 = fileHash,
                            SizeBytes = fileSize
                        });
                    }
                }

                // 3. Write manifest.json
                string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(stagingFolder, "manifest.json"), manifestJson, Encoding.UTF8);

                string finalPath;
                if (createZip)
                {
                    finalPath = Path.Combine(targetDir, $"{backupId}.zip");
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                    ZipFile.CreateFromDirectory(stagingFolder, finalPath, CompressionLevel.Optimal, false);
                    SafeDeleteDirectory(stagingFolder);
                }
                else
                {
                    finalPath = Path.Combine(targetDir, backupId);
                    if (Directory.Exists(finalPath)) SafeDeleteDirectory(finalPath);
                    Directory.Move(stagingFolder, finalPath);
                }

                return new BackupResult
                {
                    Success = true,
                    BackupPath = finalPath,
                    Manifest = manifest
                };
            }
            catch (Exception ex)
            {
                try { if (Directory.Exists(stagingFolder)) Directory.Delete(stagingFolder, true); } catch { }
                return new BackupResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Restores a backup into target paths and verifies database integrity and SHA-256 hashes.
        /// </summary>
        public RestoreResult RestoreBackup(string backupSourcePath, string targetDbPath, string targetEvidenceDir)
        {
            if (string.IsNullOrWhiteSpace(backupSourcePath) || (!File.Exists(backupSourcePath) && !Directory.Exists(backupSourcePath)))
                return new RestoreResult { Success = false, ErrorMessage = "Backup source file or directory does not exist." };

            string extractFolder = Path.Combine(Path.GetTempPath(), "superautomater_restore_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(extractFolder);

                // 1. Unpack if ZIP
                if (File.Exists(backupSourcePath) && backupSourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(backupSourcePath, extractFolder);
                }
                else if (Directory.Exists(backupSourcePath))
                {
                    CopyDirectory(backupSourcePath, extractFolder);
                }
                else
                {
                    return new RestoreResult { Success = false, ErrorMessage = "Unsupported backup format." };
                }

                // 2. Read & Verify Manifest
                string manifestPath = Path.Combine(extractFolder, "manifest.json");
                if (!File.Exists(manifestPath))
                    return new RestoreResult { Success = false, ErrorMessage = "Corrupted backup: manifest.json is missing." };

                string manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
                var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestJson);
                if (manifest == null)
                    return new RestoreResult { Success = false, ErrorMessage = "Corrupted backup: manifest.json could not be parsed." };

                // 3. Verify Database Checksum
                string dbSource = Path.Combine(extractFolder, manifest.DatabaseFileName ?? "database.db");
                if (!File.Exists(dbSource))
                    return new RestoreResult { Success = false, ErrorMessage = "Corrupted backup: database file is missing from archive." };

                string actualDbHash = ComputeFileSha256(dbSource);
                if (!string.Equals(actualDbHash, manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Database SHA-256 integrity check failed! Expected: {manifest.DatabaseSha256}, Actual: {actualDbHash}");
                }

                // 4. Verify Evidence Checksums
                string evidenceFolder = Path.Combine(extractFolder, "evidence");
                int restoredEvidenceCount = 0;
                if (manifest.EvidenceFiles != null && manifest.EvidenceFiles.Count > 0)
                {
                    foreach (var item in manifest.EvidenceFiles)
                    {
                        string itemPath = Path.Combine(evidenceFolder, item.RelativePath);
                        if (!File.Exists(itemPath))
                            throw new InvalidDataException($"Missing evidence file '{item.RelativePath}' specified in manifest.");

                        string itemHash = ComputeFileSha256(itemPath);
                        if (!string.Equals(itemHash, item.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"Evidence file '{item.RelativePath}' checksum mismatch!");

                        restoredEvidenceCount++;
                    }
                }

                // 5. Restore Database File
                string targetDbDir = Path.GetDirectoryName(targetDbPath);
                if (!string.IsNullOrEmpty(targetDbDir) && !Directory.Exists(targetDbDir))
                    Directory.CreateDirectory(targetDbDir);

                File.Copy(dbSource, targetDbPath, true);

                // 6. Restore Evidence Files
                if (manifest.EvidenceFiles != null && manifest.EvidenceFiles.Count > 0 && !string.IsNullOrEmpty(targetEvidenceDir))
                {
                    if (!Directory.Exists(targetEvidenceDir)) Directory.CreateDirectory(targetEvidenceDir);
                    foreach (var item in manifest.EvidenceFiles)
                    {
                        string src = Path.Combine(evidenceFolder, item.RelativePath);
                        string dst = Path.Combine(targetEvidenceDir, item.RelativePath);
                        string dstDir = Path.GetDirectoryName(dst);
                        if (!Directory.Exists(dstDir)) Directory.CreateDirectory(dstDir);
                        File.Copy(src, dst, true);
                    }
                }

                // 7. Test restored database by opening and querying
                var testStore = QcRunStore.CreateForTest(targetDbPath);
                var assets = testStore.GetAllWipAssets();
                int assetCount = assets.Count;
                var kpis = testStore.GetDepotKpis();

                return new RestoreResult
                {
                    Success = true,
                    RestoredAssetsCount = assetCount,
                    RestoredRunsCount = kpis.DailyThroughput + kpis.WeeklyThroughput,
                    RestoredEvidenceFilesCount = restoredEvidenceCount
                };
            }
            finally
            {
                try { if (Directory.Exists(extractFolder)) Directory.Delete(extractFolder, true); } catch { }
            }
        }

        public static string ComputeFileSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), true);
            }
            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
            }
        }

        private static void SafeDeleteDirectory(string dirPath)
        {
            try
            {
                if (Directory.Exists(dirPath))
                {
                    Directory.Delete(dirPath, true);
                }
            }
            catch
            {
                try
                {
                    System.Threading.Thread.Sleep(50);
                    if (Directory.Exists(dirPath)) Directory.Delete(dirPath, true);
                }
                catch { }
            }
        }
    }
}

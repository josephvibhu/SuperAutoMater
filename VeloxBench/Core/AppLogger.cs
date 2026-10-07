using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace SuperAutoMater.Wpf.Core
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Security
    }

    public sealed class LogEntry
    {
        public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
        public LogLevel Level { get; set; }
        public string Component { get; set; } = "";
        public string Message { get; set; } = "";
        public string ErrorDetails { get; set; } = "";
        public string ContextDataJson { get; set; } = "";
    }

    /// <summary>
    /// High-throughput, structured, non-blocking application logger for SuperAutoMater.
    /// Automatically redacts session tokens, bearer headers, raw serial numbers, and webhook secrets.
    /// Writes rolling daily JSONL logs to %LOCALAPPDATA%\SuperAutoMater\logs\.
    /// </summary>
    public static class AppLogger
    {
        private static readonly BlockingCollection<LogEntry> _queue = new BlockingCollection<LogEntry>(10000);
        private static readonly Thread _writerThread;
        private static readonly string _logDirectory;
        private static volatile bool _isDisposed = false;

        // Fast regex patterns for sensitive data redaction
        private static readonly Regex _bearerRegex = new Regex(@"(?i)Bearer\s+[A-Za-z0-9_\-\.]{8,}", RegexOptions.Compiled);
        private static readonly Regex _tokenParamRegex = new Regex(@"(?i)([?&]token=)[A-Za-z0-9_\-\.]{8,}", RegexOptions.Compiled);
        private static readonly Regex _hexTokenRegex = new Regex(@"\b[0-9a-fA-F]{32,64}\b", RegexOptions.Compiled);
        private static readonly Regex _googleScriptRegex = new Regex(@"(?i)https://script\.google\.com/macros/s/([A-Za-z0-9_\-]+)/exec", RegexOptions.Compiled);

        static AppLogger()
        {
            try
            {
                _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuperAutoMater", "logs");
                Directory.CreateDirectory(_logDirectory);
            }
            catch
            {
                _logDirectory = Path.GetTempPath();
            }

            _writerThread = new Thread(ProcessLogQueue)
            {
                IsBackground = true,
                Name = "SuperAutoMater.LogWriter"
            };
            _writerThread.Start();

            AppDomain.CurrentDomain.ProcessExit += (s, e) => Shutdown();
        }

        public static void Debug(string component, string message, object context = null) =>
            Enqueue(LogLevel.Debug, component, message, null, context);

        public static void Info(string message) =>
            Enqueue(LogLevel.Info, "General", message, null, null);

        public static void Info(string component, string message, object context = null) =>
            Enqueue(LogLevel.Info, component, message, null, context);

        public static void Warn(string message, Exception ex = null) =>
            Enqueue(LogLevel.Warning, "General", message, ex, null);

        public static void Warn(string component, string message, Exception ex = null, object context = null) =>
            Enqueue(LogLevel.Warning, component, message, ex, context);

        public static void Error(string message, Exception ex = null) =>
            Enqueue(LogLevel.Error, "General", message, ex, null);

        public static void Error(string component, string message, Exception ex = null, object context = null) =>
            Enqueue(LogLevel.Error, component, message, ex, context);

        public static void Security(string component, string message, object context = null) =>
            Enqueue(LogLevel.Security, component, message, null, context);

        public static string MaskSerial(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial)) return "[NO-SERIAL]";
            string trimmed = serial.Trim();
            if (trimmed.Length <= 4) return "****";
            return string.Concat("SN-****", trimmed.Substring(trimmed.Length - 4));
        }

        public static string Redact(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            string sanitized = _bearerRegex.Replace(input, "Bearer [REDACTED]");
            sanitized = _tokenParamRegex.Replace(sanitized, "$1[REDACTED]");
            sanitized = _hexTokenRegex.Replace(sanitized, "[REDACTED_TOKEN]");
            sanitized = _googleScriptRegex.Replace(sanitized, "https://script.google.com/macros/s/[REDACTED_WEBHOOK]/exec");
            return sanitized;
        }

        private static void Enqueue(LogLevel level, string component, string message, Exception ex, object context)
        {
            if (_isDisposed) return;

            try
            {
                string ctxJson = "";
                if (context != null)
                {
                    try
                    {
                        string rawJson = JsonSerializer.Serialize(context);
                        ctxJson = Redact(rawJson);
                    }
                    catch
                    {
                        ctxJson = Redact(context.ToString() ?? "");
                    }
                }

                string err = "";
                if (ex != null)
                {
                    err = $"{ex.GetType().Name}: {Redact(ex.Message)}\n{ex.StackTrace}";
                }

                var entry = new LogEntry
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Level = level,
                    Component = component ?? "General",
                    Message = Redact(message ?? ""),
                    ErrorDetails = err,
                    ContextDataJson = ctxJson
                };

                _queue.TryAdd(entry);

                Trace.WriteLine($"[{entry.TimestampUtc:HH:mm:ss.fff}] [{entry.Level}] [{entry.Component}] {entry.Message} {err}");
            }
            catch
            {
                // Never allow logging failures to crash diagnostics
            }
        }

        private static void ProcessLogQueue()
        {
            while (!_isDisposed || _queue.Count > 0)
            {
                try
                {
                    if (_queue.TryTake(out var entry, 1000))
                    {
                        WriteToFile(entry);
                    }
                }
                catch
                {
                    // Fallback
                }
            }
        }

        private static void WriteToFile(LogEntry entry)
        {
            try
            {
                string dateSlug = entry.TimestampUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                string filePath = Path.Combine(_logDirectory, $"superautomater-{dateSlug}.jsonl");

                var record = new
                {
                    ts = entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
                    lvl = entry.Level.ToString().ToUpperInvariant(),
                    comp = entry.Component,
                    msg = entry.Message,
                    ctx = string.IsNullOrEmpty(entry.ContextDataJson) ? null : entry.ContextDataJson,
                    err = string.IsNullOrEmpty(entry.ErrorDetails) ? null : entry.ErrorDetails
                };

                string line = JsonSerializer.Serialize(record) + Environment.NewLine;
                File.AppendAllText(filePath, line, Encoding.UTF8);
            }
            catch
            {
                // Silent fallback
            }
        }

        public static void Flush(int timeoutMs = 2000)
        {
            var sw = Stopwatch.StartNew();
            while (_queue.Count > 0 && sw.ElapsedMilliseconds < timeoutMs)
            {
                Thread.Sleep(50);
            }
        }

        public static void Shutdown()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            try
            {
                _queue.CompleteAdding();
                _writerThread.Join(1500);
            }
            catch { }
        }
    }
}

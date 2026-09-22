using System;
using System.IO;

namespace VeloxBench.Core;

public static class AppLogger
{
    private static readonly object _lock = new();
    private static readonly string _logDir;
    private static readonly string _logFile;

    static AppLogger()
    {
        _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Velox", "VeloxBench", "Logs");
        Directory.CreateDirectory(_logDir);
        _logFile = Path.Combine(_logDir, $"bench_{DateTime.UtcNow:yyyyMMdd}.log");
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex != null ? $"{message} | EX: {ex.Message}\n{ex.StackTrace}" : message);

    private static void Write(string level, string message)
    {
        lock (_lock)
        {
            try
            {
                var entry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff UTC}] [{level}] {message}";
                Console.WriteLine(entry);
                File.AppendAllText(_logFile, entry + Environment.NewLine);
            }
            catch { }
        }
    }
}

using System.Runtime.CompilerServices;

namespace TACTIX.Engine.Core.Logging;

public enum LogLevel
{
    Info,
    Warning,
    Error
}

public readonly record struct LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Member, string Message);

public static class Log
{
    private const int MaxHistory = 500;
    private static readonly object Sync = new();
    private static readonly List<LogEntry> History = new();

    public static event Action<LogEntry>? EntryWritten;

    public static IReadOnlyList<LogEntry> Snapshot()
    {
        lock (Sync)
            return History.ToArray();
    }

    public static void Info(string message, [CallerMemberName] string member = "")
        => Write(LogLevel.Info, message, member);

    public static void Warn(string message, [CallerMemberName] string member = "")
        => Write(LogLevel.Warning, message, member);

    public static void Error(string message, [CallerMemberName] string member = "")
        => Write(LogLevel.Error, message, member);

    private static void Write(LogLevel level, string message, string member)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, member, message);
        lock (Sync)
        {
            History.Add(entry);
            if (History.Count > MaxHistory)
                History.RemoveRange(0, History.Count - MaxHistory);
        }

        var tag = level switch
        {
            LogLevel.Info => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERR ",
            _ => "LOG "
        };
        Console.WriteLine($"[{tag}] {entry.Timestamp:HH:mm:ss.fff} {member}: {message}");
        EntryWritten?.Invoke(entry);
    }
}

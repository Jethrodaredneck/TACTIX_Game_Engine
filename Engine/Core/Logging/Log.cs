using System.Runtime.CompilerServices;

namespace TACTIX.Engine.Core.Logging;

public static class Log
{
    public static void Info(string message, [CallerMemberName] string member = "")
        => Console.WriteLine($"[INFO] {DateTimeOffset.Now:HH:mm:ss.fff} {member}: {message}");

    public static void Warn(string message, [CallerMemberName] string member = "")
        => Console.WriteLine($"[WARN] {DateTimeOffset.Now:HH:mm:ss.fff} {member}: {message}");

    public static void Error(string message, [CallerMemberName] string member = "")
        => Console.WriteLine($"[ERR ] {DateTimeOffset.Now:HH:mm:ss.fff} {member}: {message}");
}
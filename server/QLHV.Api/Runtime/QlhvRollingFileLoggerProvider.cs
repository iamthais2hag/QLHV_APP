using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace QLHV.Api.Runtime;

internal sealed class QlhvRollingFileLoggerProvider : ILoggerProvider
{
    private const string FilePattern = "qlhv-api-*.log";

    private static readonly Regex SensitiveMarker = new(
        """(?is)(?:Server|Data\s+Source|Initial\s+Catalog|Database)\s*=|(?:PasswordHash|Password|Pwd|User\s*Id|Uid|Cookie|Set-Cookie|Authorization|Operations(?:\s+|_)?Key|ConnectionString)\s*[:=]|"(?:PasswordHash|Password|Pwd|Cookie|Set-Cookie|Authorization|OperationsKey|ConnectionString)"\s*:""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly long _maxFileSizeBytes;
    private readonly int _retainedFileCount;
    private DateOnly _currentDate;
    private int _currentSegment;
    private string? _currentPath;
    private bool _disposed;

    public QlhvRollingFileLoggerProvider(
        string directory,
        long maxFileSizeBytes,
        int retainedFileCount)
    {
        _directory = Path.GetFullPath(directory);
        _maxFileSizeBytes = Math.Clamp(maxFileSizeBytes, 64 * 1024, 100 * 1024 * 1024);
        _retainedFileCount = Math.Clamp(retainedFileCount, 2, 100);
    }

    public ILogger CreateLogger(string categoryName) => new RollingLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        if (level == LogLevel.None)
        {
            return;
        }

        var safeCategory = Sanitize(category);
        var safeMessage = Sanitize(message);
        var exceptionSuffix = exception is null
            ? string.Empty
            : $" ExceptionType={exception.GetType().Name}";
        var now = DateTimeOffset.Now;
        var line = $"{now:O} [{level}] {safeCategory} {safeMessage}{exceptionSuffix}{Environment.NewLine}";
        var byteCount = Encoding.UTF8.GetByteCount(line);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(_directory);
                SelectFile(now, byteCount);
                File.AppendAllText(_currentPath!, line, Encoding.UTF8);
                PruneOldFiles();
            }
            catch
            {
                // File logging must never break startup or echo a potentially sensitive payload.
            }
        }
    }

    internal static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        try
        {
            if (SensitiveMarker.IsMatch(value))
            {
                return "[REDACTED SENSITIVE LOG MESSAGE]";
            }

            return value;
        }
        catch (RegexMatchTimeoutException)
        {
            return "[REDACTED LOG MESSAGE]";
        }
    }

    private void SelectFile(DateTimeOffset now, int nextByteCount)
    {
        var date = DateOnly.FromDateTime(now.LocalDateTime);
        if (_currentPath is null || date != _currentDate)
        {
            _currentDate = date;
            _currentSegment = FindLatestSegment(date);
            _currentPath = BuildPath(date, _currentSegment);
            PruneOldFiles();
        }

        var currentLength = File.Exists(_currentPath)
            ? new FileInfo(_currentPath).Length
            : 0L;
        if (currentLength > 0 && currentLength + nextByteCount > _maxFileSizeBytes)
        {
            _currentSegment++;
            _currentPath = BuildPath(date, _currentSegment);
            PruneOldFiles();
        }
    }

    private int FindLatestSegment(DateOnly date)
    {
        var prefix = $"qlhv-api-{date:yyyyMMdd}-";
        var latest = 0;
        foreach (var path in Directory.EnumerateFiles(_directory, $"{prefix}*.log", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var suffix = name[prefix.Length..];
            if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var segment))
            {
                latest = Math.Max(latest, segment);
            }
        }

        return latest;
    }

    private string BuildPath(DateOnly date, int segment) =>
        Path.Combine(_directory, $"qlhv-api-{date:yyyyMMdd}-{segment:D2}.log");

    private void PruneOldFiles()
    {
        var files = Directory.EnumerateFiles(_directory, FilePattern, SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(_retainedFileCount)
            .ToArray();
        foreach (var file in files)
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // Retention is best effort and never broadens beyond the exact QLHV log pattern.
            }
        }
    }

    private sealed class RollingLogger : ILogger
    {
        private readonly QlhvRollingFileLoggerProvider _provider;
        private readonly string _category;

        public RollingLogger(QlhvRollingFileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message;
            try
            {
                message = formatter(state, exception);
            }
            catch
            {
                message = "Log message formatting failed.";
            }

            _provider.Write(logLevel, _category, message, exception);
        }
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using QLHV.Application.Runtime;

namespace QLHV.Infrastructure.Runtime;

public sealed class TimeAuthorityService : ITimeAuthorityService
{
    private static readonly TimeSpan DiagnosticsTimeout = TimeSpan.FromSeconds(3);
    private readonly IDatabaseTimeAuthorityProbe _databaseProbe;
    private readonly TimeProvider _timeProvider;

    public TimeAuthorityService(
        IDatabaseTimeAuthorityProbe databaseProbe,
        TimeProvider timeProvider)
    {
        _databaseProbe = databaseProbe;
        _timeProvider = timeProvider;
    }

    public async Task<TimeHealthDto> GetHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var writeAuthorization = await GetWriteAuthorizationAsync(cancellationToken);
        var windows = await ReadWindowsTimeAsync(cancellationToken);
        var observation = CreateObservation(writeAuthorization, windows);
        return TimeAuthorityPolicy.Evaluate(observation);
    }

    /// <summary>
    /// Mutation path. Deliberately performs only SELECT SYSUTCDATETIME(); and
    /// never waits for W32Time, NTP, durable, audit, or realtime-history data.
    /// </summary>
    public async Task<TimeHealthDto> GetWriteAuthorizationAsync(
        CancellationToken cancellationToken = default)
    {
        var apiAtStart = _timeProvider.GetUtcNow();
        var monotonicStart = _timeProvider.GetTimestamp();
        var databaseUtc = await _databaseProbe.ReadDatabaseUtcAsync(cancellationToken);
        var monotonicDuration = _timeProvider.GetElapsedTime(monotonicStart);
        var apiAfterQuery = _timeProvider.GetUtcNow();
        var observation = new TimeAuthorityObservation(
            apiAtStart,
            apiAfterQuery,
            databaseUtc,
            monotonicDuration,
            TimeZoneInfo.Local.Id,
            WindowsTimeRunning: false,
            ConfiguredPeer: null,
            CurrentSource: null,
            TimeSinceLastGoodSync: null,
            NtpPhaseOffsetMilliseconds: null,
            LastSyncError: null);
        return TimeAuthorityPolicy.Evaluate(observation);
    }

    private static TimeAuthorityObservation CreateObservation(
        TimeHealthDto authorization,
        WindowsTimeSnapshot windows)
    {
        var duration = TimeSpan.FromMilliseconds(
            Math.Max(0, authorization.DatabaseUtcQueryMilliseconds));
        var end = authorization.ServerUtcNow;
        return new TimeAuthorityObservation(
            end.Subtract(duration),
            end,
            authorization.DatabaseUtcNow,
            duration,
            authorization.TimeZone,
            windows.Running,
            windows.ConfiguredPeer,
            windows.CurrentSource,
            windows.TimeSinceLastGoodSync,
            windows.PhaseOffsetMilliseconds,
            windows.LastSyncError)
        {
            EffectivePollInterval = windows.EffectivePollInterval,
        };
    }

    private static async Task<WindowsTimeSnapshot> ReadWindowsTimeAsync(
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return WindowsTimeSnapshot.Unavailable;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DiagnosticsTimeout);
        try
        {
            var service = await RunProcessAsync(
                "sc.exe",
                "query W32Time",
                timeout.Token);
            var running = service.ExitCode == 0 &&
                Regex.IsMatch(
                    service.Output,
                    @"(?im)^\s*STATE\s*:\s*\d+\s+RUNNING\s*$",
                    RegexOptions.CultureInvariant);
            var status = await RunProcessAsync(
                "w32tm.exe",
                "/query /status /verbose",
                timeout.Token);
            if (status.ExitCode != 0)
            {
                var configuredPeer = ReadConfiguredPeer();
                return new WindowsTimeSnapshot(
                    running,
                    configuredPeer,
                    null,
                    null,
                    null,
                    null,
                    ReadEffectivePollInterval(configuredPeer, string.Empty));
            }

            var peer = ReadConfiguredPeer();
            return new WindowsTimeSnapshot(
                Running: running,
                ConfiguredPeer: peer,
                CurrentSource: ReadLine(status.Output, "Source"),
                TimeSinceLastGoodSync: ReadSeconds(
                    status.Output, "Time since Last Good Sync Time"),
                PhaseOffsetMilliseconds: ReadSeconds(
                    status.Output, "Phase Offset") is { } offset
                    ? offset.TotalMilliseconds
                    : null,
                LastSyncError: ReadLeadingInteger(
                    status.Output, "Last Sync Error"),
                EffectivePollInterval: ReadEffectivePollInterval(
                    peer,
                    status.Output));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return WindowsTimeSnapshot.Unavailable;
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start);
        if (process is null)
        {
            return new ProcessResult(-1, string.Empty);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        _ = await errorTask;
        return new ProcessResult(process.ExitCode, output);
    }

    private static string? ReadConfiguredPeer()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\W32Time\Parameters",
                "NtpServer",
                null) as string;
        }
        catch
        {
            return null;
        }
    }

    private static TimeSpan? ReadEffectivePollInterval(
        string? configuredPeer,
        string statusOutput)
    {
        if (string.IsNullOrWhiteSpace(configuredPeer))
        {
            return null;
        }

        var flags = Regex.Match(
            configuredPeer,
            @",0x(?<flags>[0-9a-f]+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (flags.Success &&
            int.TryParse(
                flags.Groups["flags"].Value,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var parsedFlags) &&
            (parsedFlags & 0x1) != 0)
        {
            var special = ReadRegistryInteger(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\W32Time\TimeProviders\NtpClient",
                "SpecialPollInterval");
            return special is > 0
                ? TimeSpan.FromSeconds(special.Value)
                : null;
        }

        var exponent = ReadLeadingInteger(statusOutput, "Poll Interval");
        return exponent is >= 0 and <= 30
            ? TimeSpan.FromSeconds(Math.Pow(2, exponent.Value))
            : null;
    }

    private static int? ReadRegistryInteger(string path, string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return Registry.GetValue(path, name, null) switch
            {
                int value => value,
                long value when value is > 0 and <= int.MaxValue => (int)value,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadLine(string output, string label)
    {
        var match = Regex.Match(
            output,
            $"(?im)^{Regex.Escape(label)}:\\s*(?<value>[^\\r\\n]{{1,200}})\\s*$",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static TimeSpan? ReadSeconds(string output, string label)
    {
        var value = ReadLine(output, label);
        if (value is null)
        {
            return null;
        }

        var match = Regex.Match(
            value,
            @"(?<seconds>[+-]?\d+(?:\.\d+)?)s",
            RegexOptions.CultureInvariant);
        return match.Success &&
               double.TryParse(
                   match.Groups["seconds"].Value,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static int? ReadLeadingInteger(string output, string label)
    {
        var value = ReadLine(output, label);
        if (value is null)
        {
            return null;
        }

        var match = Regex.Match(
            value,
            @"^\s*(?<value>\d+)",
            RegexOptions.CultureInvariant);
        return match.Success &&
               int.TryParse(
                   match.Groups["value"].Value,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out var parsed)
            ? parsed
            : null;
    }

    private sealed record WindowsTimeSnapshot(
        bool Running,
        string? ConfiguredPeer,
        string? CurrentSource,
        TimeSpan? TimeSinceLastGoodSync,
        double? PhaseOffsetMilliseconds,
        int? LastSyncError,
        TimeSpan? EffectivePollInterval)
    {
        public static readonly WindowsTimeSnapshot Unavailable =
            new(false, null, null, null, null, null, null);
    }

    private sealed record ProcessResult(int ExitCode, string Output);

}

public sealed class TimeAuthorityClockMonitor
{
    private readonly object _gate = new();
    private bool _hasObservation;
    private DateTimeOffset _lastWallUtc;
    private long _lastTimestamp;

    public TimeAuthorityClockProgress Observe(
        DateTimeOffset wallUtc,
        long timestamp,
        TimeProvider timeProvider)
    {
        lock (_gate)
        {
            TimeAuthorityClockProgress result;
            if (_hasObservation)
            {
                result = new TimeAuthorityClockProgress(
                    wallUtc - _lastWallUtc,
                    timeProvider.GetElapsedTime(_lastTimestamp, timestamp));
            }
            else
            {
                result = new TimeAuthorityClockProgress(null, null);
                _hasObservation = true;
            }

            _lastWallUtc = wallUtc;
            _lastTimestamp = timestamp;
            return result;
        }
    }
}

public sealed record TimeAuthorityClockProgress(
    TimeSpan? WallElapsed,
    TimeSpan? MonotonicElapsed);

public sealed class TimeAuthorityNtpProbeMonitor
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _host;
    private DateTimeOffset? _lastProbeAtUtc;
    private bool? _lastProbeSucceeded;
    private int _consecutiveFailures;

    public async Task<TimeAuthorityNtpProbeSnapshot> ObserveAsync(
        string? host,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return TimeAuthorityNtpProbeSnapshot.Unavailable;
        }

        var now = timeProvider.GetUtcNow();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(_host, host, StringComparison.OrdinalIgnoreCase) &&
                _lastProbeAtUtc is { } cachedAt &&
                now - cachedAt < CacheDuration)
            {
                return Snapshot();
            }

            var succeeded = await ProbeAsync(host, cancellationToken);
            _host = host;
            _lastProbeAtUtc = timeProvider.GetUtcNow();
            _lastProbeSucceeded = succeeded;
            _consecutiveFailures = succeeded
                ? 0
                : checked(_consecutiveFailures + 1);
            return Snapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    private TimeAuthorityNtpProbeSnapshot Snapshot() => new(
        _lastProbeSucceeded,
        _consecutiveFailures,
        _lastProbeAtUtc);

    private static async Task<bool> ProbeAsync(
        string host,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
            var address = addresses.FirstOrDefault(candidate =>
                candidate.AddressFamily == AddressFamily.InterNetwork) ??
                addresses.FirstOrDefault();
            if (address is null)
            {
                return false;
            }

            using var client = new UdpClient(address.AddressFamily);
            client.Connect(new IPEndPoint(address, 123));
            var request = new byte[48];
            request[0] = 0x1B;
            _ = await client.SendAsync(request, timeout.Token);
            var response = await client.ReceiveAsync(timeout.Token);
            return response.Buffer.Length >= 48;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}

public sealed record TimeAuthorityNtpProbeSnapshot(
    bool? LastProbeSucceeded,
    int ConsecutiveFailures,
    DateTimeOffset? LastProbeAtUtc)
{
    public static readonly TimeAuthorityNtpProbeSnapshot Unavailable =
        new(null, 0, null);
}

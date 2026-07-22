using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QLHV.Application.Runtime;

namespace QLHV.Infrastructure.Runtime;

public sealed class RuntimeReadinessService : IRuntimeReadinessService
{
    public const string ExpectedDatabaseName = "QLHV_APP";

    private readonly RuntimeConfigurationState _configurationState;
    private readonly IRuntimeReadinessProbe _probe;
    private readonly IHostEnvironment _environment;
    private readonly QlhvRuntimeOptions _options;
    private readonly RuntimeReadinessCache _cache;

    public RuntimeReadinessService(
        RuntimeConfigurationState configurationState,
        IRuntimeReadinessProbe probe,
        IHostEnvironment environment,
        IOptions<QlhvRuntimeOptions>? options = null,
        RuntimeReadinessCache? cache = null)
    {
        _configurationState = configurationState;
        _probe = probe;
        _environment = environment;
        _options = options?.Value ?? new QlhvRuntimeOptions();
        _cache = cache ?? new RuntimeReadinessCache();
    }

    public async Task<RuntimeStatusDto> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGet(out var cached))
        {
            return cached;
        }

        await _cache.Gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGet(out cached))
            {
                return cached;
            }

            var status = await ComputeStatusAsync(cancellationToken);
            _cache.Set(status, TimeSpan.FromSeconds(Math.Clamp(_options.ReadinessCacheSeconds, 1, 30)));
            return status;
        }
        finally
        {
            _cache.Gate.Release();
        }
    }

    private async Task<RuntimeStatusDto> ComputeStatusAsync(CancellationToken cancellationToken)
    {
        RuntimeReadinessProbeResult probe;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            Math.Clamp(_options.ReadinessOverallTimeoutSeconds, 5, 60)));
        try
        {
            probe = await _probe.ProbeAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            probe = RuntimeReadinessProbeResult.Unavailable(
                "Kiểm tra sẵn sàng vượt quá thời gian cho phép.");
        }
        catch
        {
            probe = RuntimeReadinessProbeResult.Unavailable(
                "Không thể hoàn tất kiểm tra sẵn sàng của hệ thống.");
        }

        var messages = new List<string>();
        if (!_configurationState.IsReady)
        {
            messages.Add(
                $"Thiếu hoặc sai cấu hình QLHV_APP. Kiểm tra: {_configurationState.Path}");
        }

        messages.AddRange(probe.Messages.Where(message => !string.IsNullOrWhiteSpace(message)));

        var correctDatabase = probe.DatabaseConnected &&
            string.Equals(
                probe.DatabaseName,
                ExpectedDatabaseName,
                StringComparison.OrdinalIgnoreCase);
        if (probe.DatabaseConnected && !correctDatabase)
        {
            messages.Add("Database hiện tại không phải QLHV_APP.");
        }

        var ready = _configurationState.IsReady &&
            correctDatabase &&
            probe.RequiredSchemaReady &&
            probe.AuthenticationReady &&
            probe.BackupProfilesReady &&
            probe.BackupStorageReady &&
            probe.FileStorageReady &&
            probe.RuntimeStorageReady;

        if (ready && messages.Count == 0)
        {
            messages.Add("Hệ thống sẵn sàng.");
        }

        return new RuntimeStatusDto
        {
            IsReady = ready,
            Version = GetVersion(),
            Environment = _environment.EnvironmentName,
            ConfigurationReady = _configurationState.IsReady,
            DatabaseConnected = probe.DatabaseConnected,
            DatabaseName = probe.DatabaseConnected ? probe.DatabaseName : null,
            AuthenticationReady = probe.AuthenticationReady,
            RequiredSchemaReady = probe.RequiredSchemaReady,
            BackupProfilesReady = probe.BackupProfilesReady,
            BackupStorageReady = probe.BackupStorageReady,
            FileStorageReady = probe.FileStorageReady,
            RuntimeStorageReady = probe.RuntimeStorageReady,
            CheckedAtUtc = DateTime.UtcNow,
            Messages = messages.Distinct(StringComparer.Ordinal).ToArray(),
        };
    }

    private static string GetVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(RuntimeReadinessService).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                   ?.InformationalVersion
                   ?.Split('+', 2)[0]
               ?? assembly.GetName().Version?.ToString()
               ?? "unknown";
    }
}

public sealed class RuntimeReadinessCache
{
    private readonly object _gate = new();
    private RuntimeStatusDto? _status;
    private DateTimeOffset _expiresAtUtc;

    internal SemaphoreSlim Gate { get; } = new(1, 1);

    internal bool TryGet(out RuntimeStatusDto status)
    {
        lock (_gate)
        {
            if (_status is not null && DateTimeOffset.UtcNow < _expiresAtUtc)
            {
                status = _status;
                return true;
            }
        }

        status = null!;
        return false;
    }

    internal void Set(RuntimeStatusDto status, TimeSpan duration)
    {
        lock (_gate)
        {
            _status = status;
            _expiresAtUtc = DateTimeOffset.UtcNow.Add(duration);
        }
    }
}

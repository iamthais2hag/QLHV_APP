using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QLHV.Application.Runtime;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Runtime;

public sealed class RuntimeReadinessService : IRuntimeReadinessService
{
    public const string ExpectedDatabaseName = "QLHV_APP";

    private readonly RuntimeConfigurationState _configurationState;
    private readonly IRuntimeReadinessProbe _probe;
    private readonly IHostEnvironment _environment;
    private readonly QlhvRuntimeOptions _options;
    private readonly RuntimeReadinessCache _cache;
    private readonly IRuntimeBuildIdentity? _buildIdentity;
    private readonly IQlhvAutoSyncPollingState? _pollingState;
    private readonly QlhvAutoSyncOptions _autoSyncOptions;
    private readonly ITimeAuthorityService? _timeAuthority;
    private readonly TimeProvider _timeProvider;

    public RuntimeReadinessService(
        RuntimeConfigurationState configurationState,
        IRuntimeReadinessProbe probe,
        IHostEnvironment environment,
        IOptions<QlhvRuntimeOptions>? options = null,
        RuntimeReadinessCache? cache = null,
        IRuntimeBuildIdentity? buildIdentity = null,
        IQlhvAutoSyncPollingState? pollingState = null,
        IOptions<QlhvAutoSyncOptions>? autoSyncOptions = null,
        ITimeAuthorityService? timeAuthority = null,
        TimeProvider? timeProvider = null)
    {
        _configurationState = configurationState;
        _probe = probe;
        _environment = environment;
        _options = options?.Value ?? new QlhvRuntimeOptions();
        _cache = cache ?? new RuntimeReadinessCache();
        _buildIdentity = buildIdentity;
        _pollingState = pollingState;
        _autoSyncOptions = autoSyncOptions?.Value ?? new QlhvAutoSyncOptions();
        _timeAuthority = timeAuthority;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        TimeHealthDto timeHealth;
        try
        {
            timeHealth = _timeAuthority is null
                ? new TimeHealthDto
                {
                    TimeHealth = TimeHealthStatuses.Blocked,
                    ReasonCode = TimeHealthReasonCodes.EvaluationUnavailable,
                    WritesAllowed = false,
                    DatabaseClockAvailable = false,
                    ServerUtcNow = _timeProvider.GetUtcNow(),
                    EvaluatedAtUtc = _timeProvider.GetUtcNow(),
                    TimeZone = TimeZoneInfo.Local.Id,
                    Messages = ["Dịch vụ kiểm tra đồng bộ thời gian chưa được đăng ký."],
                }
                : await _timeAuthority.GetHealthAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            timeHealth = new TimeHealthDto
            {
                TimeHealth = TimeHealthStatuses.Blocked,
                ReasonCode = TimeHealthReasonCodes.EvaluationUnavailable,
                WritesAllowed = false,
                DatabaseClockAvailable = false,
                ServerUtcNow = _timeProvider.GetUtcNow(),
                EvaluatedAtUtc = _timeProvider.GetUtcNow(),
                TimeZone = TimeZoneInfo.Local.Id,
                Messages = ["Không thể hoàn tất kiểm tra đồng bộ thời gian."],
            };
        }
        messages.AddRange(timeHealth.Messages);

        var ready = _configurationState.IsReady &&
            correctDatabase &&
            probe.RequiredSchemaReady &&
            probe.AuthenticationReady &&
            probe.BackupProfilesReady &&
            probe.BackupStorageReady &&
            probe.FileStorageReady &&
            probe.RuntimeStorageReady &&
            TimeAuthorityPolicy.IsMutationAllowed(timeHealth);

        if (ready && messages.Count == 0)
        {
            messages.Add("Hệ thống sẵn sàng.");
        }

        return new RuntimeStatusDto
        {
            IsReady = ready,
            Version = GetVersion(),
            Environment = _environment.EnvironmentName,
            Build = _buildIdentity?.Current ?? new RuntimeBuildIdentityDto
            {
                ApplicationVersion = GetVersion(),
            },
            AutoSyncPolling = _pollingState?.Snapshot ?? new QlhvAutoSyncPollingStatusDto
            {
                Enabled = false,
                DisabledReason = "Polling runtime state is unavailable.",
            },
            ResolvedAutoSyncSourceOrder =
                QlhvAutoSyncConstants.NormalizeSourceOrder(_autoSyncOptions.SourceOrder),
            AutoSyncApiWorkerConfigParity = true,
            TimeContractVersion = TimeHealthContract.Version,
            Time = timeHealth,
            ReviewedRetained = probe.ReviewedRetained,
            ConfigurationReady = _configurationState.IsReady,
            DatabaseConnected = probe.DatabaseConnected,
            DatabaseName = probe.DatabaseConnected ? probe.DatabaseName : null,
            AuthenticationReady = probe.AuthenticationReady,
            RequiredSchemaReady = probe.RequiredSchemaReady,
            BackupProfilesReady = probe.BackupProfilesReady,
            BackupStorageReady = probe.BackupStorageReady,
            FileStorageReady = probe.FileStorageReady,
            RuntimeStorageReady = probe.RuntimeStorageReady,
            CheckedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
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
    private readonly TimeProvider _timeProvider;
    private RuntimeStatusDto? _status;
    private long _setTimestamp;
    private TimeSpan _duration;

    public RuntimeReadinessCache(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal SemaphoreSlim Gate { get; } = new(1, 1);

    internal bool TryGet(out RuntimeStatusDto status)
    {
        lock (_gate)
        {
            if (_status is not null &&
                _timeProvider.GetElapsedTime(_setTimestamp) < _duration)
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
            _setTimestamp = _timeProvider.GetTimestamp();
            _duration = duration;
        }
    }
}

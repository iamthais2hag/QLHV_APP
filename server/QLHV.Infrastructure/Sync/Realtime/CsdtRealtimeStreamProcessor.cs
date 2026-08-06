using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync.Realtime;

namespace QLHV.Infrastructure.Sync.Realtime;

/// <summary>
/// Executes one durable command or one forward synchronization pass for a
/// single fixed route. Target commits always happen before state checkpoints.
/// </summary>
internal sealed class CsdtRealtimeStreamProcessor
{
    private readonly CsdtRealtimeStateRepository _state;
    private readonly CsdtRealtimeSourceReader _reader;
    private readonly CsdtRealtimeTargetWriter _writer;
    private readonly ICsdtReverseCommandExecutor _reverseExecutor;
    private readonly CsdtRealtimeSyncOptions _options;
    private readonly ILogger<CsdtRealtimeStreamProcessor> _logger;

    public CsdtRealtimeStreamProcessor(
        CsdtRealtimeStateRepository state,
        CsdtRealtimeSourceReader reader,
        CsdtRealtimeTargetWriter writer,
        ICsdtReverseCommandExecutor reverseExecutor,
        IOptions<CsdtRealtimeSyncOptions> options,
        ILogger<CsdtRealtimeStreamProcessor> logger)
    {
        _state = state;
        _reader = reader;
        _writer = writer;
        _reverseExecutor = reverseExecutor;
        _options = options.Value;
        _logger = logger;
    }

    internal async Task ProcessOnceAsync(
        CsdtRealtimeRouteDefinition route,
        CsdtRealtimeResolvedRoute resolved,
        CancellationToken cancellationToken)
    {
        var runtime = await _state.GetRuntimeStreamAsync(route.StreamCode, cancellationToken);
        var command = await _state.ClaimNextCommandAsync(route.StreamCode, cancellationToken);
        if (command is not null)
        {
            await ProcessCommandAsync(route, resolved, runtime, command, cancellationToken);
            return;
        }

        if (!runtime.IsEnabled ||
            !string.Equals(runtime.BaselineStatus, "COMPLETED", StringComparison.Ordinal) ||
            runtime.NextRetryAtUtc > DateTimeOffset.UtcNow)
        {
            return;
        }

        var domains = await _state.GetRuntimeDomainsAsync(runtime.StreamId, cancellationToken);
        if (await RequiresSafeRebaselineAsync(resolved, domains, cancellationToken))
        {
            await RunBaselineAsync(
                route,
                resolved,
                runtime,
                command: null,
                actor: "worker:expired-checkpoint",
                cancellationToken,
                inferMissingIdentities: true);
            await RunIncrementalIfNeededAsync(
                route,
                resolved,
                runtime.StreamId,
                "worker:catch-up",
                cancellationToken);
            return;
        }

        var reconcileDue = !runtime.LastReconciledAtUtc.HasValue ||
                           runtime.LastReconciledAtUtc.Value.AddMinutes(
                               _options.ReconcileIntervalMinutes) <= DateTimeOffset.UtcNow;
        if (reconcileDue)
        {
            await RunForwardAsync(
                route,
                resolved,
                runtime,
                domains,
                "RECONCILE",
                "worker:reconcile",
                command: null,
                cancellationToken);
            return;
        }

        await RunIncrementalIfNeededAsync(
            route,
            resolved,
            runtime.StreamId,
            "worker:incremental",
            cancellationToken);
    }

    private async Task ProcessCommandAsync(
        CsdtRealtimeRouteDefinition route,
        CsdtRealtimeResolvedRoute resolved,
        CsdtRealtimeRuntimeStream runtime,
        CsdtRealtimeClaimedCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (command.CommandType)
            {
                case CsdtRealtimeCommandTypes.SetEnabled:
                {
                    var request = DeserializeRequest(command.RequestJson);
                    if (!request.Enabled.HasValue)
                    {
                        throw new InvalidOperationException("SET_ENABLED command is missing Enabled.");
                    }

                    await _state.CompleteSetEnabledCommandAsync(
                        command,
                        request.Enabled.Value,
                        cancellationToken);
                    return;
                }
                case CsdtRealtimeCommandTypes.Baseline:
                    await RunBaselineAsync(
                        route,
                        resolved,
                        runtime,
                        command,
                        command.RequestedBy,
                        cancellationToken);
                    await RunIncrementalIfNeededAsync(
                        route,
                        resolved,
                        runtime.StreamId,
                        "worker:baseline-catch-up",
                        cancellationToken);
                    return;
                case CsdtRealtimeCommandTypes.Retry:
                {
                    var domains = await _state.GetRuntimeDomainsAsync(
                        runtime.StreamId,
                        cancellationToken);
                    var baselineCompleted = string.Equals(
                        runtime.BaselineStatus,
                        "COMPLETED",
                        StringComparison.Ordinal);
                    var checkpointExpired = baselineCompleted &&
                        await RequiresSafeRebaselineAsync(resolved, domains, cancellationToken);
                    if (!baselineCompleted || checkpointExpired)
                    {
                        await RunBaselineAsync(
                            route,
                            resolved,
                            runtime,
                            command,
                            command.RequestedBy,
                            cancellationToken,
                            inferMissingIdentities: checkpointExpired);
                    }
                    else
                    {
                        await RunForwardAsync(
                            route,
                            resolved,
                            runtime,
                            domains,
                            "RETRY",
                            command.RequestedBy,
                            command,
                            cancellationToken);
                    }

                    await RunIncrementalIfNeededAsync(
                        route,
                        resolved,
                        runtime.StreamId,
                        "worker:retry-catch-up",
                        cancellationToken);
                    return;
                }
                case CsdtRealtimeCommandTypes.ReverseExecute:
                    await RunReverseAsync(route, runtime, command, cancellationToken);
                    return;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported realtime command type {command.CommandType}.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await _state.FailCommandWithoutRunAsync(
                command,
                exception,
                CancellationToken.None);
            throw;
        }
    }

    private async Task RunBaselineAsync(
        CsdtRealtimeRouteDefinition route,
        CsdtRealtimeResolvedRoute resolved,
        CsdtRealtimeRuntimeStream runtime,
        CsdtRealtimeClaimedCommand? command,
        string actor,
        CancellationToken cancellationToken,
        bool inferMissingIdentities = false)
    {
        var domains = await _state.GetRuntimeDomainsAsync(runtime.StreamId, cancellationToken);
        await RunForwardAsync(
            route,
            resolved,
            runtime,
            domains,
            "BASELINE",
            actor,
            command,
            cancellationToken,
            inferMissingIdentities);
    }

    private async Task RunIncrementalIfNeededAsync(
        CsdtRealtimeRouteDefinition route,
        CsdtRealtimeResolvedRoute resolved,
        long streamId,
        string actor,
        CancellationToken cancellationToken)
    {
        var runtime = await _state.GetRuntimeStreamAsync(route.StreamCode, cancellationToken);
        if (!runtime.IsEnabled ||
            !string.Equals(runtime.BaselineStatus, "COMPLETED", StringComparison.Ordinal))
        {
            return;
        }

        var currentVersion = await _reader.GetCurrentVersionAsync(
            resolved.SourceConnectionString,
            cancellationToken);
        var domains = await _state.GetRuntimeDomainsAsync(streamId, cancellationToken);
        var nowUtc = DateTimeOffset.UtcNow;
        if (!domains.Any(domain => CsdtRealtimeOptionalDomainRetryPolicy.HasWorkDue(
                domain, currentVersion, nowUtc)))
        {
            return;
        }

        await RunForwardAsync(
            route,
            resolved,
            runtime,
            domains,
            "INCREMENTAL",
            actor,
            command: null,
            cancellationToken);
    }

    private async Task RunForwardAsync(
        CsdtRealtimeRouteDefinition route,
        CsdtRealtimeResolvedRoute resolved,
        CsdtRealtimeRuntimeStream runtime,
        IReadOnlyList<CsdtRealtimeRuntimeDomain> runtimeDomains,
        string runType,
        string actor,
        CsdtRealtimeClaimedCommand? command,
        CancellationToken cancellationToken,
        bool inferMissingIdentities = false)
    {
        var currentVersion = await _reader.GetCurrentVersionAsync(
            resolved.SourceConnectionString,
            cancellationToken);
        var fromVersion = runType == "BASELINE"
            ? null
            : runtimeDomains
                .Where(domain => !domain.IsOptional)
                .Min(domain => domain.LastSuccessfulVersion);
        var run = await _state.TryStartRunAsync(
            runtime.StreamId,
            runType,
            actor,
            fromVersion,
            currentVersion,
            command?.CommandId,
            cancellationToken);
        if (run is null)
        {
            throw new InvalidOperationException("The realtime stream already has an active run.");
        }

        var definitions = CsdtRealtimeDomainCatalog.Ordered
            .ToDictionary(domain => domain.Name, StringComparer.Ordinal);
        var mandatoryFailed = false;
        var optionalFailed = false;
        var streamMinimumValidVersion = long.MaxValue;
        try
        {
            foreach (var domainState in runtimeDomains)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!definitions.TryGetValue(domainState.DomainCode, out var domain))
                {
                    throw new CsdtRealtimeSchemaException(
                        $"Domain {domainState.DomainCode} is outside the server catalog.");
                }

                var currentDomainState = domainState;
                var minimumValidVersion = 0L;
                try
                {
                    var retryDue = false;
                    if (domainState.IsOptional)
                    {
                        var nowUtc = DateTimeOffset.UtcNow;
                        currentDomainState = await _state.GetRuntimeDomainAsync(
                            runtime.StreamId,
                            domain.Name,
                            cancellationToken);
                        if (CsdtRealtimeOptionalDomainRetryPolicy.ShouldDefer(
                                currentDomainState,
                                nowUtc))
                        {
                            optionalFailed = true;
                            continue;
                        }

                        retryDue = CsdtRealtimeOptionalDomainRetryPolicy.IsRetryDue(
                            currentDomainState,
                            nowUtc);
                    }

                    minimumValidVersion = await _reader.GetMinimumValidVersionAsync(
                        resolved.SourceConnectionString,
                        domain,
                        cancellationToken);
                    if (!currentDomainState.IsOptional)
                    {
                        streamMinimumValidVersion = Math.Min(
                            streamMinimumValidVersion,
                            minimumValidVersion);
                    }
                    var domainFromVersion = runType == "BASELINE"
                        ? null
                        : currentDomainState.LastSuccessfulVersion;
                    if (retryDue &&
                        string.Equals(
                            currentDomainState.DomainStatus,
                            "SKIPPED",
                            StringComparison.Ordinal))
                    {
                        domainFromVersion = null;
                    }

                    if (runType != "BASELINE" &&
                        (!domainFromVersion.HasValue ||
                         domainFromVersion.Value < minimumValidVersion))
                    {
                        if (!currentDomainState.IsOptional)
                        {
                            throw new CsdtRealtimeCheckpointExpiredException(domain.Name);
                        }

                        await _state.BeginDomainAsync(
                            run,
                            domain,
                            null,
                            currentVersion,
                            minimumValidVersion,
                            cancellationToken);
                        var optionalSnapshot = await _reader.ReadForwardPartitionSnapshotAsync(
                            resolved.SourceConnectionString,
                            domain,
                            route.MaCSDT,
                            cancellationToken);
                        var optionalWrite = await WriteForwardAsync(
                            route,
                            resolved,
                            domain,
                            optionalSnapshot,
                            cancellationToken);
                        await _state.CompleteDomainAsync(
                            run,
                            domain,
                            optionalWrite,
                            currentVersion,
                            minimumValidVersion,
                            [],
                            cancellationToken,
                            inferMissingIdentities: true);
                        continue;
                    }

                    await _state.BeginDomainAsync(
                        run,
                        domain,
                        domainFromVersion,
                        currentVersion,
                        minimumValidVersion,
                        cancellationToken);

                    if (runType is "BASELINE" or "RECONCILE")
                    {
                        var snapshot = await _reader.ReadForwardPartitionSnapshotAsync(
                            resolved.SourceConnectionString,
                            domain,
                            route.MaCSDT,
                            cancellationToken);
                        var write = await WriteForwardAsync(
                            route,
                            resolved,
                            domain,
                            snapshot,
                            cancellationToken);
                        await _state.CompleteDomainAsync(
                            run,
                            domain,
                            write,
                            currentVersion,
                            minimumValidVersion,
                            [],
                            cancellationToken,
                            inferMissingIdentities:
                                runType == "BASELINE" && inferMissingIdentities);
                        continue;
                    }

                    var changes = await _reader.ReadChangesAsync(
                        resolved.SourceConnectionString,
                        domain,
                        domainFromVersion!.Value,
                        currentVersion,
                        route.MaCSDT,
                        cancellationToken);
                    var tombstones = await FindPartitionTombstonesAsync(
                        run.StreamId,
                        domain,
                        changes,
                        cancellationToken);
                    var snapshotForChanges = await _reader.ReadForwardChangedPartitionSnapshotAsync(
                        resolved.SourceConnectionString,
                        domain,
                        domainFromVersion.Value,
                        currentVersion,
                        route.MaCSDT,
                        cancellationToken);
                    if (snapshotForChanges.Rows.Rows.Count == 0)
                    {
                        await _state.CompleteCheckpointOnlyDomainAsync(
                            run,
                            domain,
                            currentVersion,
                            minimumValidVersion,
                            tombstones,
                            cancellationToken);
                    }
                    else
                    {
                        var write = await WriteForwardAsync(
                            route,
                            resolved,
                            domain,
                            snapshotForChanges,
                            cancellationToken);
                        await _state.CompleteDomainAsync(
                            run,
                            domain,
                            write,
                            currentVersion,
                            minimumValidVersion,
                            tombstones,
                            cancellationToken);
                    }
                }
                catch (Exception exception) when (
                    currentDomainState.IsOptional &&
                    exception is CsdtRealtimeSchemaException)
                {
                    await _state.SkipOptionalDomainAsync(
                        run,
                        domain,
                        currentVersion,
                        minimumValidVersion,
                        exception,
                        cancellationToken);
                    optionalFailed = true;
                    _logger.LogWarning(
                        exception,
                        "CSDT realtime optional domain {Domain} was skipped for {StreamCode} because its schema is unsupported.",
                        domain.Name,
                        route.StreamCode);
                }
                catch (Exception exception) when (
                    exception is not OperationCanceledException &&
                    exception is not CsdtRealtimeCheckpointExpiredException)
                {
                    await _state.FailDomainAsync(run, domain, exception, cancellationToken);
                    mandatoryFailed |= !currentDomainState.IsOptional;
                    optionalFailed |= currentDomainState.IsOptional;
                    _logger.LogWarning(
                        exception,
                        "CSDT realtime domain {Domain} failed for {StreamCode}.",
                        domain.Name,
                        route.StreamCode);
                }
            }

            await _state.CompleteRunAsync(
                run,
                command?.CommandId,
                mandatoryFailed,
                optionalFailed,
                currentVersion,
                streamMinimumValidVersion == long.MaxValue
                    ? 0
                    : streamMinimumValidVersion,
                cancellationToken);
        }
        catch (Exception exception)
        {
            await _state.FailRunAsync(
                run,
                command?.CommandId,
                exception,
                CancellationToken.None);
            throw;
        }
    }

    private async Task RunReverseAsync(
        CsdtRealtimeRouteDefinition route,
        CsdtRealtimeRuntimeStream runtime,
        CsdtRealtimeClaimedCommand command,
        CancellationToken cancellationToken)
    {
        var request = DeserializeRequest(command.RequestJson);
        if (string.IsNullOrWhiteSpace(request.ExpectedPlanToken))
        {
            throw new InvalidOperationException(
                "V1_TO_V2_EXECUTE command is missing ExpectedPlanToken.");
        }

        var run = await _state.TryStartRunAsync(
            runtime.StreamId,
            "REVERSE",
            command.RequestedBy,
            runtime.LastSuccessfulVersion,
            runtime.LastSuccessfulVersion,
            command.CommandId,
            cancellationToken);
        if (run is null)
        {
            throw new InvalidOperationException("The realtime stream already has an active run.");
        }

        try
        {
            var result = await _reverseExecutor.ExecuteAsync(
                new CsdtReverseExecutionContext(
                    run.RunId,
                    run.StreamId,
                    command.CommandId),
                route.Reverse(),
                request.MaKhoaHoc,
                request.ExpectedPlanToken,
                cancellationToken);
            try
            {
                await _state.CompleteReverseRunAsync(
                    run,
                    command.CommandId,
                    result,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await _state.MarkReverseCommitStateUnknownAsync(
                    run,
                    command.CommandId,
                    exception,
                    CancellationToken.None);
            }
        }
        catch (CsdtReverseAtomicWriteException exception)
        {
            await _state.FailAtomicReverseRunAsync(
                run,
                command.CommandId,
                exception,
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await _state.FailAtomicReverseRunAsync(
                run,
                command.CommandId,
                new CsdtReverseAtomicWriteException(
                    "<reverse-preflight>",
                    [],
                    [],
                    exception),
                CancellationToken.None);
        }
    }

    private async Task<CsdtRealtimeWriteResult> WriteForwardAsync(
        CsdtRealtimeRouteDefinition route,
        CsdtRealtimeResolvedRoute resolved,
        CsdtRealtimeDomainDefinition domain,
        CsdtRealtimeSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var result = await _writer.UpsertAsync(
            resolved.TargetConnectionString,
            snapshot,
            route.MaCSDT,
            cancellationToken);
        foreach (var group in result.Conflicts.GroupBy(
                     conflict => conflict.Code,
                     StringComparer.Ordinal))
        {
            var columns = group
                .SelectMany(conflict => conflict.Columns ?? [])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var identities = group
                .Take(5)
                .Select(conflict =>
                    "sha256:" + Convert.ToHexString(
                        CsdtRealtimeTargetWriter.HashKey(conflict.KeyJson)))
                .ToArray();
            _logger.LogInformation(
                "CSDT realtime ownership decision Stream={StreamCode} Domain={Domain} Reason={ReasonCode} Count={Count} Columns={Columns} Identities={Identities}.",
                route.StreamCode,
                domain.Name,
                group.Key,
                group.Count(),
                columns,
                identities);
        }

        return result;
    }

    private async Task<bool> RequiresSafeRebaselineAsync(
        CsdtRealtimeResolvedRoute resolved,
        IReadOnlyList<CsdtRealtimeRuntimeDomain> domains,
        CancellationToken cancellationToken)
    {
        var definitions = CsdtRealtimeDomainCatalog.Ordered
            .ToDictionary(domain => domain.Name, StringComparer.Ordinal);
        foreach (var domainState in domains.Where(domain => !domain.IsOptional))
        {
            if (!definitions.TryGetValue(domainState.DomainCode, out var domain) ||
                !domainState.LastSuccessfulVersion.HasValue)
            {
                return true;
            }

            var minimum = await _reader.GetMinimumValidVersionAsync(
                resolved.SourceConnectionString,
                domain,
                cancellationToken);
            if (domainState.LastSuccessfulVersion.Value < minimum)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<IReadOnlyList<CsdtRealtimeChange>> FindPartitionTombstonesAsync(
        long streamId,
        CsdtRealtimeDomainDefinition domain,
        IReadOnlyList<CsdtRealtimeChange> changes,
        CancellationToken cancellationToken)
    {
        var tombstones = new List<CsdtRealtimeChange>();
        foreach (var change in changes.Where(item => !item.CurrentRowIsInPartition))
        {
            if (await _state.EntityBelongsToStreamAsync(
                    streamId,
                    domain.Name,
                    change.KeyJson,
                    cancellationToken))
            {
                tombstones.Add(change);
            }
        }

        return tombstones;
    }

    private static CommandRequest DeserializeRequest(string? requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return new CommandRequest();
        }

        return JsonSerializer.Deserialize<CommandRequest>(
                   requestJson,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new CommandRequest();
    }

    private sealed record CommandRequest(
        bool? Enabled = null,
        string? MaKhoaHoc = null,
        string? ExpectedPlanToken = null);
}

internal sealed class CsdtRealtimeCheckpointExpiredException : InvalidOperationException
{
    internal CsdtRealtimeCheckpointExpiredException(string domain)
        : base($"Change Tracking checkpoint expired for domain {domain}.")
    {
    }
}

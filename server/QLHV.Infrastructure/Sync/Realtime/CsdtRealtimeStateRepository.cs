using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Realtime;

namespace QLHV.Infrastructure.Sync.Realtime;

internal sealed partial class CsdtRealtimeStateRepository :
    ICsdtRealtimeStateRepository,
    ICsdtRealtimeCommandRepository
{
    private readonly IConnectionSettingsProvider _connections;
    private readonly IReadOnlyDictionary<string, CsdtRealtimeRouteDefinition> _routesByStream;

    public CsdtRealtimeStateRepository(
        IConnectionSettingsProvider connections,
        IOptions<CsdtRealtimeSyncOptions> options)
    {
        _connections = connections;
        var realtimeOptions = options.Value;
        var validation = new CsdtRealtimeSyncOptionsValidator().Validate(
            Options.DefaultName,
            realtimeOptions);
        if (validation.Failed)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(CsdtRealtimeSyncOptions),
                validation.Failures);
        }

        _routesByStream = CsdtRealtimeStreamCatalog
            .GetConfiguredRoutes(realtimeOptions)
            .ToDictionary(route => route.StreamCode, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<CsdtRealtimeStreamStatusDto>> GetStreamsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenStateConnectionAsync(cancellationToken);
            var rows = (await connection.QueryAsync<StreamDomainRow>(new CommandDefinition(
                StreamStatusSql,
                commandTimeout: 30,
                cancellationToken: cancellationToken))).AsList();
            return rows.GroupBy(row => row.StreamId)
                .Select(group =>
                {
                    var row = group.First();
                    var route = ResolveStoredRoute(row);
                    return new CsdtRealtimeStreamStatusDto
                    {
                        StreamCode = row.StreamCode,
                        VehicleType = row.VehicleType,
                        SourceProfileCode = row.SourceProfileCode,
                        TargetProfileCode = row.TargetProfileCode,
                        SourceDatabaseName = route?.SourceDatabaseName ?? string.Empty,
                        TargetDatabaseName = route?.TargetDatabaseName ?? string.Empty,
                        MaCSDT = row.MaCSDT,
                        Enabled = row.IsEnabled,
                        State = row.StreamStatus,
                        BaselineStatus = row.BaselineStatus,
                        BaselineVersion = row.BaselineVersion,
                        LastSuccessfulVersion = row.LastSuccessfulVersion,
                        CurrentSourceVersion = row.CurrentSourceVersion,
                        MinimumValidVersion = row.MinimumValidVersion,
                        LagVersions = row.LagVersions,
                        ActiveRunId = row.ActiveRunId,
                        RetryCount = row.RetryCount,
                        NextRetryAtUtc = row.NextRetryAtUtc,
                        LastStartedAtUtc = row.LastStartedAtUtc,
                        LastCompletedAtUtc = row.LastCompletedAtUtc,
                        LastSuccessAtUtc = row.LastSuccessAtUtc,
                        InsertedRows = row.InsertedRows,
                        UpdatedRows = row.UpdatedRows,
                        SkippedRows = row.SkippedRows,
                        ErrorRows = row.ErrorRows,
                        DeleteTombstoneCount = row.DeleteTombstoneCount,
                        UnresolvedConflictCount = row.UnresolvedConflictCount,
                        LastError = row.LastErrorMessage,
                        StateToken = Convert.ToBase64String(row.RowVersion),
                        ActionBlockers = row.ActiveRunId.HasValue
                            ? ["Stream dang xu ly mot operation khac."]
                            : [],
                        Domains = group.Where(item => item.DomainCode is not null)
                            .OrderBy(item => item.DomainOrder)
                            .Select(item => new CsdtRealtimeDomainStatusDto
                            {
                                Domain = item.DomainCode!,
                                State = item.DomainStatus ?? "PENDING",
                                SourceRows = item.DomainSourceRows,
                                TargetRows = item.DomainTargetRows,
                                InsertedRows = item.DomainInsertedRows,
                                UpdatedRows = item.DomainUpdatedRows,
                                SkippedRows = item.DomainSkippedRows,
                                ErrorRows = item.DomainErrorRows,
                                LastError = item.DomainLastErrorMessage,
                            })
                            .ToArray(),
                    };
                })
                .OrderBy(item => item.StreamCode, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (IsStoreFailure(exception))
        {
            throw new CsdtRealtimeStoreUnavailableException(
                "Khong the doc realtime state trong QLHV_APP.",
                exception);
        }
    }

    public async Task<IReadOnlyList<CsdtRealtimeHistoryItemDto>> GetHistoryAsync(
        string streamCode,
        int take,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = GetConfiguredRoute(streamCode);
            await using var connection = await OpenStateConnectionAsync(cancellationToken);
            using var result = await connection.QueryMultipleAsync(new CommandDefinition(
                HistorySql,
                new { StreamCode = streamCode, Take = Math.Clamp(take, 1, 200) },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            var runs = (await result.ReadAsync<RunRow>()).AsList();
            var domains = (await result.ReadAsync<RunDomainRow>()).AsList()
                .GroupBy(item => item.RunId)
                .ToDictionary(group => group.Key, group => group.ToArray());
            return runs.Select(run => new CsdtRealtimeHistoryItemDto
            {
                RunId = run.RunId,
                StreamCode = streamCode,
                RunType = run.RunType,
                Status = run.RunStatus,
                FromVersion = run.FromVersion,
                ToVersion = run.ToVersion,
                StartedAtUtc = run.StartedAtUtc ?? run.CreatedAtUtc,
                CompletedAtUtc = run.CompletedAtUtc,
                InsertedRows = run.InsertedRows,
                UpdatedRows = run.UpdatedRows,
                SkippedRows = run.SkippedRows,
                ErrorRows = run.ErrorRows,
                Actor = run.Actor,
                ErrorMessage = run.ErrorMessage,
                CanRetry = string.Equals(run.RunType, "REVERSE", StringComparison.Ordinal) &&
                           (string.Equals(run.RunStatus, "FAILED", StringComparison.Ordinal) ||
                            (string.Equals(run.RunStatus, "PARTIAL", StringComparison.Ordinal) &&
                             string.Equals(
                                 run.ErrorCode,
                                 "REVERSE_COMMIT_STATE_UNKNOWN",
                                 StringComparison.Ordinal))),
                Domains = domains.GetValueOrDefault(run.RunId, [])
                    .Select(item => new CsdtRealtimeRunDomainDto
                    {
                        Domain = item.DomainCode,
                        State = item.DomainStatus,
                        AttemptCount = item.AttemptCount,
                        LastAttemptAtUtc = item.LastAttemptAtUtc,
                        SucceededAtUtc = item.SucceededAtUtc,
                        InsertedRows = item.InsertedRows,
                        UpdatedRows = item.UpdatedRows,
                        SkippedRows = item.SkippedRows,
                        ErrorRows = item.ErrorRows,
                        Message = item.ErrorMessage,
                    })
                    .ToArray(),
            }).ToArray();
        }
        catch (Exception exception) when (IsStoreFailure(exception))
        {
            throw new CsdtRealtimeStoreUnavailableException(
                "Khong the doc realtime history trong QLHV_APP.",
                exception);
        }
    }

    public async Task<IReadOnlyList<CsdtRealtimeTombstoneDto>> GetTombstonesAsync(
        string streamCode,
        int take,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = GetConfiguredRoute(streamCode);
            await using var connection = await OpenStateConnectionAsync(cancellationToken);
            var rows = await connection.QueryAsync<TombstoneRow>(new CommandDefinition(
                TombstoneSql,
                new { StreamCode = streamCode, Take = Math.Clamp(take, 1, 200) },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            return rows.Select(row => new CsdtRealtimeTombstoneDto
            {
                Id = row.TombstoneId,
                StreamCode = streamCode,
                Domain = row.DomainCode,
                SourceKey = row.EntityKey,
                ChangeVersion = row.SourceVersion,
                DetectedAtUtc = row.FirstSeenAtUtc,
                Status = row.TombstoneStatus,
                Message = row.Note,
            }).ToArray();
        }
        catch (Exception exception) when (IsStoreFailure(exception))
        {
            throw new CsdtRealtimeStoreUnavailableException(
                "Khong the doc realtime tombstone trong QLHV_APP.",
                exception);
        }
    }

    public async Task<CsdtRealtimeActionResultDto> EnqueueAsync(
        CsdtRealtimeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var route = ValidateCommandRoute(command);
        var requestJson = JsonSerializer.Serialize(new
        {
            command.Enabled,
            command.MaKhoaHoc,
            command.ExpectedPlanToken,
        });

        try
        {
            await using var connection = await OpenStateConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            try
            {
                var state = await connection.QuerySingleOrDefaultAsync<CommandStateRow>(
                    new CommandDefinition(
                        CommandStateSql,
                        new { command.StreamCode },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));
                var forwardRoute = command.CommandType == CsdtRealtimeCommandTypes.ReverseExecute
                    ? route.Reverse()
                    : route;
                if (state is null ||
                    !string.Equals(state.VehicleType, forwardRoute.VehicleType, StringComparison.Ordinal) ||
                    !string.Equals(state.SourceProfileCode, forwardRoute.SourceProfileCode, StringComparison.Ordinal) ||
                    !string.Equals(state.TargetProfileCode, forwardRoute.TargetProfileCode, StringComparison.Ordinal) ||
                    !string.Equals(state.MaCSDT, forwardRoute.MaCSDT, StringComparison.Ordinal))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Rejected("Realtime state khong khop fixed route.");
                }

                if (command.ExpectedStateToken is not null)
                {
                    byte[] expected;
                    try
                    {
                        expected = Convert.FromBase64String(command.ExpectedStateToken);
                    }
                    catch (FormatException)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Rejected("State token khong hop le.");
                    }

                    if (!CryptographicOperations.FixedTimeEquals(expected, state.RowVersion))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Rejected("State da thay doi; hay tai lai truoc khi thao tac.");
                    }
                }

                var active = await connection.QuerySingleOrDefaultAsync<ActiveCommandRow>(
                    new CommandDefinition(
                        ActiveCommandSql,
                        new { state.StreamId },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));
                if (active is not null)
                {
                    var same = IsSameActiveCommand(
                        active.CommandType,
                        active.RequestJson,
                        command.CommandType,
                        requestJson);
                    await transaction.CommitAsync(cancellationToken);
                    return same
                        ? new CsdtRealtimeActionResultDto
                        {
                            Accepted = true,
                            JoinedExisting = true,
                            RunId = active.RunId,
                            Status = CsdtRealtimeActionStatuses.JoinedExisting,
                            Message = "Da tham gia operation dang cho hoac dang chay.",
                        }
                        : Rejected("Stream dang co operation khac.");
                }

                var commandId = Guid.NewGuid();
                await connection.ExecuteAsync(new CommandDefinition(
                    InsertCommandSql,
                    new
                    {
                        CommandId = commandId,
                        state.StreamId,
                        command.CommandType,
                        command.RequestedBy,
                        ExpectedRowVersion = command.ExpectedStateToken is null
                            ? null
                            : state.RowVersion,
                        RequestJson = requestJson,
                    },
                    transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
                await transaction.CommitAsync(cancellationToken);
                return new CsdtRealtimeActionResultDto
                {
                    Accepted = true,
                    Status = CsdtRealtimeActionStatuses.Queued,
                    Message = "Operation da duoc xep hang ben vung cho Worker.",
                };
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch
                {
                    // Preserve the enqueue failure.
                }
                throw;
            }
        }
        catch (Exception exception) when (IsStoreFailure(exception))
        {
            throw new CsdtRealtimeStoreUnavailableException(
                "Khong the xep hang realtime command trong QLHV_APP.",
                exception);
        }
    }

    internal static bool IsSameActiveCommand(
        string activeCommandType,
        string? activeRequestJson,
        string requestedCommandType,
        string requestJson)
        => string.Equals(
               activeCommandType,
               requestedCommandType,
               StringComparison.Ordinal) &&
           string.Equals(
               activeRequestJson ?? "{}",
               requestJson,
               StringComparison.Ordinal);

    public async Task<bool> HasRetryableReverseAsync(
        string streamCode,
        string? maKhoaHoc,
        string expectedPlanToken,
        CancellationToken cancellationToken = default)
    {
        _ = GetConfiguredRoute(streamCode);
        CsdtRealtimeIdentityRules.RequirePlanToken(
            expectedPlanToken,
            nameof(expectedPlanToken));

        try
        {
            await using var connection = await OpenStateConnectionAsync(cancellationToken);
            return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.App_CsdtRealtimeRun AS run
                    INNER JOIN dbo.App_CsdtRealtimeStream AS stream
                        ON stream.StreamId = run.StreamId
                    WHERE stream.StreamCode = @StreamCode
                      AND run.RunType = N'REVERSE'
                      AND
                      (
                          run.RunStatus = N'FAILED'
                          OR
                          (
                              run.RunStatus = N'PARTIAL'
                              AND run.ErrorCode = N'REVERSE_COMMIT_STATE_UNKNOWN'
                          )
                      )
                      AND JSON_VALUE(run.DetailJson, '$.planToken') = @ExpectedPlanToken
                      AND
                      (
                          (@MaKhoaHoc IS NULL AND JSON_VALUE(run.DetailJson, '$.maKhoaHoc') IS NULL)
                          OR JSON_VALUE(run.DetailJson, '$.maKhoaHoc') = @MaKhoaHoc
                      )
                      AND EXISTS
                      (
                          SELECT 1
                          FROM dbo.App_CsdtRealtimeRunDomain AS domainRun
                          WHERE domainRun.RunId = run.RunId
                      )
                ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;
                """,
                new
                {
                    StreamCode = streamCode,
                    MaKhoaHoc = maKhoaHoc,
                    ExpectedPlanToken = expectedPlanToken,
                },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        }
        catch (Exception exception) when (IsStoreFailure(exception))
        {
            throw new CsdtRealtimeStoreUnavailableException(
                "Khong the doc reverse recovery state trong QLHV_APP.",
                exception);
        }
    }

    internal async Task<SqlConnection> OpenStateConnectionAsync(CancellationToken cancellationToken)
    {
        var resolved = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!resolved.IsUsable || string.IsNullOrWhiteSpace(resolved.ConnectionString))
        {
            throw new CsdtRealtimeStoreUnavailableException(
                "QLHV_APP connection chua san sang.");
        }

        CsdtRealtimeConnectionResolver.ValidateInitialCatalog(
            resolved.ConnectionString,
            "QLHV_APP");
        var connection = new SqlConnection(resolved.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private CsdtRealtimeRouteDefinition ValidateCommandRoute(CsdtRealtimeCommand command)
    {
        var route = GetConfiguredRoute(command.StreamCode);
        var expected = command.CommandType == CsdtRealtimeCommandTypes.ReverseExecute
            ? route.Reverse()
            : route;
        if (!string.Equals(expected.VehicleType, command.VehicleType, StringComparison.Ordinal) ||
            !string.Equals(expected.SourceProfileCode, command.SourceProfileCode, StringComparison.Ordinal) ||
            !string.Equals(expected.TargetProfileCode, command.TargetProfileCode, StringComparison.Ordinal) ||
            !string.Equals(expected.SourceDatabaseName, command.SourceDatabaseName, StringComparison.Ordinal) ||
            !string.Equals(expected.TargetDatabaseName, command.TargetDatabaseName, StringComparison.Ordinal) ||
            !string.Equals(expected.MaCSDT, command.MaCSDT, StringComparison.Ordinal))
        {
            throw new ArgumentException("Realtime command route does not match the validated server configuration.");
        }

        return expected;
    }

    private CsdtRealtimeRouteDefinition GetConfiguredRoute(string streamCode)
        => _routesByStream.TryGetValue(streamCode, out var route)
            ? route
            : CsdtRealtimeStreamCatalog.GetLiveByStream(streamCode);

    private static CsdtRealtimeRouteDefinition? ResolveStoredRoute(StreamDomainRow row)
        => CsdtRealtimeStreamCatalog.TryResolveAllowedRoute(
            row.StreamCode,
            row.SourceProfileCode,
            row.TargetProfileCode,
            out var route)
            ? route
            : null;

    private static CsdtRealtimeActionResultDto Rejected(string message) => new()
    {
        Accepted = false,
        Status = CsdtRealtimeActionStatuses.Conflict,
        Message = message,
    };

    private static bool IsStoreFailure(Exception exception) =>
        exception is SqlException or
            TimeoutException or
            InvalidOperationException;

    private sealed class StreamDomainRow
    {
        public long StreamId { get; set; }
        public string StreamCode { get; set; } = string.Empty;
        public string VehicleType { get; set; } = string.Empty;
        public string SourceProfileCode { get; set; } = string.Empty;
        public string TargetProfileCode { get; set; } = string.Empty;
        public string MaCSDT { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string StreamStatus { get; set; } = string.Empty;
        public string BaselineStatus { get; set; } = string.Empty;
        public long? BaselineVersion { get; set; }
        public long? LastSuccessfulVersion { get; set; }
        public long? CurrentSourceVersion { get; set; }
        public long? MinimumValidVersion { get; set; }
        public long? LagVersions { get; set; }
        public Guid? ActiveRunId { get; set; }
        public int RetryCount { get; set; }
        public DateTimeOffset? NextRetryAtUtc { get; set; }
        public DateTimeOffset? LastStartedAtUtc { get; set; }
        public DateTimeOffset? LastCompletedAtUtc { get; set; }
        public DateTimeOffset? LastSuccessAtUtc { get; set; }
        public long InsertedRows { get; set; }
        public long UpdatedRows { get; set; }
        public long SkippedRows { get; set; }
        public long ErrorRows { get; set; }
        public long DeleteTombstoneCount { get; set; }
        public long UnresolvedConflictCount { get; set; }
        public string? LastErrorMessage { get; set; }
        public byte[] RowVersion { get; set; } = [];
        public string? DomainCode { get; set; }
        public int DomainOrder { get; set; }
        public string? DomainStatus { get; set; }
        public long DomainSourceRows { get; set; }
        public long DomainTargetRows { get; set; }
        public long DomainInsertedRows { get; set; }
        public long DomainUpdatedRows { get; set; }
        public long DomainSkippedRows { get; set; }
        public long DomainErrorRows { get; set; }
        public string? DomainLastErrorMessage { get; set; }
    }

    private sealed class RunRow
    {
        public Guid RunId { get; set; }
        public string RunType { get; set; } = string.Empty;
        public string RunStatus { get; set; } = string.Empty;
        public long? FromVersion { get; set; }
        public long? ToVersion { get; set; }
        public DateTimeOffset? StartedAtUtc { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset? CompletedAtUtc { get; set; }
        public long InsertedRows { get; set; }
        public long UpdatedRows { get; set; }
        public long SkippedRows { get; set; }
        public long ErrorRows { get; set; }
        public string Actor { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private sealed class RunDomainRow
    {
        public Guid RunId { get; set; }
        public string DomainCode { get; set; } = string.Empty;
        public string DomainStatus { get; set; } = string.Empty;
        public int AttemptCount { get; set; }
        public DateTimeOffset? LastAttemptAtUtc { get; set; }
        public DateTimeOffset? SucceededAtUtc { get; set; }
        public long InsertedRows { get; set; }
        public long UpdatedRows { get; set; }
        public long SkippedRows { get; set; }
        public long ErrorRows { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private sealed class TombstoneRow
    {
        public long TombstoneId { get; set; }
        public string DomainCode { get; set; } = string.Empty;
        public string EntityKey { get; set; } = string.Empty;
        public long SourceVersion { get; set; }
        public DateTimeOffset FirstSeenAtUtc { get; set; }
        public string TombstoneStatus { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    private sealed class CommandStateRow
    {
        public long StreamId { get; set; }
        public string VehicleType { get; set; } = string.Empty;
        public string SourceProfileCode { get; set; } = string.Empty;
        public string TargetProfileCode { get; set; } = string.Empty;
        public string MaCSDT { get; set; } = string.Empty;
        public byte[] RowVersion { get; set; } = [];
    }

    private sealed class ActiveCommandRow
    {
        public string CommandType { get; set; } = string.Empty;
        public string? RequestJson { get; set; }
        public Guid? RunId { get; set; }
    }

    private const string StreamStatusSql = """
        SELECT
            s.StreamId,
            s.StreamCode,
            s.VehicleType,
            s.SourceProfileCode,
            s.TargetProfileCode,
            s.MaCSDT,
            s.IsEnabled,
            s.StreamStatus,
            s.BaselineStatus,
            s.BaselineVersion,
            s.LastSuccessfulVersion,
            s.CurrentSourceVersion,
            s.MinimumValidVersion,
            s.LagVersions,
            activeRun.RunId AS ActiveRunId,
            s.RetryCount,
            s.NextRetryAtUtc,
            s.LastStartedAtUtc,
            s.LastCompletedAtUtc,
            s.LastSuccessAtUtc,
            ISNULL(latestRun.InsertedRows, 0) AS InsertedRows,
            ISNULL(latestRun.UpdatedRows, 0) AS UpdatedRows,
            ISNULL(latestRun.SkippedRows, 0) AS SkippedRows,
            ISNULL(latestRun.ErrorRows, 0) AS ErrorRows,
            ISNULL(tombstone.DeleteTombstoneCount, 0) AS DeleteTombstoneCount,
            ISNULL(conflict.UnresolvedConflictCount, 0) AS UnresolvedConflictCount,
            s.LastErrorMessage,
            s.RowVersion,
            d.DomainCode,
            d.DomainOrder,
            d.DomainStatus,
            d.SourceRows AS DomainSourceRows,
            d.TargetRows AS DomainTargetRows,
            d.InsertedRows AS DomainInsertedRows,
            d.UpdatedRows AS DomainUpdatedRows,
            d.SkippedRows AS DomainSkippedRows,
            d.ErrorRows AS DomainErrorRows,
            d.LastErrorMessage AS DomainLastErrorMessage
        FROM dbo.App_CsdtRealtimeStream AS s
        LEFT JOIN dbo.App_CsdtRealtimeDomainState AS d ON d.StreamId = s.StreamId
        OUTER APPLY
        (
            SELECT TOP (1) r.RunId
            FROM dbo.App_CsdtRealtimeRun AS r
            WHERE r.StreamId = s.StreamId AND r.ActiveSlot = 1
            ORDER BY r.CreatedAtUtc DESC
        ) AS activeRun
        OUTER APPLY
        (
            SELECT TOP (1) r.InsertedRows, r.UpdatedRows, r.SkippedRows, r.ErrorRows
            FROM dbo.App_CsdtRealtimeRun AS r
            WHERE r.StreamId = s.StreamId AND r.RunStatus IN (N'SUCCEEDED', N'PARTIAL', N'FAILED')
            ORDER BY r.CreatedAtUtc DESC
        ) AS latestRun
        OUTER APPLY
        (
            SELECT COUNT_BIG(1) AS DeleteTombstoneCount
            FROM dbo.App_CsdtRealtimeTombstone AS t
            WHERE t.StreamId = s.StreamId AND t.TombstoneStatus = N'PENDING'
        ) AS tombstone
        OUTER APPLY
        (
            SELECT COUNT_BIG(1) AS UnresolvedConflictCount
            FROM dbo.App_CsdtRealtimeConflict AS c
            WHERE c.StreamId = s.StreamId AND c.ConflictStatus = N'PENDING'
        ) AS conflict
        ORDER BY s.StreamCode, d.DomainOrder;
        """;

    private const string HistorySql = """
        SELECT TOP (@Take)
            r.RunId, r.RunType, r.RunStatus, r.FromVersion, r.ToVersion,
            r.StartedAtUtc, r.CreatedAtUtc, r.CompletedAtUtc,
            r.InsertedRows, r.UpdatedRows, r.SkippedRows, r.ErrorRows,
            r.Actor, r.ErrorCode, r.ErrorMessage
        INTO #SelectedRuns
        FROM dbo.App_CsdtRealtimeRun AS r
        INNER JOIN dbo.App_CsdtRealtimeStream AS s ON s.StreamId = r.StreamId
        WHERE s.StreamCode = @StreamCode
        ORDER BY r.CreatedAtUtc DESC;

        SELECT
            RunId, RunType, RunStatus, FromVersion, ToVersion,
            StartedAtUtc, CreatedAtUtc, CompletedAtUtc,
            InsertedRows, UpdatedRows, SkippedRows, ErrorRows,
            Actor, ErrorCode, ErrorMessage
        FROM #SelectedRuns
        ORDER BY CreatedAtUtc DESC;

        SELECT
            d.RunId, d.DomainCode, d.DomainStatus, d.AttemptCount,
            d.LastAttemptAtUtc, d.SucceededAtUtc,
            d.InsertedRows, d.UpdatedRows,
            d.SkippedRows, d.ErrorRows, d.ErrorMessage
        FROM dbo.App_CsdtRealtimeRunDomain AS d
        INNER JOIN #SelectedRuns AS selected ON selected.RunId = d.RunId
        ORDER BY selected.CreatedAtUtc DESC, d.DomainCode;
        """;

    private const string TombstoneSql = """
        SELECT TOP (@Take)
            t.TombstoneId, t.DomainCode, t.EntityKey, t.SourceVersion,
            t.FirstSeenAtUtc, t.TombstoneStatus, t.Note
        FROM dbo.App_CsdtRealtimeTombstone AS t
        INNER JOIN dbo.App_CsdtRealtimeStream AS s ON s.StreamId = t.StreamId
        WHERE s.StreamCode = @StreamCode
        ORDER BY t.LastSeenAtUtc DESC, t.TombstoneId DESC;
        """;

    private const string CommandStateSql = """
        SELECT
            StreamId, VehicleType, SourceProfileCode, TargetProfileCode,
            MaCSDT, RowVersion
        FROM dbo.App_CsdtRealtimeStream WITH (UPDLOCK, HOLDLOCK)
        WHERE StreamCode = @StreamCode;
        """;

    private const string ActiveCommandSql = """
        SELECT TOP (1) CommandType, RequestJson, RunId
        FROM dbo.App_CsdtRealtimeCommand WITH (UPDLOCK, HOLDLOCK)
        WHERE StreamId = @StreamId AND ActiveSlot = 1
        ORDER BY RequestedAtUtc;
        """;

    private const string InsertCommandSql = """
        INSERT INTO dbo.App_CsdtRealtimeCommand
        (
            CommandId, StreamId, CommandType, CommandStatus, ActiveSlot,
            RequestedBy, ExpectedRowVersion, RequestJson
        )
        VALUES
        (
            @CommandId, @StreamId, @CommandType, N'QUEUED', 1,
            @RequestedBy, @ExpectedRowVersion, @RequestJson
        );
        """;
}

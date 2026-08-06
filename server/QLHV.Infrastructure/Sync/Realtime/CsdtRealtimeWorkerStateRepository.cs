using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync.Realtime;

namespace QLHV.Infrastructure.Sync.Realtime;

internal sealed partial class CsdtRealtimeStateRepository
{
    /// <summary>
    /// BAK verification and live operation intentionally share the durable state
    /// database but never share checkpoints. A fixed route switch invalidates
    /// only synchronization metadata and forces a fresh baseline; business rows
    /// in either source or target are never changed here.
    /// </summary>
    internal async Task EnsureRuntimeRouteAsync(
        CsdtRealtimeRouteDefinition route,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var current = await connection.QuerySingleAsync<RuntimeRouteRow>(new CommandDefinition(
                """
                SELECT
                    StreamId, VehicleType, SourceProfileCode, TargetProfileCode, MaCSDT
                FROM dbo.App_CsdtRealtimeStream WITH (UPDLOCK, HOLDLOCK)
                WHERE StreamCode = @StreamCode;
                """,
                new { route.StreamCode },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            var matches =
                string.Equals(current.VehicleType, route.VehicleType, StringComparison.Ordinal) &&
                string.Equals(current.SourceProfileCode, route.SourceProfileCode, StringComparison.Ordinal) &&
                string.Equals(current.TargetProfileCode, route.TargetProfileCode, StringComparison.Ordinal) &&
                string.Equals(current.MaCSDT, route.MaCSDT, StringComparison.Ordinal);
            if (matches)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var activeWork = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT
                    (
                        SELECT COUNT(*)
                        FROM dbo.App_CsdtRealtimeRun
                        WHERE StreamId = @StreamId AND ActiveSlot = 1
                    )
                    +
                    (
                        SELECT COUNT(*)
                        FROM dbo.App_CsdtRealtimeCommand
                        WHERE StreamId = @StreamId AND ActiveSlot = 1
                    );
                """,
                new { current.StreamId },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            if (activeWork != 0)
            {
                throw new InvalidOperationException(
                    "Cannot switch the fixed realtime route while work is active.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM dbo.App_CsdtRealtimeSourceIdentity WHERE StreamId = @StreamId;
                DELETE FROM dbo.App_CsdtRealtimeEntityState WHERE StreamId = @StreamId;
                DELETE FROM dbo.App_CsdtRealtimeTombstone WHERE StreamId = @StreamId;
                DELETE FROM dbo.App_CsdtRealtimeConflict WHERE StreamId = @StreamId;

                UPDATE dbo.App_CsdtRealtimeDomainState
                SET DomainStatus = N'PENDING',
                    BaselineStatus = N'NOT_STARTED',
                    BaselineVersion = NULL,
                    LastSuccessfulVersion = NULL,
                    CurrentSourceVersion = NULL,
                    MinimumValidVersion = NULL,
                    LagVersions = NULL,
                    SourceRows = 0,
                    TargetRows = 0,
                    BaselineRows = 0,
                    InsertedRows = 0,
                    UpdatedRows = 0,
                    SkippedRows = 0,
                    ErrorRows = 0,
                    TombstoneRows = 0,
                    ReconciledRows = 0,
                    LastStartedAtUtc = NULL,
                    LastCompletedAtUtc = NULL,
                    LastSuccessAtUtc = NULL,
                    RetryCount = 0,
                    NextRetryAtUtc = NULL,
                    LastErrorCode = NULL,
                    LastErrorMessage = NULL,
                    UpdatedAtUtc = SYSUTCDATETIME()
                WHERE StreamId = @StreamId;

                UPDATE dbo.App_CsdtRealtimeStream
                SET VehicleType = @VehicleType,
                    SourceProfileCode = @SourceProfileCode,
                    TargetProfileCode = @TargetProfileCode,
                    MaCSDT = @MaCSDT,
                    StreamStatus = CASE WHEN IsEnabled = 1 THEN N'BASELINE_PENDING' ELSE N'DISABLED' END,
                    BaselineStatus = N'NOT_STARTED',
                    BaselineVersion = NULL,
                    LastSuccessfulVersion = NULL,
                    CurrentSourceVersion = NULL,
                    MinimumValidVersion = NULL,
                    LagVersions = NULL,
                    LastStartedAtUtc = NULL,
                    LastCompletedAtUtc = NULL,
                    LastSuccessAtUtc = NULL,
                    LastReconciledAtUtc = NULL,
                    RetryCount = 0,
                    NextRetryAtUtc = NULL,
                    LastErrorCode = N'ROUTE_CHANGED_REBASELINE_REQUIRED',
                    LastErrorMessage = N'Fixed live/BAK route changed; a new baseline is required.',
                    UpdatedAtUtc = SYSUTCDATETIME()
                WHERE StreamId = @StreamId;
                """,
                new
                {
                    current.StreamId,
                    route.VehicleType,
                    route.SourceProfileCode,
                    route.TargetProfileCode,
                    route.MaCSDT,
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    /// <summary>
    /// A RUNNING row can only have an owner while that owner holds the stream's
    /// session applock. Once a new session owns that lock, any active run or
    /// RUNNING command is an orphan left by a terminated Worker.
    /// </summary>
    internal async Task<bool> RecoverOrphanedWorkAsync(
        long streamId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var recovered = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                DECLARE @Recovered int = 0;
                DECLARE @RecoveredForward int = 0;

                UPDATE domainRun
                SET DomainStatus = N'PENDING',
                    CompletedAtUtc = NULL,
                    ErrorCode = N'REVERSE_COMMIT_STATE_UNKNOWN',
                    ErrorMessage = N'Worker stopped while target commit state was being finalized; retry will verify the approved source digest.'
                FROM dbo.App_CsdtRealtimeRunDomain AS domainRun
                INNER JOIN dbo.App_CsdtRealtimeRun AS run
                    ON run.RunId = domainRun.RunId
                WHERE run.StreamId = @StreamId
                  AND run.ActiveSlot = 1
                  AND run.RunType = N'REVERSE'
                  AND run.RunStatus IN (N'QUEUED', N'RUNNING')
                  AND domainRun.DomainStatus IN (N'PENDING', N'RUNNING');

                UPDATE dbo.App_CsdtRealtimeRun
                SET RunStatus = N'PARTIAL',
                    ActiveSlot = NULL,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    ErrorCode = N'REVERSE_COMMIT_STATE_UNKNOWN',
                    ErrorMessage = N'Worker stopped before reverse completion state was finalized.'
                WHERE StreamId = @StreamId
                  AND ActiveSlot = 1
                  AND RunType = N'REVERSE'
                  AND RunStatus IN (N'QUEUED', N'RUNNING');
                SET @Recovered += @@ROWCOUNT;

                UPDATE command
                SET CommandStatus = N'PARTIAL',
                    ActiveSlot = NULL,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    ErrorCode = N'REVERSE_COMMIT_STATE_UNKNOWN',
                    ErrorMessage = N'Retry will verify whether the atomic target transaction committed.'
                FROM dbo.App_CsdtRealtimeCommand AS command
                INNER JOIN dbo.App_CsdtRealtimeRun AS run
                    ON run.RunId = command.RunId
                WHERE command.StreamId = @StreamId
                  AND command.ActiveSlot = 1
                  AND command.CommandStatus = N'RUNNING'
                  AND run.RunType = N'REVERSE';
                SET @Recovered += @@ROWCOUNT;

                UPDATE domainRun
                SET DomainStatus = N'FAILED',
                    CompletedAtUtc = SYSUTCDATETIME(),
                    ErrorRows = ErrorRows + 1,
                    ErrorCode = N'WORKER_RESTARTED',
                    ErrorMessage = N'Worker stopped before the operation completed.'
                FROM dbo.App_CsdtRealtimeRunDomain AS domainRun
                INNER JOIN dbo.App_CsdtRealtimeRun AS run
                    ON run.RunId = domainRun.RunId
                WHERE run.StreamId = @StreamId
                  AND run.ActiveSlot = 1
                  AND run.RunType <> N'REVERSE'
                  AND run.RunStatus IN (N'QUEUED', N'RUNNING')
                  AND domainRun.DomainStatus IN (N'PENDING', N'RUNNING');

                UPDATE dbo.App_CsdtRealtimeRun
                SET RunStatus = N'FAILED',
                    ActiveSlot = NULL,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    ErrorRows = ErrorRows + 1,
                    ErrorCode = N'WORKER_RESTARTED',
                    ErrorMessage = N'Worker stopped before the operation completed.'
                WHERE StreamId = @StreamId
                  AND ActiveSlot = 1
                  AND RunType <> N'REVERSE'
                  AND RunStatus IN (N'QUEUED', N'RUNNING');
                SET @RecoveredForward += @@ROWCOUNT;

                UPDATE command
                SET CommandStatus = N'FAILED',
                    ActiveSlot = NULL,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    ErrorCode = N'WORKER_RESTARTED',
                    ErrorMessage = N'Worker stopped before the command completed.'
                FROM dbo.App_CsdtRealtimeCommand AS command
                WHERE command.StreamId = @StreamId
                  AND command.ActiveSlot = 1
                  AND command.CommandStatus = N'RUNNING'
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.App_CsdtRealtimeRun AS run
                      WHERE run.RunId = command.RunId
                        AND run.RunType = N'REVERSE'
                  );
                SET @RecoveredForward += @@ROWCOUNT;
                SET @Recovered += @RecoveredForward;

                IF @RecoveredForward > 0
                BEGIN
                    UPDATE dbo.App_CsdtRealtimeStream
                    SET StreamStatus = CASE WHEN IsEnabled = 0 THEN N'DISABLED' ELSE N'ERROR' END,
                        NextRetryAtUtc = SYSUTCDATETIME(),
                        LastCompletedAtUtc = SYSUTCDATETIME(),
                        LastErrorCode = N'WORKER_RESTARTED',
                        LastErrorMessage = N'Orphaned realtime work was recovered; checkpoint was preserved.',
                        UpdatedAtUtc = SYSUTCDATETIME()
                    WHERE StreamId = @StreamId;
                END;

                SELECT @Recovered;
                """,
                new { StreamId = streamId },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return recovered > 0;
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    private sealed record RuntimeRouteRow(
        long StreamId,
        string VehicleType,
        string SourceProfileCode,
        string TargetProfileCode,
        string MaCSDT);

    /// <summary>
    /// Advances a domain checkpoint when Change Tracking contained no current
    /// row that needed a target write. Existing row counters are deliberately
    /// preserved.
    /// </summary>
    internal async Task CompleteCheckpointOnlyDomainAsync(
        CsdtRealtimeRunHandle run,
        CsdtRealtimeDomainDefinition domain,
        long toVersion,
        long minimumValidVersion,
        IReadOnlyList<CsdtRealtimeChange> tombstones,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var persistedTombstones = await PersistSourceIdentitiesAndTombstonesAsync(
                connection,
                transaction,
                run,
                domain,
                currentSourceIdentities: [],
                tombstones,
                toVersion,
                inferMissingIdentities: false,
                cancellationToken);

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.App_CsdtRealtimeDomainState
                SET DomainStatus = N'RUNNING',
                    LastSuccessfulVersion = @ToVersion,
                    CurrentSourceVersion = @ToVersion,
                    MinimumValidVersion = @MinimumValidVersion,
                    LagVersions = 0,
                    TombstoneRows = TombstoneRows + @TombstoneRows,
                    LastCompletedAtUtc = SYSUTCDATETIME(),
                    LastSuccessAtUtc = SYSUTCDATETIME(),
                    RetryCount = 0,
                    NextRetryAtUtc = NULL,
                    LastErrorCode = NULL,
                    LastErrorMessage = NULL,
                    UpdatedAtUtc = SYSUTCDATETIME()
                WHERE StreamId = @StreamId AND DomainCode = @DomainCode;

                UPDATE dbo.App_CsdtRealtimeRunDomain
                SET DomainStatus = N'SUCCEEDED',
                    ToVersion = @ToVersion,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    InsertedRows = 0,
                    UpdatedRows = 0,
                    SkippedRows = 0,
                    ErrorRows = 0,
                    TombstoneRows = @TombstoneRows,
                    ErrorCode = NULL,
                    ErrorMessage = NULL
                WHERE RunId = @RunId AND DomainCode = @DomainCode;
                """,
                new
                {
                    run.RunId,
                    run.StreamId,
                    DomainCode = domain.Name,
                    ToVersion = toVersion,
                    MinimumValidVersion = minimumValidVersion,
                    TombstoneRows = persistedTombstones.Count,
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    internal async Task SkipOptionalDomainAsync(
        CsdtRealtimeRunHandle run,
        CsdtRealtimeDomainDefinition domain,
        long toVersion,
        long minimumValidVersion,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (!domain.IsOptional)
        {
            throw new ArgumentException("Only optional realtime domains can be skipped.", nameof(domain));
        }

        var message = SanitizeError(exception.Message);
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.App_CsdtRealtimeDomainState
            SET DomainStatus = N'SKIPPED',
                BaselineStatus = CASE WHEN @RunType = N'BASELINE' THEN N'SKIPPED' ELSE BaselineStatus END,
                CurrentSourceVersion = @ToVersion,
                MinimumValidVersion = @MinimumValidVersion,
                LagVersions = CASE
                    WHEN LastSuccessfulVersion IS NULL THEN @ToVersion
                    WHEN @ToVersion >= LastSuccessfulVersion THEN @ToVersion - LastSuccessfulVersion
                    ELSE 0
                END,
                SkippedRows = SkippedRows + 1,
                LastCompletedAtUtc = SYSUTCDATETIME(),
                RetryCount = RetryCount + 1,
                NextRetryAtUtc = DATEADD
                (
                    second,
                    CASE
                        WHEN RetryCount = 0 THEN 5
                        WHEN RetryCount = 1 THEN 15
                        WHEN RetryCount = 2 THEN 30
                        ELSE 60
                    END,
                    SYSUTCDATETIME()
                ),
                LastErrorCode = N'SKIPPED_UNSUPPORTED_SCHEMA',
                LastErrorMessage = @Message,
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE StreamId = @StreamId AND DomainCode = @DomainCode;

            UPDATE dbo.App_CsdtRealtimeRunDomain
            SET DomainStatus = N'SKIPPED',
                ToVersion = @ToVersion,
                CompletedAtUtc = SYSUTCDATETIME(),
                InsertedRows = 0,
                UpdatedRows = 0,
                SkippedRows = 1,
                ErrorRows = 0,
                TombstoneRows = 0,
                ErrorCode = N'SKIPPED_UNSUPPORTED_SCHEMA',
                ErrorMessage = @Message
            WHERE RunId = @RunId AND DomainCode = @DomainCode;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.App_CsdtRealtimeRunDomain
                (
                    RunId, DomainCode, DomainStatus, FromVersion, ToVersion,
                    StartedAtUtc, CompletedAtUtc, InsertedRows, UpdatedRows,
                    SkippedRows, ErrorRows, TombstoneRows, ErrorCode, ErrorMessage
                )
                VALUES
                (
                    @RunId, @DomainCode, N'SKIPPED', NULL, @ToVersion,
                    SYSUTCDATETIME(), SYSUTCDATETIME(), 0, 0,
                    1, 0, 0, N'SKIPPED_UNSUPPORTED_SCHEMA', @Message
                );
            END;
            """,
            new
            {
                run.RunId,
                run.StreamId,
                RunType = run.RunType,
                DomainCode = domain.Name,
                ToVersion = toVersion,
                MinimumValidVersion = minimumValidVersion,
                Message = message,
            },
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }
    internal async Task RecordStreamFailureAsync(
        long streamId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.App_CsdtRealtimeStream
            SET StreamStatus = CASE WHEN IsEnabled = 0 THEN N'DISABLED' ELSE N'ERROR' END,
                LastCompletedAtUtc = SYSUTCDATETIME(),
                RetryCount = RetryCount + 1,
                NextRetryAtUtc = DATEADD
                (
                    second,
                    CASE
                        WHEN RetryCount = 0 THEN 5
                        WHEN RetryCount = 1 THEN 15
                        WHEN RetryCount = 2 THEN 30
                        ELSE 60
                    END,
                    SYSUTCDATETIME()
                ),
                LastErrorCode = N'WORKER_FAILURE',
                LastErrorMessage = @Message,
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE StreamId = @StreamId;
            """,
            new
            {
                StreamId = streamId,
                Message = SanitizeError(exception.Message),
            },
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    internal async Task<CsdtReverseRecovery?> GetLatestReverseRecoveryAsync(
        long streamId,
        string? maKhoaHoc,
        string expectedPlanToken,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        var runId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            """
            SELECT TOP (1) RunId
            FROM dbo.App_CsdtRealtimeRun
            WHERE StreamId = @StreamId
              AND RunType = N'REVERSE'
              AND
              (
                  RunStatus = N'FAILED'
                  OR
                  (
                      RunStatus = N'PARTIAL'
                      AND ErrorCode = N'REVERSE_COMMIT_STATE_UNKNOWN'
                  )
              )
              AND JSON_VALUE(DetailJson, '$.planToken') = @ExpectedPlanToken
              AND
              (
                  (@MaKhoaHoc IS NULL AND JSON_VALUE(DetailJson, '$.maKhoaHoc') IS NULL)
                  OR JSON_VALUE(DetailJson, '$.maKhoaHoc') = @MaKhoaHoc
              )
            ORDER BY CreatedAtUtc DESC, RunId DESC;
            """,
            new
            {
                StreamId = streamId,
                MaKhoaHoc = maKhoaHoc,
                ExpectedPlanToken = expectedPlanToken,
            },
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        if (!runId.HasValue)
        {
            return null;
        }

        var rows = await connection.QueryAsync<ReverseRecoveryDomainRow>(new CommandDefinition(
            """
            SELECT
                DomainCode,
                SourceRows,
                AttemptCount,
                JSON_VALUE(DetailJson, '$.sourceDigest') AS SourceDigest,
                CAST(JSON_VALUE(DetailJson, '$.isOptional') AS bit) AS IsOptional
            FROM dbo.App_CsdtRealtimeRunDomain
            WHERE RunId = @RunId
            ORDER BY DomainCode;
            """,
            new { RunId = runId.Value },
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        var domains = rows.ToDictionary(
            row => row.DomainCode,
            row => new CsdtReverseDomainIntent(
                row.DomainCode,
                row.IsOptional,
                row.SourceRows ?? 0,
                row.SourceDigest ?? string.Empty,
                row.AttemptCount),
            StringComparer.Ordinal);
        return domains.Count == 0
            ? null
            : new CsdtReverseRecovery(runId.Value, domains);
    }

    internal async Task InitializeReverseRunAsync(
        CsdtReverseExecutionContext context,
        string planToken,
        string? maKhoaHoc,
        IReadOnlyList<CsdtReverseDomainIntent> intents,
        CancellationToken cancellationToken)
    {
        var runDetail = JsonSerializer.Serialize(new
        {
            planToken,
            maKhoaHoc,
            semantics = "ATOMIC_LOCAL_TARGET_TRANSACTION",
        });
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.App_CsdtRealtimeRun
                SET DetailJson = @DetailJson
                WHERE RunId = @RunId
                  AND StreamId = @StreamId
                  AND RunType = N'REVERSE'
                  AND RunStatus = N'RUNNING'
                  AND ActiveSlot = 1;
                """,
                new
                {
                    context.RunId,
                    context.StreamId,
                    DetailJson = runDetail,
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            if (updated != 1)
            {
                throw new InvalidOperationException(
                    "Reverse run lost its active ownership before intent initialization.");
            }

            foreach (var intent in intents)
            {
                var domainDetail = JsonSerializer.Serialize(new
                {
                    sourceDigest = intent.SourceDigest,
                    isOptional = intent.IsOptional,
                });
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dbo.App_CsdtRealtimeRunDomain
                    (
                        RunId, DomainCode, DomainStatus, SourceRows,
                        AttemptCount, DetailJson
                    )
                    VALUES
                    (
                        @RunId, @DomainCode, N'PENDING', @SourceRows,
                        @AttemptCount, @DetailJson
                    );
                    """,
                    new
                    {
                        context.RunId,
                        DomainCode = intent.Domain,
                        intent.SourceRows,
                        intent.AttemptCount,
                        DetailJson = domainDetail,
                    },
                    transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    internal async Task MarkReverseDomainsRunningAsync(
        Guid runId,
        IReadOnlyList<string> domainCodes,
        CancellationToken cancellationToken)
    {
        if (domainCodes.Count == 0)
        {
            return;
        }

        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.App_CsdtRealtimeRunDomain
            SET DomainStatus = N'RUNNING',
                StartedAtUtc = SYSUTCDATETIME(),
                CompletedAtUtc = NULL,
                AttemptCount = AttemptCount + 1,
                LastAttemptAtUtc = SYSUTCDATETIME(),
                ErrorCode = NULL,
                ErrorMessage = NULL
            WHERE RunId = @RunId
              AND DomainCode IN @DomainCodes
              AND DomainStatus = N'PENDING';
            """,
            new { RunId = runId, DomainCodes = domainCodes },
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    internal async Task CompleteReverseRunAsync(
        CsdtRealtimeRunHandle run,
        Guid commandId,
        CsdtReverseCommandExecutionResult result,
        CancellationToken cancellationToken)
    {
        CsdtReverseAtomicExecutionPolicy.EnsureMandatoryDomainsCompleted(result.Domains);
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            foreach (var domain in result.Domains)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.App_CsdtRealtimeRunDomain
                    SET DomainStatus = @DomainStatus,
                        CompletedAtUtc = SYSUTCDATETIME(),
                        UpdatedRows = @UpdatedRows,
                        SkippedRows = @SkippedRows,
                        ErrorRows = CASE WHEN @ErrorCode IS NULL THEN 0 ELSE 1 END,
                        AttemptCount = @AttemptCount,
                        SucceededAtUtc = CASE
                            WHEN @DomainStatus IN (N'SUCCEEDED', N'SKIPPED')
                                THEN SYSUTCDATETIME()
                            ELSE SucceededAtUtc
                        END,
                        ErrorCode = @ErrorCode,
                        ErrorMessage = @ErrorMessage
                    WHERE RunId = @RunId AND DomainCode = @DomainCode;
                    """,
                    new
                    {
                        run.RunId,
                        DomainCode = domain.Domain,
                        DomainStatus = domain.Status,
                        domain.UpdatedRows,
                        domain.SkippedRows,
                        domain.AttemptCount,
                        domain.ErrorCode,
                        domain.ErrorMessage,
                    },
                    transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.App_CsdtRealtimeRun
                SET RunStatus = @RunStatus,
                    ActiveSlot = NULL,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    UpdatedRows = @UpdatedRows,
                    SkippedRows = @SkippedRows,
                    ErrorRows = @ErrorRows,
                    ErrorCode = NULL,
                    ErrorMessage = NULL
                WHERE RunId = @RunId
                  AND RunType = N'REVERSE'
                  AND ActiveSlot = 1;

                UPDATE dbo.App_CsdtRealtimeCommand
                SET CommandStatus = N'SUCCEEDED',
                    ActiveSlot = NULL,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    ErrorCode = NULL,
                    ErrorMessage = NULL
                WHERE CommandId = @CommandId
                  AND CommandStatus = N'RUNNING'
                  AND ActiveSlot = 1;
                """,
                new
                {
                    run.RunId,
                    CommandId = commandId,
                    RunStatus = result.HasOptionalSkips ? "PARTIAL" : "SUCCEEDED",
                    result.UpdatedRows,
                    SkippedRows = result.Domains.Sum(domain => domain.SkippedRows),
                    ErrorRows = result.Domains.LongCount(domain => domain.ErrorCode is not null),
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    internal async Task FailAtomicReverseRunAsync(
        CsdtRealtimeRunHandle run,
        Guid commandId,
        CsdtReverseAtomicWriteException exception,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var orderedDomains = (await connection.QueryAsync<string>(new CommandDefinition(
                """
                SELECT DomainCode
                FROM dbo.App_CsdtRealtimeRunDomain
                WHERE RunId = @RunId
                ORDER BY DomainCode;
                """,
                new { run.RunId },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken))).AsList();
            var statuses = CsdtReverseAtomicExecutionPolicy.BuildRollbackStatuses(
                orderedDomains,
                exception.AttemptedDomains,
                exception.OptionalSkips);
            var skipsByDomain = exception.OptionalSkips.ToDictionary(
                skip => skip.Domain,
                StringComparer.Ordinal);
            foreach (var (domain, status) in statuses.Where(item =>
                         !string.Equals(item.Value, "PENDING", StringComparison.Ordinal)))
            {
                var isSkipped = string.Equals(status, "SKIPPED", StringComparison.Ordinal);
                skipsByDomain.TryGetValue(domain, out var skip);
                var isFailedDomain = string.Equals(
                    domain,
                    exception.FailedDomain,
                    StringComparison.Ordinal);
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.App_CsdtRealtimeRunDomain
                    SET DomainStatus = @DomainStatus,
                        CompletedAtUtc = SYSUTCDATETIME(),
                        SkippedRows = @SkippedRows,
                        ErrorRows = CASE
                            WHEN @DomainStatus = N'FAILED' THEN ErrorRows + 1
                            ELSE ErrorRows
                        END,
                        SucceededAtUtc = CASE
                            WHEN @DomainStatus = N'SKIPPED' THEN SYSUTCDATETIME()
                            ELSE SucceededAtUtc
                        END,
                        ErrorCode = @ErrorCode,
                        ErrorMessage = @ErrorMessage
                    WHERE RunId = @RunId AND DomainCode = @DomainCode;
                    """,
                    new
                    {
                        run.RunId,
                        DomainCode = domain,
                        DomainStatus = status,
                        SkippedRows = skip?.SkippedRows ?? 0,
                        ErrorCode = isSkipped
                            ? skip?.ErrorCode
                            : isFailedDomain
                                ? "REVERSE_DOMAIN_FAILED"
                                : "REVERSE_TRANSACTION_ROLLED_BACK",
                        ErrorMessage = isSkipped
                            ? skip?.ErrorMessage
                            : isFailedDomain
                                ? SanitizeError(
                                    exception.InnerException?.Message ?? exception.Message)
                                : "Target changes were rolled back because another domain failed.",
                    },
                    transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.App_CsdtRealtimeRun
                SET RunStatus = N'FAILED',
                    ActiveSlot = NULL,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    ErrorRows = ErrorRows + 1,
                    ErrorCode = N'REVERSE_ATOMIC_ROLLBACK',
                    ErrorMessage = @FailureMessage
                WHERE RunId = @RunId
                  AND RunType = N'REVERSE'
                  AND ActiveSlot = 1;

                UPDATE dbo.App_CsdtRealtimeCommand
                SET CommandStatus = N'FAILED',
                    ActiveSlot = NULL,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    ErrorCode = N'REVERSE_ATOMIC_ROLLBACK',
                    ErrorMessage = @FailureMessage
                WHERE CommandId = @CommandId
                  AND CommandStatus = N'RUNNING'
                  AND ActiveSlot = 1;
                """,
                new
                {
                    run.RunId,
                    CommandId = commandId,
                    FailureMessage = SanitizeError(
                        exception.InnerException?.Message ?? exception.Message),
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    internal async Task MarkReverseCommitStateUnknownAsync(
        CsdtRealtimeRunHandle run,
        Guid commandId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var message = SanitizeError(exception.Message);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.App_CsdtRealtimeRunDomain
                SET DomainStatus = N'PENDING',
                    CompletedAtUtc = NULL,
                    ErrorCode = N'REVERSE_COMMIT_STATE_UNKNOWN',
                    ErrorMessage = N'Target transaction committed; durable completion state requires recovery.'
                WHERE RunId = @RunId
                  AND DomainStatus IN (N'PENDING', N'RUNNING');

                UPDATE dbo.App_CsdtRealtimeRun
                SET RunStatus = N'PARTIAL',
                    ActiveSlot = NULL,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    ErrorCode = N'REVERSE_COMMIT_STATE_UNKNOWN',
                    ErrorMessage = @Message
                WHERE RunId = @RunId
                  AND RunType = N'REVERSE'
                  AND ActiveSlot = 1;

                UPDATE dbo.App_CsdtRealtimeCommand
                SET CommandStatus = N'PARTIAL',
                    ActiveSlot = NULL,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    ErrorCode = N'REVERSE_COMMIT_STATE_UNKNOWN',
                    ErrorMessage = @Message
                WHERE CommandId = @CommandId
                  AND CommandStatus = N'RUNNING'
                  AND ActiveSlot = 1;
                """,
                new
                {
                    run.RunId,
                    CommandId = commandId,
                    Message = message,
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    private sealed record ReverseRecoveryDomainRow(
        string DomainCode,
        long? SourceRows,
        int AttemptCount,
        string? SourceDigest,
        bool IsOptional);

    internal async Task FailRunAsync(
        CsdtRealtimeRunHandle run,
        Guid? commandId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var message = SanitizeError(exception.Message);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.App_CsdtRealtimeRun
                SET RunStatus = N'FAILED',
                    ActiveSlot = NULL,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    ErrorRows = ErrorRows + 1,
                    ErrorCode = N'RUN_FAILED',
                    ErrorMessage = @Message
                WHERE RunId = @RunId AND ActiveSlot = 1;

                UPDATE dbo.App_CsdtRealtimeCommand
                SET CommandStatus = N'FAILED',
                    ActiveSlot = NULL,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    ErrorCode = N'RUN_FAILED',
                    ErrorMessage = @Message
                WHERE CommandId = @CommandId AND ActiveSlot = 1;

                UPDATE dbo.App_CsdtRealtimeStream
                SET StreamStatus = CASE WHEN IsEnabled = 0 THEN N'DISABLED' ELSE N'ERROR' END,
                    LastCompletedAtUtc = SYSUTCDATETIME(),
                    RetryCount = RetryCount + 1,
                    NextRetryAtUtc = DATEADD
                    (
                        second,
                        CASE
                            WHEN RetryCount = 0 THEN 5
                            WHEN RetryCount = 1 THEN 15
                            WHEN RetryCount = 2 THEN 30
                            ELSE 60
                        END,
                        SYSUTCDATETIME()
                    ),
                    LastErrorCode = N'RUN_FAILED',
                    LastErrorMessage = @Message,
                    UpdatedAtUtc = SYSUTCDATETIME()
                WHERE StreamId = @StreamId;
                """,
                new
                {
                    run.RunId,
                    run.StreamId,
                    CommandId = commandId,
                    Message = message,
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }
}

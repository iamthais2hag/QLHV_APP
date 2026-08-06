using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace QLHV.Infrastructure.Sync.Realtime;

internal sealed partial class CsdtRealtimeStateRepository
{
    internal async Task<CsdtRealtimeRuntimeStream> GetRuntimeStreamAsync(
        string streamCode,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<CsdtRealtimeRuntimeStream>(new CommandDefinition(
            """
            SELECT
                StreamId, StreamCode, IsEnabled, StreamStatus, BaselineStatus,
                BaselineVersion, LastSuccessfulVersion, LastReconciledAtUtc,
                NextRetryAtUtc, RetryCount
            FROM dbo.App_CsdtRealtimeStream
            WHERE StreamCode = @StreamCode;
            """,
            new { StreamCode = streamCode },
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    internal async Task<IReadOnlyList<CsdtRealtimeRuntimeDomain>> GetRuntimeDomainsAsync(
        long streamId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        var domains = await connection.QueryAsync<CsdtRealtimeRuntimeDomain>(new CommandDefinition(
            """
            SELECT
                StreamId, DomainCode, IsOptional, DomainStatus, BaselineStatus,
                BaselineVersion, LastSuccessfulVersion, NextRetryAtUtc, RetryCount
            FROM dbo.App_CsdtRealtimeDomainState
            WHERE StreamId = @StreamId
            ORDER BY DomainOrder;
            """,
            new { StreamId = streamId },
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        return domains.AsList();
    }

    internal async Task<CsdtRealtimeRuntimeDomain> GetRuntimeDomainAsync(
        long streamId,
        string domainCode,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<CsdtRealtimeRuntimeDomain>(new CommandDefinition(
            """
            SELECT
                StreamId, DomainCode, IsOptional, DomainStatus, BaselineStatus,
                BaselineVersion, LastSuccessfulVersion, NextRetryAtUtc, RetryCount
            FROM dbo.App_CsdtRealtimeDomainState
            WHERE StreamId = @StreamId AND DomainCode = @DomainCode;
            """,
            new { StreamId = streamId, DomainCode = domainCode },
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    internal async Task<CsdtRealtimeClaimedCommand?> ClaimNextCommandAsync(
        string streamCode,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var command = await connection.QuerySingleOrDefaultAsync<CsdtRealtimeClaimedCommand>(
                new CommandDefinition(
                    """
                    SELECT TOP (1)
                        c.CommandId, c.StreamId, s.StreamCode, c.CommandType,
                        c.RequestedBy, c.RequestJson, c.ExpectedRowVersion,
                        s.RowVersion AS CurrentRowVersion
                    FROM dbo.App_CsdtRealtimeCommand AS c WITH (UPDLOCK, READPAST, ROWLOCK)
                    INNER JOIN dbo.App_CsdtRealtimeStream AS s ON s.StreamId = c.StreamId
                    WHERE s.StreamCode = @StreamCode
                      AND c.CommandStatus = N'QUEUED'
                      AND c.ActiveSlot = 1
                    ORDER BY c.RequestedAtUtc, c.CommandId;
                    """,
                    new { StreamCode = streamCode },
                    transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
            if (command is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            if (command.ExpectedRowVersion is not null &&
                !CryptographicOperations.FixedTimeEquals(
                    command.ExpectedRowVersion,
                    command.CurrentRowVersion))
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.App_CsdtRealtimeCommand
                    SET CommandStatus = N'FAILED',
                        ActiveSlot = NULL,
                        CompletedAtUtc = SYSUTCDATETIME(),
                        ErrorCode = N'STALE_STATE_TOKEN',
                        ErrorMessage = N'Stream state changed before Worker claimed the command.'
                    WHERE CommandId = @CommandId
                      AND CommandStatus = N'QUEUED'
                      AND ActiveSlot = 1;
                    """,
                    new { command.CommandId },
                    transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.App_CsdtRealtimeCommand
                SET CommandStatus = N'RUNNING',
                    StartedAtUtc = SYSUTCDATETIME()
                WHERE CommandId = @CommandId
                  AND CommandStatus = N'QUEUED'
                  AND ActiveSlot = 1;
                """,
                new { command.CommandId },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return updated == 1 ? command : null;
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    internal async Task CompleteSetEnabledCommandAsync(
        CsdtRealtimeClaimedCommand command,
        bool enabled,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.App_CsdtRealtimeStream
                SET IsEnabled = @Enabled,
                    StreamStatus = CASE
                        WHEN @Enabled = 0 THEN N'DISABLED'
                        WHEN BaselineStatus = N'COMPLETED' THEN N'RUNNING'
                        ELSE N'BASELINE_PENDING'
                    END,
                    NextRetryAtUtc = NULL,
                    UpdatedAtUtc = SYSUTCDATETIME()
                WHERE StreamId = @StreamId;

                UPDATE dbo.App_CsdtRealtimeCommand
                SET CommandStatus = N'SUCCEEDED',
                    ActiveSlot = NULL,
                    CompletedAtUtc = SYSUTCDATETIME()
                WHERE CommandId = @CommandId
                  AND CommandStatus = N'RUNNING'
                  AND ActiveSlot = 1;
                """,
                new
                {
                    Enabled = enabled,
                    command.StreamId,
                    command.CommandId,
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

    internal async Task<CsdtRealtimeRunHandle?> TryStartRunAsync(
        long streamId,
        string runType,
        string actor,
        long? fromVersion,
        long? toVersion,
        Guid? commandId,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var active = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(1)
                FROM dbo.App_CsdtRealtimeRun WITH (UPDLOCK, HOLDLOCK)
                WHERE StreamId = @StreamId AND ActiveSlot = 1;
                """,
                new { StreamId = streamId },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            if (active > 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.App_CsdtRealtimeRun
                (
                    RunId, StreamId, RunType, RunStatus, ActiveSlot,
                    FromVersion, ToVersion, StartedAtUtc, Actor
                )
                VALUES
                (
                    @RunId, @StreamId, @RunType, N'RUNNING', 1,
                    @FromVersion, @ToVersion, SYSUTCDATETIME(), @Actor
                );

                UPDATE dbo.App_CsdtRealtimeStream
                SET StreamStatus = CASE
                        WHEN @RunType = N'REVERSE' THEN StreamStatus
                        WHEN @RunType = N'BASELINE' THEN N'BASELINING'
                        ELSE N'CATCHING_UP'
                    END,
                    BaselineStatus = CASE
                        WHEN @RunType = N'BASELINE' THEN N'RUNNING'
                        ELSE BaselineStatus
                    END,
                    LastStartedAtUtc = CASE
                        WHEN @RunType = N'REVERSE' THEN LastStartedAtUtc
                        ELSE SYSUTCDATETIME()
                    END,
                    LastErrorCode = CASE
                        WHEN @RunType = N'REVERSE' THEN LastErrorCode
                        ELSE NULL
                    END,
                    LastErrorMessage = CASE
                        WHEN @RunType = N'REVERSE' THEN LastErrorMessage
                        ELSE NULL
                    END,
                    UpdatedAtUtc = CASE
                        WHEN @RunType = N'REVERSE' THEN UpdatedAtUtc
                        ELSE SYSUTCDATETIME()
                    END
                WHERE StreamId = @StreamId;

                UPDATE dbo.App_CsdtRealtimeCommand
                SET RunId = @RunId
                WHERE CommandId = @CommandId
                  AND CommandStatus = N'RUNNING'
                  AND ActiveSlot = 1;
                """,
                new
                {
                    RunId = runId,
                    StreamId = streamId,
                    RunType = runType,
                    FromVersion = fromVersion,
                    ToVersion = toVersion,
                    Actor = actor,
                    CommandId = commandId,
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return new CsdtRealtimeRunHandle(runId, streamId, runType, fromVersion, toVersion);
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    internal async Task BeginDomainAsync(
        CsdtRealtimeRunHandle run,
        CsdtRealtimeDomainDefinition domain,
        long? fromVersion,
        long toVersion,
        long minimumValidVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.App_CsdtRealtimeDomainState
            SET DomainStatus = CASE WHEN @RunType = N'BASELINE' THEN N'BASELINING' ELSE N'CATCHING_UP' END,
                BaselineStatus = CASE WHEN @RunType = N'BASELINE' THEN N'RUNNING' ELSE BaselineStatus END,
                CurrentSourceVersion = @ToVersion,
                MinimumValidVersion = @MinimumValidVersion,
                LagVersions = CASE
                    WHEN LastSuccessfulVersion IS NULL THEN @ToVersion
                    WHEN @ToVersion >= LastSuccessfulVersion THEN @ToVersion - LastSuccessfulVersion
                    ELSE 0
                END,
                LastStartedAtUtc = SYSUTCDATETIME(),
                LastErrorCode = NULL,
                LastErrorMessage = NULL,
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE StreamId = @StreamId AND DomainCode = @DomainCode;

            IF NOT EXISTS
            (
                SELECT 1 FROM dbo.App_CsdtRealtimeRunDomain
                WHERE RunId = @RunId AND DomainCode = @DomainCode
            )
            BEGIN
                INSERT INTO dbo.App_CsdtRealtimeRunDomain
                (
                    RunId, DomainCode, DomainStatus, FromVersion, ToVersion, StartedAtUtc
                )
                VALUES
                (
                    @RunId, @DomainCode, N'RUNNING', @FromVersion, @ToVersion, SYSUTCDATETIME()
                );
            END;
            """,
            new
            {
                run.RunId,
                run.StreamId,
                RunType = run.RunType,
                DomainCode = domain.Name,
                FromVersion = fromVersion,
                ToVersion = toVersion,
                MinimumValidVersion = minimumValidVersion,
            },
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    internal async Task CompleteDomainAsync(
        CsdtRealtimeRunHandle run,
        CsdtRealtimeDomainDefinition domain,
        CsdtRealtimeWriteResult result,
        long toVersion,
        long minimumValidVersion,
        IReadOnlyList<CsdtRealtimeChange> tombstones,
        CancellationToken cancellationToken,
        bool inferMissingIdentities = false)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            foreach (var entity in result.Entities)
            {
                var keyHash = CsdtRealtimeTargetWriter.HashKey(entity.KeyJson);
                await connection.ExecuteAsync(new CommandDefinition(
                    EntityUpsertSql,
                    new
                    {
                        run.StreamId,
                        DomainCode = domain.Name,
                        EntityKeyHash = keyHash,
                        EntityKey = entity.KeyJson,
                        SourceVersion = toVersion,
                        entity.SourceHash,
                        entity.TargetHash,
                        LastAction = run.RunType == "RECONCILE" ? "RECONCILE" : "UPDATE",
                        run.RunId,
                    },
                    transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
            }

            var currentSourceIdentities = result.Entities
                .Select(entity => entity.KeyJson)
                .Concat(result.Conflicts.Select(conflict => conflict.KeyJson))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var persistedTombstones = await PersistSourceIdentitiesAndTombstonesAsync(
                connection,
                transaction,
                run,
                domain,
                currentSourceIdentities,
                tombstones,
                toVersion,
                inferMissingIdentities,
                cancellationToken);

            foreach (var conflict in result.Conflicts)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    ConflictInsertSql,
                    new
                    {
                        run.StreamId,
                        run.RunId,
                        DomainCode = domain.Name,
                        EntityKeyHash = CsdtRealtimeTargetWriter.HashKey(conflict.KeyJson),
                        EntityKey = BuildDiagnosticIdentity(conflict.KeyJson),
                        ConflictCode = conflict.Code,
                        DetailJson = JsonSerializer.Serialize(new
                        {
                            conflict.Message,
                            Columns = conflict.Columns ?? [],
                            Identity = BuildDiagnosticIdentity(conflict.KeyJson),
                        }),
                        SourceVersion = toVersion,
                    },
                    transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.App_CsdtRealtimeDomainState
                SET DomainStatus = N'RUNNING',
                    BaselineStatus = CASE WHEN @RunType = N'BASELINE' THEN N'COMPLETED' ELSE BaselineStatus END,
                    BaselineVersion = CASE WHEN @RunType = N'BASELINE' THEN @ToVersion ELSE BaselineVersion END,
                    LastSuccessfulVersion = @ToVersion,
                    CurrentSourceVersion = @ToVersion,
                    MinimumValidVersion = @MinimumValidVersion,
                    LagVersions = 0,
                    SourceRows = @SourceRows,
                    TargetRows = @TargetRows,
                    BaselineRows = CASE WHEN @RunType = N'BASELINE' THEN @SourceRows ELSE BaselineRows END,
                    InsertedRows = InsertedRows + @InsertedRows,
                    UpdatedRows = UpdatedRows + @UpdatedRows,
                    SkippedRows = SkippedRows + @SkippedRows,
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
                    SourceRows = @SourceRows,
                    InsertedRows = @InsertedRows,
                    UpdatedRows = @UpdatedRows,
                    SkippedRows = @SkippedRows,
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
                    RunType = run.RunType,
                    DomainCode = domain.Name,
                    ToVersion = toVersion,
                    MinimumValidVersion = minimumValidVersion,
                    result.SourceRows,
                    result.TargetRows,
                    result.InsertedRows,
                    result.UpdatedRows,
                    result.SkippedRows,
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

    internal async Task CompleteNoChangeDomainAsync(
        CsdtRealtimeRunHandle run,
        CsdtRealtimeDomainDefinition domain,
        long toVersion,
        long minimumValidVersion,
        CancellationToken cancellationToken)
    {
        await CompleteCheckpointOnlyDomainAsync(
            run,
            domain,
            toVersion,
            minimumValidVersion,
            [],
            cancellationToken);
    }

    internal async Task FailDomainAsync(
        CsdtRealtimeRunHandle run,
        CsdtRealtimeDomainDefinition domain,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var message = SanitizeError(exception.Message);
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.App_CsdtRealtimeDomainState
            SET DomainStatus = N'ERROR',
                BaselineStatus = CASE WHEN @RunType = N'BASELINE' THEN N'FAILED' ELSE BaselineStatus END,
                ErrorRows = ErrorRows + 1,
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
                LastCompletedAtUtc = SYSUTCDATETIME(),
                LastErrorCode = N'DOMAIN_FAILED',
                LastErrorMessage = @Message,
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE StreamId = @StreamId AND DomainCode = @DomainCode;

            UPDATE dbo.App_CsdtRealtimeRunDomain
            SET DomainStatus = N'FAILED',
                CompletedAtUtc = SYSUTCDATETIME(),
                ErrorRows = ErrorRows + 1,
                ErrorCode = N'DOMAIN_FAILED',
                ErrorMessage = @Message
            WHERE RunId = @RunId AND DomainCode = @DomainCode;
            """,
            new
            {
                run.RunId,
                run.StreamId,
                RunType = run.RunType,
                DomainCode = domain.Name,
                Message = message,
            },
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    internal async Task CompleteRunAsync(
        CsdtRealtimeRunHandle run,
        Guid? commandId,
        bool hasMandatoryFailure,
        bool hasOptionalFailure,
        long currentVersion,
        long minimumValidVersion,
        CancellationToken cancellationToken)
    {
        var runStatus = hasMandatoryFailure
            ? "FAILED"
            : hasOptionalFailure
                ? "PARTIAL"
                : "SUCCEEDED";
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                DECLARE
                    @InsertedRows bigint,
                    @UpdatedRows bigint,
                    @SkippedRows bigint,
                    @ErrorRows bigint,
                    @TombstoneRows bigint;

                SELECT
                    @InsertedRows = ISNULL(SUM(InsertedRows), 0),
                    @UpdatedRows = ISNULL(SUM(UpdatedRows), 0),
                    @SkippedRows = ISNULL(SUM(SkippedRows), 0),
                    @ErrorRows = ISNULL(SUM(ErrorRows), 0),
                    @TombstoneRows = ISNULL(SUM(TombstoneRows), 0)
                FROM dbo.App_CsdtRealtimeRunDomain
                WHERE RunId = @RunId;

                UPDATE dbo.App_CsdtRealtimeRun
                SET RunStatus = @RunStatus,
                    ActiveSlot = NULL,
                    ToVersion = @CurrentVersion,
                    MinimumValidVersion = @MinimumValidVersion,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    InsertedRows = @InsertedRows,
                    UpdatedRows = @UpdatedRows,
                    SkippedRows = @SkippedRows,
                    ErrorRows = @ErrorRows,
                    TombstoneRows = @TombstoneRows,
                    ErrorCode = CASE WHEN @HasMandatoryFailure = 1 THEN N'MANDATORY_DOMAIN_FAILED' ELSE NULL END,
                    ErrorMessage = CASE WHEN @HasMandatoryFailure = 1 THEN N'At least one mandatory domain failed.' ELSE NULL END
                WHERE RunId = @RunId AND ActiveSlot = 1;

                UPDATE stream
                SET StreamStatus = CASE
                        WHEN stream.IsEnabled = 0 THEN N'DISABLED'
                        WHEN @HasMandatoryFailure = 1 THEN N'ERROR'
                        ELSE N'RUNNING'
                    END,
                    BaselineStatus = CASE
                        WHEN @RunType = N'BASELINE' AND @HasMandatoryFailure = 1 THEN N'FAILED'
                        WHEN @RunType = N'BASELINE' THEN N'COMPLETED'
                        ELSE stream.BaselineStatus
                    END,
                    BaselineVersion = CASE
                        WHEN @RunType = N'BASELINE' AND @HasMandatoryFailure = 0 THEN @CurrentVersion
                        ELSE stream.BaselineVersion
                    END,
                    LastSuccessfulVersion =
                    (
                        SELECT MIN(domain.LastSuccessfulVersion)
                        FROM dbo.App_CsdtRealtimeDomainState AS domain
                        WHERE domain.StreamId = stream.StreamId
                          AND domain.IsOptional = 0
                    ),
                    CurrentSourceVersion = @CurrentVersion,
                    MinimumValidVersion = @MinimumValidVersion,
                    LagVersions = CASE
                        WHEN @HasMandatoryFailure = 1 THEN stream.LagVersions
                        ELSE 0
                    END,
                    LastCompletedAtUtc = SYSUTCDATETIME(),
                    LastSuccessAtUtc = CASE
                        WHEN @HasMandatoryFailure = 0 THEN SYSUTCDATETIME()
                        ELSE stream.LastSuccessAtUtc
                    END,
                    LastReconciledAtUtc = CASE
                        WHEN @RunType = N'RECONCILE' AND @HasMandatoryFailure = 0 THEN SYSUTCDATETIME()
                        ELSE stream.LastReconciledAtUtc
                    END,
                    RetryCount = CASE WHEN @HasMandatoryFailure = 0 THEN 0 ELSE stream.RetryCount + 1 END,
                    NextRetryAtUtc = CASE
                        WHEN @HasMandatoryFailure = 0 THEN NULL
                        ELSE DATEADD
                        (
                            second,
                            CASE
                                WHEN stream.RetryCount = 0 THEN 5
                                WHEN stream.RetryCount = 1 THEN 15
                                WHEN stream.RetryCount = 2 THEN 30
                                ELSE 60
                            END,
                            SYSUTCDATETIME()
                        )
                    END,
                    LastErrorCode = CASE WHEN @HasMandatoryFailure = 1 THEN N'MANDATORY_DOMAIN_FAILED' ELSE NULL END,
                    LastErrorMessage = CASE
                        WHEN @HasMandatoryFailure = 1 THEN N'At least one mandatory domain failed.'
                        WHEN @HasOptionalFailure = 1 THEN N'Optional domain needs review; mandatory data continues.'
                        ELSE NULL
                    END,
                    UpdatedAtUtc = SYSUTCDATETIME()
                FROM dbo.App_CsdtRealtimeStream AS stream
                WHERE stream.StreamId = @StreamId;

                UPDATE dbo.App_CsdtRealtimeCommand
                SET CommandStatus = CASE WHEN @HasMandatoryFailure = 1 THEN N'FAILED' ELSE N'SUCCEEDED' END,
                    ActiveSlot = NULL,
                    CompletedAtUtc = SYSUTCDATETIME(),
                    ErrorCode = CASE WHEN @HasMandatoryFailure = 1 THEN N'MANDATORY_DOMAIN_FAILED' ELSE NULL END,
                    ErrorMessage = CASE WHEN @HasMandatoryFailure = 1 THEN N'At least one mandatory domain failed.' ELSE NULL END
                WHERE CommandId = @CommandId
                  AND CommandStatus = N'RUNNING'
                  AND ActiveSlot = 1;
                """,
                new
                {
                    run.RunId,
                    run.StreamId,
                    run.RunType,
                    RunStatus = runStatus,
                    HasMandatoryFailure = hasMandatoryFailure,
                    HasOptionalFailure = hasOptionalFailure,
                    CurrentVersion = currentVersion,
                    MinimumValidVersion = minimumValidVersion,
                    CommandId = commandId,
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

    internal async Task FailCommandWithoutRunAsync(
        CsdtRealtimeClaimedCommand command,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.App_CsdtRealtimeCommand
            SET CommandStatus = N'FAILED',
                ActiveSlot = NULL,
                CompletedAtUtc = SYSUTCDATETIME(),
                ErrorCode = N'COMMAND_FAILED',
                ErrorMessage = @Message
            WHERE CommandId = @CommandId AND ActiveSlot = 1;
            """,
            new
            {
                command.CommandId,
                Message = SanitizeError(exception.Message),
            },
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    private static async Task<IReadOnlyList<CsdtRealtimeChange>> PersistSourceIdentitiesAndTombstonesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtRealtimeRunHandle run,
        CsdtRealtimeDomainDefinition domain,
        IReadOnlyCollection<string> currentSourceIdentities,
        IReadOnlyList<CsdtRealtimeChange> tombstones,
        long toVersion,
        bool inferMissingIdentities,
        CancellationToken cancellationToken)
    {
        var current = currentSourceIdentities
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<CsdtRealtimeSourceIdentityInventoryRow> inventory = [];
        if (inferMissingIdentities)
        {
            var rows = await connection.QueryAsync<CsdtRealtimeSourceIdentityInventoryRow>(
                new CommandDefinition(
                    """
                    SELECT SourceIdentity, IdentityStatus, LastSeenVersion
                    FROM dbo.App_CsdtRealtimeSourceIdentity WITH (UPDLOCK, HOLDLOCK)
                    WHERE StreamId = @StreamId
                      AND DomainCode = @DomainCode
                      AND IdentityStatus = N'PRESENT';
                    """,
                    new
                    {
                        run.StreamId,
                        DomainCode = domain.Name,
                    },
                    transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
            inventory = rows.AsList();
        }

        var inferredTombstones = CsdtRealtimeSourceIdentityPlanner.InferMissingIdentities(
            inventory,
            current,
            inferMissingIdentities);
        var allTombstones = tombstones
            .Concat(inferredTombstones)
            .DistinctBy(item => (item.KeyJson, item.Version))
            .ToArray();

        foreach (var sourceIdentity in current)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                SourceIdentityUpsertSql,
                new
                {
                    run.StreamId,
                    DomainCode = domain.Name,
                    SourceIdentityHash = CsdtRealtimeTargetWriter.HashKey(sourceIdentity),
                    SourceIdentity = sourceIdentity,
                    LastSeenVersion = toVersion,
                    run.RunId,
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        }

        foreach (var tombstone in allTombstones)
        {
            var keyHash = CsdtRealtimeTargetWriter.HashKey(tombstone.KeyJson);
            var inferred = string.Equals(
                tombstone.Operation,
                CsdtRealtimeSourceIdentityPlanner.InferredDeleteOperation,
                StringComparison.Ordinal);
            await connection.ExecuteAsync(new CommandDefinition(
                TombstoneUpsertSql,
                new
                {
                    run.StreamId,
                    DomainCode = domain.Name,
                    EntityKeyHash = keyHash,
                    EntityKey = tombstone.KeyJson,
                    SourceVersion = tombstone.Version,
                    SourceKeyJson = tombstone.KeyJson,
                    Note = inferred ? "INFERRED_EXPIRED_CHECKPOINT" : null,
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                SourceIdentityMarkMissingSql,
                new
                {
                    run.StreamId,
                    DomainCode = domain.Name,
                    SourceIdentityHash = keyHash,
                    SourceIdentity = tombstone.KeyJson,
                    MissingSinceVersion = toVersion,
                    run.RunId,
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        }

        return allTombstones;
    }

    internal async Task<bool> EntityBelongsToStreamAsync(
        long streamId,
        string domainCode,
        string keyJson,
        CancellationToken cancellationToken)
    {
        var keyHash = CsdtRealtimeTargetWriter.HashKey(keyJson);
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        var stored = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT SourceIdentity
            FROM dbo.App_CsdtRealtimeSourceIdentity
            WHERE StreamId = @StreamId
              AND DomainCode = @DomainCode
              AND SourceIdentityHash = @EntityKeyHash
              AND IdentityStatus = N'PRESENT';
            """,
            new
            {
                StreamId = streamId,
                DomainCode = domainCode,
                EntityKeyHash = keyHash,
            },
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        return string.Equals(stored, keyJson, StringComparison.Ordinal);
    }

    internal async Task<IReadOnlyList<CsdtRealtimeEntityLedgerRow>> GetEntityLedgerAsync(
        long streamId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenStateConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<CsdtRealtimeEntityLedgerRow>(new CommandDefinition(
            """
            SELECT
                DomainCode, EntityKey, EntityKeyHash, SourceHash, TargetHash, SourceVersion
            FROM dbo.App_CsdtRealtimeEntityState
            WHERE StreamId = @StreamId;
            """,
            new { StreamId = streamId },
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private static async Task SafeRollbackAsync(SqlTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Keep the original error.
        }
    }

    private static string SanitizeError(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Realtime operation failed.";
        }

        var safe = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (safe.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            safe.Contains("connection string", StringComparison.OrdinalIgnoreCase) ||
            safe.Contains("data source=", StringComparison.OrdinalIgnoreCase))
        {
            return "Sensitive realtime failure details were omitted.";
        }

        return safe.Length <= 2000 ? safe : safe[..2000];
    }

    private static string BuildDiagnosticIdentity(string keyJson)
        => "sha256:" + Convert.ToHexString(CsdtRealtimeTargetWriter.HashKey(keyJson));

    private const string EntityUpsertSql = """
        UPDATE dbo.App_CsdtRealtimeEntityState WITH (UPDLOCK, HOLDLOCK)
        SET EntityKey = @EntityKey,
            SourceVersion = @SourceVersion,
            SourceHash = @SourceHash,
            TargetHash = @TargetHash,
            LastAction = @LastAction,
            LastSynchronizedAtUtc = SYSUTCDATETIME(),
            LastVerifiedAtUtc = SYSUTCDATETIME(),
            LastRunId = @RunId
        WHERE StreamId = @StreamId
          AND DomainCode = @DomainCode
          AND EntityKeyHash = @EntityKeyHash;

        IF @@ROWCOUNT = 0
        BEGIN
            INSERT INTO dbo.App_CsdtRealtimeEntityState
            (
                StreamId, DomainCode, EntityKeyHash, EntityKey,
                SourceVersion, SourceHash, TargetHash, LastAction,
                LastSynchronizedAtUtc, LastVerifiedAtUtc, LastRunId
            )
            VALUES
            (
                @StreamId, @DomainCode, @EntityKeyHash, @EntityKey,
                @SourceVersion, @SourceHash, @TargetHash, @LastAction,
                SYSUTCDATETIME(), SYSUTCDATETIME(), @RunId
            );
        END;
        """;

    private const string SourceIdentityUpsertSql = """
        IF EXISTS
        (
            SELECT 1
            FROM dbo.App_CsdtRealtimeSourceIdentity WITH (UPDLOCK, HOLDLOCK)
            WHERE StreamId = @StreamId
              AND DomainCode = @DomainCode
              AND SourceIdentityHash = @SourceIdentityHash
              AND SourceIdentity <> @SourceIdentity
        )
            THROW 527484, 'A CSDT realtime source identity hash collision was detected.', 1;

        UPDATE dbo.App_CsdtRealtimeSourceIdentity WITH (UPDLOCK, HOLDLOCK)
        SET SourceIdentity = @SourceIdentity,
            IdentityStatus = N'PRESENT',
            LastSeenVersion = @LastSeenVersion,
            LastSeenAtUtc = SYSUTCDATETIME(),
            MissingSinceVersion = NULL,
            MissingSinceAtUtc = NULL,
            LastRunId = @RunId,
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE StreamId = @StreamId
          AND DomainCode = @DomainCode
          AND SourceIdentityHash = @SourceIdentityHash;

        IF @@ROWCOUNT = 0
        BEGIN
            INSERT INTO dbo.App_CsdtRealtimeSourceIdentity
            (
                StreamId, DomainCode, SourceIdentityHash, SourceIdentity,
                IdentityStatus, FirstObservedVersion, FirstObservedAtUtc,
                LastSeenVersion, LastSeenAtUtc, LastRunId, UpdatedAtUtc
            )
            VALUES
            (
                @StreamId, @DomainCode, @SourceIdentityHash, @SourceIdentity,
                N'PRESENT', @LastSeenVersion, SYSUTCDATETIME(),
                @LastSeenVersion, SYSUTCDATETIME(), @RunId, SYSUTCDATETIME()
            );
        END;
        """;

    private const string SourceIdentityMarkMissingSql = """
        UPDATE dbo.App_CsdtRealtimeSourceIdentity WITH (UPDLOCK, HOLDLOCK)
        SET IdentityStatus = N'MISSING',
            MissingSinceVersion = CASE
                WHEN IdentityStatus = N'PRESENT' THEN @MissingSinceVersion
                ELSE MissingSinceVersion
            END,
            MissingSinceAtUtc = CASE
                WHEN IdentityStatus = N'PRESENT' THEN SYSUTCDATETIME()
                ELSE MissingSinceAtUtc
            END,
            LastRunId = @RunId,
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE StreamId = @StreamId
          AND DomainCode = @DomainCode
          AND SourceIdentityHash = @SourceIdentityHash
          AND SourceIdentity = @SourceIdentity;
        """;

    private const string TombstoneUpsertSql = """
        UPDATE dbo.App_CsdtRealtimeTombstone WITH (UPDLOCK, HOLDLOCK)
        SET LastSeenAtUtc = SYSUTCDATETIME(),
            SourceKeyJson = @SourceKeyJson,
            Note = COALESCE(@Note, Note)
        WHERE StreamId = @StreamId
          AND DomainCode = @DomainCode
          AND EntityKeyHash = @EntityKeyHash
          AND SourceVersion = @SourceVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            INSERT INTO dbo.App_CsdtRealtimeTombstone
            (
                StreamId, DomainCode, EntityKeyHash, EntityKey,
                SourceVersion, SourceKeyJson, Note
            )
            VALUES
            (
                @StreamId, @DomainCode, @EntityKeyHash, @EntityKey,
                @SourceVersion, @SourceKeyJson, @Note
            );
        END;
        """;

    private const string ConflictInsertSql = """
        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.App_CsdtRealtimeConflict WITH (UPDLOCK, HOLDLOCK)
            WHERE StreamId = @StreamId
              AND DomainCode = @DomainCode
              AND EntityKeyHash = @EntityKeyHash
              AND Direction = N'V2_TO_V1'
              AND ConflictStatus = N'PENDING'
        )
        BEGIN
            INSERT INTO dbo.App_CsdtRealtimeConflict
            (
                StreamId, RunId, Direction, DomainCode, EntityKeyHash,
                EntityKey, ConflictCode, ConflictStatus, SourceVersion, DetailJson
            )
            VALUES
            (
                @StreamId, @RunId, N'V2_TO_V1', @DomainCode, @EntityKeyHash,
                @EntityKey, @ConflictCode, N'PENDING', @SourceVersion, @DetailJson
            );
        END;
        """;
}

using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Rt03;

namespace QLHV.Infrastructure.Sync.Rt03;

internal sealed class Rt03FullConvergenceStateStore :
    IRt03FullConvergenceStateStore
{
    private readonly IConnectionSettingsProvider _connections;

    public Rt03FullConvergenceStateStore(
        IConnectionSettingsProvider connections)
    {
        _connections = connections;
    }

    public async Task<Rt03RecoveryPreflightState> ReadPreflightAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken = default)
    {
        _ = QlhvOperationSourceCatalog.ResolveSourceTypeFromProfile(
            sourceProfileCode);
        await using var connection = await OpenAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<PreflightRow>(
            new CommandDefinition(
                PreflightSql,
                new { SourceProfileCode = sourceProfileCode },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        return new(
            row.CheckpointVersion,
            row.SourceDatabaseGuid,
            row.ActiveAutoSync == 0,
            row.ActiveOperations == 0,
            row.RecoverySchemaReady);
    }

    public async Task BeginOrResumeAsync(
        Rt03FullConvergenceRecoveryRequest request,
        Guid sourceDatabaseGuid,
        long anchorVersion,
        string mappingFingerprint,
        string sourceSchemaFingerprint,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "dbo.usp_App_Rt03BeginFullConvergence",
            new
            {
                request.RecoveryId,
                request.SourceProfileCode,
                SourceDatabaseGuid = sourceDatabaseGuid,
                CheckpointBefore = request.ExpectedCheckpoint,
                AnchorVersion = anchorVersion,
                MappingFingerprint = mappingFingerprint,
                SourceSchemaFingerprint = sourceSchemaFingerprint,
            },
            commandType: System.Data.CommandType.StoredProcedure,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    public async Task RecordDomainAsync(
        Guid recoveryId,
        Rt03RecoveryDomainResult result,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "dbo.usp_App_Rt03RecordFullConvergenceDomain",
            new
            {
                RecoveryId = recoveryId,
                DomainCode = result.Domain,
                result.SequenceOrder,
                result.SourceRows,
                result.InsertedRows,
                result.UpdatedRows,
                result.InactiveRows,
                result.MissingRows,
                result.ManualReviewRows,
                result.NoChangeRows,
                result.VerificationHash,
            },
            commandType: System.Data.CommandType.StoredProcedure,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    public async Task MarkVerifiedAsync(
        Guid recoveryId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "dbo.usp_App_Rt03VerifyFullConvergence",
            new { RecoveryId = recoveryId },
            commandType: System.Data.CommandType.StoredProcedure,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    public async Task FinalizeAsync(
        Guid recoveryId,
        string verificationHash,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "dbo.usp_App_Rt03FinalizeFullConvergence",
            new
            {
                RecoveryId = recoveryId,
                VerificationHash = verificationHash,
            },
            commandType: System.Data.CommandType.StoredProcedure,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    private async Task<SqlConnection> OpenAsync(
        CancellationToken cancellationToken)
    {
        var resolved =
            await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!resolved.IsUsable || string.IsNullOrWhiteSpace(resolved.ConnectionString))
        {
            throw new Rt03SafetyException(
                Rt03Errors.ConfigurationRejected,
                "QLHV_APP connection is unavailable for recovery state.");
        }

        var connection = new SqlConnection(resolved.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class PreflightRow
    {
        public long CheckpointVersion { get; init; }
        public Guid SourceDatabaseGuid { get; init; }
        public int ActiveAutoSync { get; init; }
        public int ActiveOperations { get; init; }
        public bool RecoverySchemaReady { get; init; }
    }

    internal const string PreflightSql = """
        SELECT checkpointRow.SourceChangeTrackingVersion CheckpointVersion,
               checkpointRow.SourceDatabaseGuid,
               (SELECT COUNT(1)
                FROM dbo.App_QlhvAutoSyncRun
                WHERE ActiveSlot=1 AND Status IN(N'QUEUED',N'RUNNING')
                  AND CompletedAtUtc IS NULL) ActiveAutoSync,
               (SELECT COUNT(1)
                FROM dbo.App_QlhvSyncOperationHistory
                WHERE Status IN(N'QUEUED',N'RUNNING')) ActiveOperations,
               CONVERT(bit,CASE WHEN
                    OBJECT_ID(N'dbo.App_Rt03FullConvergenceSession',N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.App_Rt03FullConvergenceDomain',N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.App_Rt03FullConvergenceMarker',N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.App_QlhvDirectRealtimeApplyMarker',N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.usp_App_Rt03BeginFullConvergence',N'P') IS NOT NULL
                    AND OBJECT_ID(N'dbo.usp_App_Rt03FinalizeFullConvergence',N'P') IS NOT NULL
                    THEN 1 ELSE 0 END) RecoverySchemaReady
        FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint checkpointRow
        WHERE checkpointRow.Mode=N'DIRECT_REALTIME_APPLY'
          AND checkpointRow.EnvironmentId=N'PRODUCTION'
          AND checkpointRow.SourceProfileCode=@SourceProfileCode;
        """;
}

using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Rt03;

namespace QLHV.Infrastructure.Sync.Rt03;

/// <summary>
/// Holds the existing Auto Sync lock for the complete RT-03 worker lifetime.
/// Per-profile source-operation locks are acquired by the worker around each
/// OTO or MOTO cycle, preserving independent profile partitions.
/// </summary>
public sealed class QlhvDirectRealtimeGlobalLock : IQlhvDirectRealtimeGlobalLock
{
    internal static IReadOnlyList<string> LifetimeLockResources { get; } =
    [
        QlhvSqlAutoSyncGlobalLock.LockResource,
    ];

    private readonly IConnectionSettingsProvider _connections;

    public QlhvDirectRealtimeGlobalLock(IConnectionSettingsProvider connections)
    {
        _connections = connections;
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        CancellationToken cancellationToken = default)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new Rt03SafetyException(
                Rt03Errors.ProductionIdentityRejected,
                "QLHV_APP connection is unavailable for the RT-03 lifetime lock.");
        }

        var connection = new SqlConnection(target.ConnectionString);
        var acquiredResources = new List<string>(LifetimeLockResources.Count);
        try
        {
            await connection.OpenAsync(cancellationToken);
            foreach (var resource in LifetimeLockResources)
            {
                var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    QlhvSqlAutoSyncGlobalLock.AcquireSql,
                    new { Resource = resource },
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
                if (result < 0)
                {
                    await ReleaseAllAsync(
                        connection,
                        acquiredResources,
                        CancellationToken.None);
                    await connection.DisposeAsync();
                    return null;
                }

                acquiredResources.Add(resource);
            }

            var activeAutoSyncRows = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    ActiveAutoSyncSql,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
            if (activeAutoSyncRows != 0)
            {
                await ReleaseAllAsync(
                    connection,
                    acquiredResources,
                    CancellationToken.None);
                await connection.DisposeAsync();
                return null;
            }

            return new Lease(connection, acquiredResources.ToArray());
        }
        catch
        {
            try
            {
                await ReleaseAllAsync(
                    connection,
                    acquiredResources,
                    CancellationToken.None);
            }
            catch
            {
                SqlConnection.ClearPool(connection);
            }

            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease : IAsyncDisposable
    {
        private SqlConnection? _connection;
        private readonly IReadOnlyList<string> _resources;

        public Lease(
            SqlConnection connection,
            IReadOnlyList<string> resources)
        {
            _connection = connection;
            _resources = resources;
        }

        public async ValueTask DisposeAsync()
        {
            var connection = Interlocked.Exchange(ref _connection, null);
            if (connection is null)
            {
                return;
            }

            try
            {
                if (connection.State == System.Data.ConnectionState.Open)
                {
                    await ReleaseAllAsync(
                        connection,
                        _resources,
                        CancellationToken.None);
                }
            }
            catch
            {
                SqlConnection.ClearPool(connection);
                throw;
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }

    private static async Task ReleaseAllAsync(
        SqlConnection connection,
        IReadOnlyList<string> resources,
        CancellationToken cancellationToken)
    {
        for (var index = resources.Count - 1; index >= 0; index--)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                ReleaseSql,
                new { Resource = resources[index] },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        }
    }

    internal const string ReleaseSql = """
        EXEC sys.sp_releaseapplock
            @Resource=@Resource,
            @LockOwner=N'Session',
            @DbPrincipal=N'public';
        """;

    internal const string ActiveAutoSyncSql = """
        SELECT
          (SELECT COUNT(1) FROM dbo.App_QlhvAutoSyncRun
           WHERE ActiveSlot=1
             AND Status IN (N'QUEUED',N'RUNNING')
             AND CompletedAtUtc IS NULL
             AND UpdatedAtUtc >= DATEADD(SECOND, -120, SYSUTCDATETIME())
             AND (CurrentSourceType IS NOT NULL OR CurrentStage IS NOT NULL))
          +
          (SELECT COUNT(1) FROM dbo.App_QlhvSyncOperationHistory
           WHERE Status IN (N'QUEUED',N'RUNNING'));
        """;
}

using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;

namespace QLHV.Infrastructure.Sync;

public sealed class QlhvSqlAutoSyncGlobalLock : IQlhvAutoSyncGlobalLock
{
    internal const string LockResource = "QLHV:CSDT_AUTO_SYNC";

    private readonly IConnectionSettingsProvider _connections;

    public QlhvSqlAutoSyncGlobalLock(IConnectionSettingsProvider connections)
    {
        _connections = connections;
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        CancellationToken cancellationToken = default)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new QlhvAutoSyncStoreUnavailableException(
                "QLHV_APP connection chua san sang cho Auto Sync lock.");
        }

        var connection = new SqlConnection(target.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                AcquireSql,
                new { Resource = LockResource },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            if (result < 0)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new Lease(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease : IAsyncDisposable
    {
        private SqlConnection? _connection;

        public Lease(SqlConnection connection)
        {
            _connection = connection;
        }

        public async ValueTask DisposeAsync()
        {
            var connection = Interlocked.Exchange(ref _connection, null);
            if (connection is null)
            {
                return;
            }

            var clearPool = false;
            try
            {
                if (connection.State == System.Data.ConnectionState.Open)
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        ReleaseSql,
                        new { Resource = LockResource },
                        commandTimeout: 30));
                }
            }
            catch
            {
                clearPool = true;
            }
            finally
            {
                if (clearPool)
                {
                    SqlConnection.ClearPool(connection);
                }

                try
                {
                    await connection.DisposeAsync();
                }
                catch
                {
                    SqlConnection.ClearPool(connection);
                    connection.Dispose();
                }
            }
        }
    }

    internal const string AcquireSql = @"
DECLARE @LockResult int;
EXEC @LockResult = sys.sp_getapplock
    @Resource = @Resource,
    @LockMode = N'Exclusive',
    @LockOwner = N'Session',
    @LockTimeout = 0,
    @DbPrincipal = N'public';
SELECT @LockResult;";

    private const string ReleaseSql = @"
EXEC sys.sp_releaseapplock
    @Resource = @Resource,
    @LockOwner = N'Session',
    @DbPrincipal = N'public';";
}

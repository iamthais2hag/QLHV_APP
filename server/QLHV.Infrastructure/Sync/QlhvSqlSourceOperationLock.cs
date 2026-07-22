using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;

namespace QLHV.Infrastructure.Sync;

public sealed class QlhvSqlSourceOperationLock : IQlhvSourceOperationLock
{
    private readonly IConnectionSettingsProvider _connections;

    public QlhvSqlSourceOperationLock(IConnectionSettingsProvider connections)
    {
        _connections = connections;
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        QlhvOperationSourceDefinition source,
        CancellationToken cancellationToken = default)
    {
        var allowed = QlhvOperationSourceCatalog.GetRequired(source.SourceType);
        if (source != allowed)
        {
            throw new InvalidOperationException("Operation lock source khong nam trong allowlist.");
        }

        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new InvalidOperationException("QLHV_APP connection chua san sang cho operation lock.");
        }

        var connection = new SqlConnection(target.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                AcquireSql,
                new { Resource = allowed.LockResource },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            if (result < 0)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new Lease(connection, allowed.LockResource);
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
        private readonly string _resource;

        public Lease(SqlConnection connection, string resource)
        {
            _connection = connection;
            _resource = resource;
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
                        new { Resource = _resource },
                        commandTimeout: 30));
                }
            }
            catch
            {
                // Never return a possibly lock-owning physical session to the pool.
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
                    try
                    {
                        connection.Dispose();
                    }
                    catch
                    {
                        // Lock cleanup must not replace a committed operation result.
                    }
                }
            }
        }
    }

    private const string AcquireSql = @"
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

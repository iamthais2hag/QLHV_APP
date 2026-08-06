using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace QLHV.Infrastructure.Sync.Realtime;

internal sealed class CsdtRealtimeStreamLock : IAsyncDisposable
{
    internal const string ResourcePrefix = "QLHV:CSDT_REALTIME:";

    private readonly SqlConnection _connection;
    private readonly string _resource;
    private bool _released;

    private CsdtRealtimeStreamLock(SqlConnection connection, string resource)
    {
        _connection = connection;
        _resource = resource;
    }

    public static async Task<CsdtRealtimeStreamLock?> TryAcquireAsync(
        string stateConnectionString,
        string streamCode,
        CancellationToken cancellationToken)
    {
        if (streamCode is not ("OTO_V2_TO_V1" or "MOTO_V2_TO_V1"))
        {
            throw new ArgumentException("Realtime stream lock is outside the fixed allowlist.", nameof(streamCode));
        }

        var connection = new SqlConnection(stateConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var resource = ResourcePrefix + streamCode;
            var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                DECLARE @Result int;
                EXEC @Result = sys.sp_getapplock
                    @Resource = @Resource,
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Session',
                    @LockTimeout = 0,
                    @DbPrincipal = N'public';
                SELECT @Result;
                """,
                new { Resource = resource },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
            if (result < 0)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new CsdtRealtimeStreamLock(connection, resource);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        try
        {
            if (_connection.State == ConnectionState.Open)
            {
                await _connection.ExecuteAsync(new CommandDefinition(
                    """
                    DECLARE @Result int;
                    EXEC @Result = sys.sp_releaseapplock
                        @Resource = @Resource,
                        @LockOwner = N'Session',
                        @DbPrincipal = N'public';
                    """,
                    new { Resource = _resource },
                    commandTimeout: 30));
            }
        }
        catch
        {
            // Closing the SQL session releases a session-owned applock even when
            // the explicit release cannot reach SQL Server.
        }
        finally
        {
            await _connection.DisposeAsync();
        }
    }
}

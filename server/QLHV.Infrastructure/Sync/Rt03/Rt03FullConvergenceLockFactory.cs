using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Rt03;

namespace QLHV.Infrastructure.Sync.Rt03;

internal sealed class Rt03FullConvergenceLockFactory
{
    private readonly IConnectionSettingsProvider _connections;

    public Rt03FullConvergenceLockFactory(
        IConnectionSettingsProvider connections)
    {
        _connections = connections;
    }

    public async Task<Rt03FullConvergenceLockLease?> TryAcquireProfileAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken)
    {
        var resources =
            Rt03FullConvergenceLocks.ForProfile(sourceProfileCode);
        var target =
            await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable ||
            string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new Rt03SafetyException(
                Rt03Errors.ConfigurationRejected,
                "QLHV_APP connection is unavailable for recovery locks.");
        }

        var connection = new SqlConnection(target.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var lease = new Rt03FullConvergenceLockLease(
                connection,
                resources[1],
                resources.Skip(3).ToArray());
            if (!await lease.TryAcquireProfileAsync(cancellationToken))
            {
                await lease.DisposeAsync();
                return null;
            }

            return lease;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

internal sealed class Rt03FullConvergenceLockLease : IAsyncDisposable
{
    private SqlConnection? _connection;
    private readonly string _profileResource;
    private readonly IReadOnlyList<string> _domainResources;
    private readonly List<string> _acquired = [];
    private bool _domainAttempted;

    public Rt03FullConvergenceLockLease(
        SqlConnection connection,
        string profileResource,
        IReadOnlyList<string> domainResources)
    {
        _connection = connection;
        _profileResource = profileResource;
        _domainResources = domainResources;
    }

    internal Task<bool> TryAcquireProfileAsync(
        CancellationToken cancellationToken)
        => TryAcquireAsync(_profileResource, cancellationToken);

    public async Task<bool> TryAcquireDomainsAsync(
        CancellationToken cancellationToken)
    {
        if (_domainAttempted)
        {
            throw new InvalidOperationException(
                "Recovery domain locks may only be acquired once.");
        }

        _domainAttempted = true;
        foreach (var resource in _domainResources)
        {
            if (!await TryAcquireAsync(resource, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> TryAcquireAsync(
        string resource,
        CancellationToken cancellationToken)
    {
        var connection = _connection ??
            throw new ObjectDisposedException(
                nameof(Rt03FullConvergenceLockLease));
        var result = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                AcquireSql,
                new { Resource = resource },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        if (result < 0)
        {
            return false;
        }

        _acquired.Add(resource);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection is null)
        {
            return;
        }

        var releaseFailed = false;
        try
        {
            if (connection.State == System.Data.ConnectionState.Open)
            {
                for (var index = _acquired.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        await connection.ExecuteAsync(new CommandDefinition(
                            ReleaseSql,
                            new { Resource = _acquired[index] },
                            commandTimeout: 30));
                    }
                    catch
                    {
                        releaseFailed = true;
                    }
                }
            }
        }
        finally
        {
            if (releaseFailed)
            {
                SqlConnection.ClearPool(connection);
            }

            await connection.DisposeAsync();
        }
    }

    internal const string AcquireSql = """
        DECLARE @Result int;
        EXEC @Result=sys.sp_getapplock
            @Resource=@Resource,
            @LockMode=N'Exclusive',
            @LockOwner=N'Session',
            @LockTimeout=0,
            @DbPrincipal=N'public';
        SELECT @Result;
        """;

    internal const string ReleaseSql = """
        EXEC sys.sp_releaseapplock
            @Resource=@Resource,
            @LockOwner=N'Session',
            @DbPrincipal=N'public';
        """;
}

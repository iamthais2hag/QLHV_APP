using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Rt03;

namespace QLHV.Infrastructure.Sync.Rt03;

public sealed class Rt03WorkerPermissionProbe : IRt03WorkerPermissionProbe
{
    private readonly IConnectionSettingsProvider _connections;
    private readonly SyncOptions _options;

    public Rt03WorkerPermissionProbe(
        IConnectionSettingsProvider connections,
        IOptions<SyncOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw Rejected();
        }

        await using var connection = new SqlConnection(target.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var allowed = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            PermissionSql,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (!allowed)
        {
            throw Rejected();
        }
    }

    private static Rt03SafetyException Rejected() => new(
        Rt03RealtimeMasterErrors.PermissionRejected,
        "Realtime worker effective permissions do not match the master-switch contract.");

    internal const string PermissionSql = """
        SELECT CONVERT(bit, CASE WHEN
            HAS_PERMS_BY_NAME(N'dbo.App_Rt03RealtimeControl', N'OBJECT', N'SELECT')=1 AND
            HAS_PERMS_BY_NAME(N'dbo.App_Rt03RealtimeControl', N'OBJECT', N'UPDATE')=1 AND
            HAS_PERMS_BY_NAME(N'dbo.App_Rt03RealtimeControlAudit', N'OBJECT', N'INSERT')=1 AND
            HAS_PERMS_BY_NAME(N'dbo.App_Rt03RealtimeRunRequest', N'OBJECT', N'SELECT')=1 AND
            HAS_PERMS_BY_NAME(N'dbo.App_Rt03RealtimeRunRequest', N'OBJECT', N'UPDATE')=1
        THEN 1 ELSE 0 END);
        """;
}

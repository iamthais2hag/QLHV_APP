using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Runtime;
using QLHV.Application.Sync.Connections;

namespace QLHV.Infrastructure.Runtime;

/// <summary>
/// The complete database-clock contract: one bounded scalar query and no
/// durable, recovery, audit, or realtime-history reads.
/// </summary>
public sealed class SqlDatabaseTimeAuthorityProbe : IDatabaseTimeAuthorityProbe
{
    private readonly IConnectionSettingsProvider _connections;

    public SqlDatabaseTimeAuthorityProbe(IConnectionSettingsProvider connections)
    {
        _connections = connections;
    }

    public async Task<DateTimeOffset?> ReadDatabaseUtcAsync(
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            DatabaseTimeAuthorityContract.QueryTimeoutSeconds));
        try
        {
            var target = await _connections.GetQlhvAppConnectionAsync(timeout.Token);
            if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
            {
                return null;
            }

            await using var connection = new SqlConnection(target.ConnectionString);
            await connection.OpenAsync(timeout.Token);
            var value = await connection.QuerySingleAsync<DateTime>(
                new CommandDefinition(
                    DatabaseUtcSql,
                    commandTimeout: DatabaseTimeAuthorityContract.QueryTimeoutSeconds,
                    cancellationToken: timeout.Token));
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    internal const string DatabaseUtcSql = "SELECT SYSUTCDATETIME();";
}

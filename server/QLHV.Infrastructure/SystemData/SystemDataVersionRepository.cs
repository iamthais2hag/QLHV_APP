using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.SystemData;

namespace QLHV.Infrastructure.SystemData;

public sealed class SystemDataVersionRepository : ISystemDataVersionRepository
{
    private readonly IConnectionSettingsProvider _connections;

    public SystemDataVersionRepository(IConnectionSettingsProvider connections)
    {
        _connections = connections;
    }

    public async Task<SystemDataVersionDto> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new QlhvAutoSyncStoreUnavailableException(
                "QLHV_APP chua co connection dung duoc cho data version.");
        }

        try
        {
            await using var connection = new SqlConnection(target.ConnectionString);
            var result = await connection.QuerySingleOrDefaultAsync<SystemDataVersionDto>(
                new CommandDefinition(ReadSql, cancellationToken: cancellationToken));
            return result ?? throw new QlhvAutoSyncStoreUnavailableException(
                "Data version chua duoc khoi tao.");
        }
        catch (SqlException ex) when (ex.Number is 207 or 208)
        {
            throw new QlhvAutoSyncStoreUnavailableException(
                "Data version chua san sang; can chay patch tao dbo.App_DataVersion.",
                ex);
        }
        catch (SqlException ex)
        {
            throw new QlhvAutoSyncStoreUnavailableException(
                "Tam thoi khong doc duoc data version tu QLHV_APP.",
                ex);
        }
    }

    private const string ReadSql = @"
SELECT
    HocVienVersion,
    KhoaHocVersion,
    GiaoVienVersion,
    PhotoVersion,
    LastSuccessfulSyncUtc
FROM dbo.App_DataVersion
WHERE VersionId = 1;";
}

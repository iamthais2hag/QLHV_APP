using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;

namespace QLHV.Infrastructure.SystemData;

public sealed class QlhvPartitionSyncStateRepository : IQlhvPartitionSyncStateRepository
{
    private readonly IConnectionSettingsProvider _connections;

    public QlhvPartitionSyncStateRepository(IConnectionSettingsProvider connections)
    {
        _connections = connections;
    }

    public async Task<QlhvPartitionSyncState?> GetAsync(
        string sourceType,
        CancellationToken cancellationToken = default)
    {
        var source = QlhvOperationSourceCatalog.GetRequired(sourceType);
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new QlhvAutoSyncStoreUnavailableException(
                "QLHV_APP chua co connection dung duoc cho partition sync state.");
        }

        try
        {
            await using var connection = new SqlConnection(target.ConnectionString);
            return await connection.QuerySingleOrDefaultAsync<QlhvPartitionSyncState>(
                new CommandDefinition(
                    ReadSql,
                    new { source.SourceType },
                    cancellationToken: cancellationToken));
        }
        catch (SqlException ex) when (ex.Number is 207 or 208)
        {
            throw new QlhvAutoSyncStoreUnavailableException(
                "Partition sync state chua san sang; can chay patch Auto Sync.",
                ex);
        }
        catch (SqlException ex)
        {
            throw new QlhvAutoSyncStoreUnavailableException(
                "Tam thoi khong doc duoc partition sync state tu QLHV_APP.",
                ex);
        }
    }

    private const string ReadSql = @"
SELECT
    SourceType,
    SourceProfileCode,
    AppliedBackupSnapshotToken,
    CONVERT(int, HocVienRows) AS HocVienRows,
    CONVERT(int, KhoaHocRows) AS KhoaHocRows,
    CONVERT(int, GiaoVienRows) AS GiaoVienRows,
    CONVERT(int, KhoaHocGiaoVienRows) AS KhoaHocGiaoVienRows,
    AppliedAtUtc
FROM dbo.App_QlhvSyncPartitionState
WHERE SourceType = @SourceType;";
}

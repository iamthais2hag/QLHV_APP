using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Ghi/đối chiếu học viên tại đích QLHV_APP.dbo.App_HocVien.
///
/// CHỈ ĐỌC: CountAsync, GetExistingKeysAsync.
/// GHI: UpsertBatchAsync = SqlBulkCopy vào bảng tạm #Sync_HocVien_Staging + MERGE keyed on MaDK
///      trong một transaction, rollback khi lỗi, không xóa vật lý.
///      Gác công tắc EnableTargetWrites (defense-in-depth) — ném lỗi nếu chưa bật.
/// Kết nối lấy qua IConnectionSettingsProvider; không lộ chuỗi kết nối.
/// </summary>
public sealed class QlhvHocVienTargetRepository : IQlhvHocVienTargetRepository
{
    private readonly IConnectionSettingsProvider _connections;
    private readonly SyncOptions _options;
    private readonly SyncExecutionOptions _execution;

    public QlhvHocVienTargetRepository(
        IConnectionSettingsProvider connections,
        IOptions<SyncOptions> options,
        IOptions<SyncExecutionOptions> execution)
    {
        _connections = connections;
        _options = options.Value;
        _execution = execution.Value;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var command = new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.App_HocVien WHERE IsDeleted = 0;",
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<int>(command);
    }

    public async Task<IReadOnlyCollection<string>> GetExistingKeysAsync(
        IReadOnlyCollection<string> maDks,
        CancellationToken cancellationToken = default)
    {
        if (maDks is null || maDks.Count == 0)
        {
            return Array.Empty<string>();
        }

        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var command = new CommandDefinition(
            "SELECT MaDK FROM dbo.App_HocVien WHERE MaDK IN @MaDks;",
            new { MaDks = maDks },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken);

        var keys = await connection.QueryAsync<string>(command);
        return keys.ToList();
    }

    public async Task<UpsertCounts> UpsertBatchAsync(
        IReadOnlyList<HocVienTargetWriteModel> rows,
        CancellationToken cancellationToken = default)
    {
        // Defense-in-depth: chặn ghi nếu công tắc chưa bật, dù người gọi có sai sót.
        if (!_execution.EnableTargetWrites)
        {
            throw new InvalidOperationException(
                "Ghi vao QLHV_APP bi chan: SyncExecution.EnableTargetWrites = false.");
        }

        if (rows.Count == 0)
        {
            return UpsertCounts.Empty;
        }

        var connectionString = await ResolveUsableTargetAsync(cancellationToken);

        return await SyncRetryPolicyFactory.CreateDefault(_options.MaxRetryAttempts).ExecuteAsync(
            ct => UpsertBatchCoreAsync(connectionString, rows, ct),
            cancellationToken);
    }

    private async Task<UpsertCounts> UpsertBatchCoreAsync(
        string connectionString,
        IReadOnlyList<HocVienTargetWriteModel> rows,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // 1. Tạo bảng staging tạm trên đúng session/transaction.
            await connection.ExecuteAsync(new CommandDefinition(
                HocVienTargetMergeSql.CreateStagingTable,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));

            // 2. SqlBulkCopy nạp dữ liệu vào bảng staging.
            using (var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
            {
                DestinationTableName = "#Sync_HocVien_Staging",
                BulkCopyTimeout = _options.TimeoutSeconds,
            })
            {
                using var table = BuildStagingTable(rows);
                foreach (DataColumn column in table.Columns)
                {
                    bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                }

                await bulkCopy.WriteToServerAsync(table, cancellationToken);
            }

            // 3. MERGE staging -> App_HocVien; thu OUTPUT $action để đếm.
            var actions = await connection.QueryAsync<string>(new CommandDefinition(
                HocVienTargetMergeSql.MergeStatement,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));

            var actionList = actions.ToList();
            var inserted = actionList.Count(a => string.Equals(a, "INSERT", StringComparison.OrdinalIgnoreCase));
            var updated = actionList.Count(a => string.Equals(a, "UPDATE", StringComparison.OrdinalIgnoreCase));
            var skipped = rows.Count - inserted - updated;

            // 4. Dọn bảng staging và commit.
            await connection.ExecuteAsync(new CommandDefinition(
                HocVienTargetMergeSql.DropStagingTable,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new UpsertCounts(inserted, updated, skipped < 0 ? 0 : skipped);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>Dựng DataTable staging khớp cấu trúc #Sync_HocVien_Staging, kèm V2RowHash.</summary>
    private static DataTable BuildStagingTable(IReadOnlyList<HocVienTargetWriteModel> rows)
    {
        var table = new DataTable();
        table.Columns.Add("MaDK", typeof(string));
        table.Columns.Add("MaKhoa", typeof(string));
        table.Columns.Add("TenKhoa", typeof(string));
        table.Columns.Add("HangGPLXHoc", typeof(string));
        table.Columns.Add("HoTen", typeof(string));
        table.Columns.Add("NgaySinh", typeof(DateTime));
        table.Columns.Add("GioiTinh", typeof(string));
        table.Columns.Add("SoCCCD", typeof(string));
        table.Columns.Add("DiaChiThuongTru", typeof(string));
        table.Columns.Add("SoGPLXDaCo", typeof(string));
        table.Columns.Add("HangGPLXDaCo", typeof(string));
        table.Columns.Add("NguoiNhanHoSo", typeof(string));
        table.Columns.Add("V2RowHash", typeof(string));

        foreach (var row in rows)
        {
            table.Rows.Add(
                row.MaDK,
                (object?)row.MaKhoa ?? DBNull.Value,
                (object?)row.TenKhoa ?? DBNull.Value,
                (object?)row.HangGPLXHoc ?? DBNull.Value,
                (object?)row.HoTen ?? DBNull.Value,
                (object?)row.NgaySinh ?? DBNull.Value,
                (object?)row.GioiTinh ?? DBNull.Value,
                (object?)row.SoCCCD ?? DBNull.Value,
                (object?)row.DiaChiThuongTru ?? DBNull.Value,
                (object?)row.SoGPLXDaCo ?? DBNull.Value,
                (object?)row.HangGPLXDaCo ?? DBNull.Value,
                (object?)row.NguoiNhanHoSo ?? DBNull.Value,
                V2RowHashCalculator.Compute(row));
        }

        return table;
    }

    private async Task<string> ResolveUsableTargetAsync(CancellationToken cancellationToken)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new InvalidOperationException(
                "QLHV_APP chua co cau hinh ket noi dung duoc (thieu hoac dang la placeholder).");
        }

        return target.ConnectionString;
    }
}

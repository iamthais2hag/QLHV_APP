using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Read and guarded write access to QLHV_APP.dbo.App_HocVien.
/// B3B implements the write path, but callers must pass execution guards first.
/// </summary>
public sealed class QlhvHocVienTargetRepository : IQlhvHocVienTargetRepository
{
    private readonly IConnectionSettingsProvider _connections;
    private readonly SyncOptions _options;

    public QlhvHocVienTargetRepository(
        IConnectionSettingsProvider connections,
        IOptions<SyncOptions> options)
    {
        _connections = connections;
        _options = options.Value;
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

    public async Task<HocVienUpsertResultDto> UpsertBatchAsync(
        IReadOnlyList<V2HocVienSourceRow> rows,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        if (dryRun)
        {
            throw new InvalidOperationException("UpsertBatchAsync khong duoc goi cho dry-run.");
        }

        if (!_options.EnableTargetWrites)
        {
            throw new InvalidOperationException("EnableTargetWrites=false. Target write path is locked.");
        }

        if (rows.Count == 0)
        {
            return new HocVienUpsertResultDto();
        }

        var mappedRows = MapRows(rows, out var skippedBeforeMerge);
        if (mappedRows.Count == 0)
        {
            return new HocVienUpsertResultDto
            {
                TotalRead = rows.Count,
                Skipped = rows.Count,
            };
        }

        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                HocVienTargetMergeSql.CreateStagingTable,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));

            using (var bulkCopy = new SqlBulkCopy(
                connection,
                SqlBulkCopyOptions.CheckConstraints,
                (SqlTransaction)transaction))
            {
                bulkCopy.DestinationTableName = HocVienTargetMergeSql.StagingTableName;
                bulkCopy.BatchSize = Math.Max(1, _options.BatchSize);
                bulkCopy.BulkCopyTimeout = _options.TimeoutSeconds;
                AddColumnMappings(bulkCopy);

                await bulkCopy.WriteToServerAsync(ToDataTable(mappedRows), cancellationToken);
            }

            var actions = await connection.QueryAsync<string>(new CommandDefinition(
                HocVienTargetMergeSql.MergeStatement,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                HocVienTargetMergeSql.DropStagingTable,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            var actionList = actions.ToList();
            var inserted = actionList.Count(a => string.Equals(a, "INSERT", StringComparison.OrdinalIgnoreCase));
            var updated = actionList.Count(a => string.Equals(a, "UPDATE", StringComparison.OrdinalIgnoreCase));
            var unchanged = mappedRows.Count - inserted - updated;

            return new HocVienUpsertResultDto
            {
                TotalRead = rows.Count,
                Inserted = inserted,
                Updated = updated,
                Skipped = skippedBeforeMerge + Math.Max(0, unchanged),
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static IReadOnlyList<HocVienTargetWriteModel> MapRows(
        IReadOnlyList<V2HocVienSourceRow> rows,
        out int skipped)
    {
        skipped = 0;
        var mapped = new List<HocVienTargetWriteModel>();

        foreach (var row in rows)
        {
            var result = HocVienSyncMapper.MapAndValidate(row);
            if (result.ShouldSkip || result.Model is null)
            {
                skipped++;
                continue;
            }

            mapped.Add(result.Model);
        }

        var deduped = mapped
            .GroupBy(row => row.MaDK, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToList();

        skipped += mapped.Count - deduped.Count;
        return deduped;
    }

    private static DataTable ToDataTable(IReadOnlyList<HocVienTargetWriteModel> rows)
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
        table.Columns.Add("SourceOfTruth", typeof(string));
        table.Columns.Add("V2RowHash", typeof(string));

        foreach (var row in rows)
        {
            table.Rows.Add(
                row.MaDK,
                Db(row.MaKhoa),
                Db(row.TenKhoa),
                Db(row.HangGPLXHoc),
                Db(row.HoTen),
                Db(row.NgaySinh),
                Db(row.GioiTinh),
                Db(row.SoCCCD),
                Db(row.DiaChiThuongTru),
                Db(row.SoGPLXDaCo),
                Db(row.HangGPLXDaCo),
                Db(row.NguoiNhanHoSo),
                row.SourceOfTruth,
                row.V2RowHash);
        }

        return table;
    }

    private static void AddColumnMappings(SqlBulkCopy bulkCopy)
    {
        foreach (var column in new[]
        {
            "MaDK",
            "MaKhoa",
            "TenKhoa",
            "HangGPLXHoc",
            "HoTen",
            "NgaySinh",
            "GioiTinh",
            "SoCCCD",
            "DiaChiThuongTru",
            "SoGPLXDaCo",
            "HangGPLXDaCo",
            "NguoiNhanHoSo",
            "SourceOfTruth",
            "V2RowHash",
        })
        {
            bulkCopy.ColumnMappings.Add(column, column);
        }
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

    private static object Db(string? value) => value is null ? DBNull.Value : value;
    private static object Db(DateTime? value) => value.HasValue ? value.Value : DBNull.Value;
}

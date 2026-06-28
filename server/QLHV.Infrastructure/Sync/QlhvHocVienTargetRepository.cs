using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Read and guarded write access to QLHV_APP.dbo.App_HocVien.
/// Writes use SqlBulkCopy into a temp staging table and MERGE in a transaction; no physical delete.
/// </summary>
public sealed class QlhvHocVienTargetRepository : IQlhvHocVienTargetRepository
{
    private readonly IConnectionSettingsProvider _connections;
    private readonly AppSyncOptions _options;
    private readonly SyncExecutionOptions _execution;

    public QlhvHocVienTargetRepository(
        IConnectionSettingsProvider connections,
        IOptions<AppSyncOptions> options,
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

    public async Task<IReadOnlyCollection<string>> GetExistingSourceKeysAsync(
        string sourceProfileCode,
        IReadOnlyCollection<string> sourceMaDks,
        CancellationToken cancellationToken = default)
    {
        if (sourceMaDks is null || sourceMaDks.Count == 0)
        {
            return Array.Empty<string>();
        }

        var normalizedProfile = NormalizeRequired(sourceProfileCode, nameof(sourceProfileCode)).ToUpperInvariant();
        var normalizedSourceMaDks = sourceMaDks
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedSourceMaDks.Length == 0)
        {
            return Array.Empty<string>();
        }

        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var command = new CommandDefinition(
            @"
SELECT SourceProfileCode, SourceMaDK
FROM dbo.App_HocVien
WHERE SourceProfileCode = @SourceProfileCode
  AND SourceMaDK IN @SourceMaDks;",
            new
            {
                SourceProfileCode = normalizedProfile,
                SourceMaDks = normalizedSourceMaDks,
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken);

        var keys = await connection.QueryAsync<ExistingSourceKeyRow>(command);
        return keys
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.SourceProfileCode) &&
                !string.IsNullOrWhiteSpace(row.SourceMaDK))
            .Select(row => HocVienSourceIdentityKey.Create(row.SourceProfileCode, row.SourceMaDK))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, string>> GetExistingSourceHashesAsync(
        string sourceProfileCode,
        IReadOnlyCollection<string> sourceMaDks,
        CancellationToken cancellationToken = default)
    {
        if (sourceMaDks is null || sourceMaDks.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var normalizedProfile = NormalizeRequired(sourceProfileCode, nameof(sourceProfileCode)).ToUpperInvariant();
        var normalizedSourceMaDks = sourceMaDks
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedSourceMaDks.Length == 0)
        {
            return new Dictionary<string, string>();
        }

        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var command = new CommandDefinition(
            @"
SELECT SourceProfileCode, SourceMaDK, V2RowHash
FROM dbo.App_HocVien
WHERE SourceProfileCode = @SourceProfileCode
  AND SourceMaDK IN @SourceMaDks;",
            new
            {
                SourceProfileCode = normalizedProfile,
                SourceMaDks = normalizedSourceMaDks,
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<ExistingSourceHashRow>(command);
        return rows
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.SourceProfileCode) &&
                !string.IsNullOrWhiteSpace(row.SourceMaDK))
            .GroupBy(
                row => HocVienSourceIdentityKey.Create(row.SourceProfileCode, row.SourceMaDK),
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().V2RowHash ?? string.Empty,
                StringComparer.Ordinal);
    }

    public async Task<QlhvHocVienTargetDiagnosticsDto> GetDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);

        var schema = await connection.QuerySingleAsync<TargetSchemaDiagnosticsRow>(new CommandDefinition(
            TargetSchemaDiagnosticsSql,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        var columns = (await connection.QueryAsync<RequiredColumnCheckDto>(new CommandDefinition(
            RequiredColumnsSql,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken))).ToList();

        int? targetRows = null;
        SoCmtLengthDiagnosticsDto? soCccdLength = null;
        if (schema.AppHocVienExists)
        {
            var activeFilter = schema.IsDeletedColumnExists ? " WHERE IsDeleted = 0" : string.Empty;
            targetRows = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(1) FROM dbo.App_HocVien" + activeFilter + ";",
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));

            if (columns.Any(c => string.Equals(c.ColumnName, "SoCCCD", StringComparison.OrdinalIgnoreCase) && c.Exists))
            {
                var soCccdRows = (await connection.QueryAsync<SourceValueDistributionDto>(new CommandDefinition(
                    BuildTargetSoCccdLengthSql(activeFilter),
                    commandTimeout: _options.TimeoutSeconds,
                    cancellationToken: cancellationToken))).ToList();

                soCccdLength = new SoCmtLengthDiagnosticsDto
                {
                    NineDigits = GetBucket(soCccdRows, "9"),
                    TwelveDigits = GetBucket(soCccdRows, "12"),
                    Other = GetBucket(soCccdRows, "other"),
                    NullOrEmpty = GetBucket(soCccdRows, "null-empty"),
                };
            }
        }

        return new QlhvHocVienTargetDiagnosticsDto
        {
            CheckedAtUtc = DateTime.UtcNow,
            AppHocVienExists = schema.AppHocVienExists,
            AppDongBoLogExists = schema.AppDongBoLogExists,
            RequiredColumns = columns,
            TargetRows = targetRows,
            TargetRowsUseIsDeletedFilter = schema.IsDeletedColumnExists,
            SoCccdLength = soCccdLength,
        };
    }

    public async Task<UpsertCounts> UpsertBatchAsync(
        IReadOnlyList<HocVienTargetWriteModel> rows,
        CancellationToken cancellationToken = default)
    {
        if (_options.DryRun)
        {
            throw new InvalidOperationException(
                "Ghi vao QLHV_APP bi chan: Sync:DryRun = true.");
        }

        if (!_execution.EnableTargetWrites)
        {
            throw new InvalidOperationException(
                "Ghi vao QLHV_APP bi chan: SyncExecution.EnableTargetWrites = false.");
        }

        if (rows.Count == 0)
        {
            return UpsertCounts.Empty;
        }

        ValidateSourceIdentity(rows);

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
            await connection.ExecuteAsync(new CommandDefinition(
                HocVienTargetMergeSql.CreateStagingTable,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));

            using (var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction)
            {
                DestinationTableName = HocVienTargetMergeSql.StagingTableName,
                BatchSize = Math.Max(1, _options.BatchSize),
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
            var skipped = Math.Max(0, rows.Count - inserted - updated);
            return new UpsertCounts(inserted, updated, skipped);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static DataTable BuildStagingTable(IReadOnlyList<HocVienTargetWriteModel> rows)
    {
        var table = new DataTable();
        table.Columns.Add("SourceProfileCode", typeof(string));
        table.Columns.Add("SourceMaDK", typeof(string));
        table.Columns.Add("SourceSystem", typeof(string));
        table.Columns.Add("SourceVersion", typeof(string));
        table.Columns.Add("MaDK", typeof(string));
        table.Columns.Add("MaKhoa", typeof(string));
        table.Columns.Add("TenKhoa", typeof(string));
        table.Columns.Add("MaHangDT", typeof(string));
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
            var sourceProfileCode = NormalizeRequired(row.SourceProfileCode, nameof(row.SourceProfileCode))
                .ToUpperInvariant();
            var sourceMaDK = NormalizeRequired(row.SourceMaDK, nameof(row.SourceMaDK));
            var sourceSystem = NormalizeRequired(row.SourceSystem, nameof(row.SourceSystem))
                .ToUpperInvariant();
            var hash = string.IsNullOrWhiteSpace(row.V2RowHash)
                ? V2RowHashCalculator.Compute(row)
                : row.V2RowHash;

            table.Rows.Add(
                sourceProfileCode,
                sourceMaDK,
                sourceSystem,
                Db(row.SourceVersion),
                row.MaDK,
                Db(row.MaKhoa),
                Db(row.TenKhoa),
                Db(row.MaHangDT),
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
                hash);
        }

        return table;
    }

    private static void ValidateSourceIdentity(IReadOnlyList<HocVienTargetWriteModel> rows)
    {
        foreach (var row in rows)
        {
            _ = NormalizeRequired(row.SourceProfileCode, nameof(row.SourceProfileCode));
            _ = NormalizeRequired(row.SourceMaDK, nameof(row.SourceMaDK));
            _ = NormalizeRequired(row.SourceSystem, nameof(row.SourceSystem));
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
    private static string NormalizeRequired(string? value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Thieu thong tin dinh danh nguon bat buoc: {name}.")
            : value.Trim();

    private static int GetBucket(IEnumerable<SourceValueDistributionDto> rows, string value)
        => rows.FirstOrDefault(r => string.Equals(r.Value, value, StringComparison.OrdinalIgnoreCase))?.Total ?? 0;

    private static string BuildTargetSoCccdLengthSql(string activeFilter) => @"
SELECT
    CASE
        WHEN NULLIF(LTRIM(RTRIM(SoCCCD)), '') IS NULL THEN 'null-empty'
        WHEN LEN(LTRIM(RTRIM(SoCCCD))) = 9 THEN '9'
        WHEN LEN(LTRIM(RTRIM(SoCCCD))) = 12 THEN '12'
        ELSE 'other'
    END AS Value,
    COUNT(1) AS Total
FROM dbo.App_HocVien" + activeFilter + @"
GROUP BY CASE
    WHEN NULLIF(LTRIM(RTRIM(SoCCCD)), '') IS NULL THEN 'null-empty'
    WHEN LEN(LTRIM(RTRIM(SoCCCD))) = 9 THEN '9'
    WHEN LEN(LTRIM(RTRIM(SoCCCD))) = 12 THEN '12'
    ELSE 'other'
END
ORDER BY Value;";

    private const string TargetSchemaDiagnosticsSql = @"
SELECT
    CAST(CASE WHEN OBJECT_ID(N'dbo.App_HocVien', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS AppHocVienExists,
    CAST(CASE WHEN OBJECT_ID(N'dbo.App_DongBoLog', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS AppDongBoLogExists,
    CAST(CASE WHEN COL_LENGTH(N'dbo.App_HocVien', N'IsDeleted') IS NULL THEN 0 ELSE 1 END AS bit) AS IsDeletedColumnExists;";

    private const string RequiredColumnsSql = @"
SELECT
    requiredColumns.ColumnName,
    CAST(CASE WHEN sysColumns.column_id IS NULL THEN 0 ELSE 1 END AS bit) AS [Exists]
FROM (
    VALUES
        (1, N'MaDK'),
        (2, N'HoTen'),
        (3, N'NgaySinh'),
        (4, N'GioiTinh'),
        (5, N'SoCCCD'),
        (6, N'DiaChiThuongTru'),
        (7, N'MaKhoa'),
        (8, N'TenKhoa'),
        (9, N'MaHangDT'),
        (10, N'HangGPLXHoc'),
        (11, N'SourceProfileCode'),
        (12, N'SourceMaDK'),
        (13, N'SourceSystem'),
        (14, N'SourceVersion'),
        (15, N'V2RowHash')
) AS requiredColumns(SortOrder, ColumnName)
LEFT JOIN sys.objects AS sysObjects
    ON sysObjects.object_id = OBJECT_ID(N'dbo.App_HocVien', N'U')
LEFT JOIN sys.columns AS sysColumns
    ON sysColumns.object_id = sysObjects.object_id
   AND sysColumns.name = requiredColumns.ColumnName
ORDER BY requiredColumns.SortOrder;";

    private sealed class TargetSchemaDiagnosticsRow
    {
        public bool AppHocVienExists { get; init; }
        public bool AppDongBoLogExists { get; init; }
        public bool IsDeletedColumnExists { get; init; }
    }

    private sealed class ExistingSourceKeyRow
    {
        public string SourceProfileCode { get; init; } = string.Empty;
        public string SourceMaDK { get; init; } = string.Empty;
    }

    private sealed class ExistingSourceHashRow
    {
        public string SourceProfileCode { get; init; } = string.Empty;
        public string SourceMaDK { get; init; } = string.Empty;
        public string? V2RowHash { get; init; }
    }
}

using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.CsdtConnections;
using QLHV.Application.CsdtConnections.Dtos;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Infrastructure.Sync;

public sealed class QlhvImportReadRepository : IQlhvImportReadRepository
{
    private const string AuthModeSqlLogin = "SqlLogin";

    private readonly ICsdtConnectionProfileRepository _profileRepository;
    private readonly IConnectionPasswordProtector _passwordProtector;
    private readonly IConnectionSettingsProvider _connections;
    private readonly SyncOptions _options;

    public QlhvImportReadRepository(
        ICsdtConnectionProfileRepository profileRepository,
        IConnectionPasswordProtector passwordProtector,
        IConnectionSettingsProvider connections,
        IOptions<SyncOptions> options)
    {
        _profileRepository = profileRepository;
        _passwordProtector = passwordProtector;
        _connections = connections;
        _options = options.Value;
    }

    public async Task<QlhvImportSourceSnapshot> ReadSourceAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveSourceProfileAsync(request.SourceProfileCode, cancellationToken);
        return await SyncRetryPolicyFactory.CreateDefault(_options.MaxRetryAttempts).ExecuteAsync(async ct =>
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            var schema = await connection.QuerySingleAsync<SourceSchemaRow>(new CommandDefinition(
                SourceSchemaSql,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct));
            var missingTables = schema.MissingRequiredTables();
            if (missingTables.Count > 0)
            {
                throw new QlhvImportReadException(
                    "Source thieu bang bat buoc: " + string.Join(", ", missingTables) + ".");
            }

            var query = QlhvImportSqlBuilder.BuildSourceRead(request, schema.KhoaHocHasMaCsdt);
            using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
                query.Sql,
                query.Parameters,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct));
            var hocVienRows = (await grid.ReadAsync<V2HocVienSourceRow>()).ToList();
            var khoaHocRows = await grid.ReadSingleAsync<int>();

            return new QlhvImportSourceSnapshot
            {
                HocVienRows = hocVienRows,
                KhoaHocRows = khoaHocRows,
            };
        }, cancellationToken);
    }

    public async Task<QlhvImportTargetSnapshot> ReadTargetAsync(
        QlhvImportRequest request,
        IReadOnlyCollection<string> sourceMaDks,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveTargetAsync(cancellationToken);
        return await SyncRetryPolicyFactory.CreateDefault(_options.MaxRetryAttempts).ExecuteAsync(async ct =>
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            var schema = await connection.QuerySingleAsync<TargetImportSchemaRow>(new CommandDefinition(
                TargetImportSchemaSql,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct));
            var writeColumns = await connection.QueryAsync<RequiredColumnCheckDto>(new CommandDefinition(
                TargetHocVienWriteColumnsSql,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct));
            var missingWriteColumns = writeColumns
                .Where(column => !column.Exists)
                .Select(column => column.ColumnName)
                .ToArray();
            if (missingWriteColumns.Length > 0)
            {
                throw new QlhvImportReadException(
                    "Target dbo.App_HocVien thieu cot can cho import: " +
                    string.Join(", ", missingWriteColumns) + ".");
            }

            var countQuery = QlhvImportSqlBuilder.BuildTargetKhoaHocCount(
                request,
                schema.AppKhoaHocExists,
                schema.AppKhoaHocHasMaKhoa,
                schema.AppKhoaHocHasIsDeleted);
            var appKhoaHocRows = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                countQuery.Sql,
                countQuery.Parameters,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct));
            var currentAppHocVienRows = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                CurrentAppHocVienRowsSql,
                new
                {
                    request.SourceProfileCode,
                    request.MaKhoaHoc,
                },
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct));

            var existingRows = await ReadExistingRowsAsync(
                connection,
                request.SourceProfileCode,
                sourceMaDks,
                ct);
            var targetRowsForSourceProfile = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                TargetRowsForSourceProfileSql,
                new { request.SourceProfileCode },
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct));
            var collisionCount = sourceMaDks.Count == 0
                ? 0
                : await CountCrossProfileMaDkCollisionsAsync(
                    connection,
                    request.SourceProfileCode,
                    sourceMaDks,
                    ct);
            var constraintRows = await connection.QueryAsync<CheckConstraintDefinitionRow>(new CommandDefinition(
                SourceProfileConstraintsSql,
                new
                {
                    QualifiedTableName = "dbo.App_HocVien",
                    ColumnName = "SourceProfileCode",
                },
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct));
            var constraintEvaluation = QlhvImportConstraintEvaluator.Evaluate(
                constraintRows.Select(row => row.Definition),
                request.SourceProfileCode);

            return new QlhvImportTargetSnapshot
            {
                CurrentAppHocVienRows = currentAppHocVienRows,
                AppKhoaHocRows = appKhoaHocRows,
                ExistingHocVienHashes = existingRows.Hashes,
                TargetRowsForSourceProfile = targetRowsForSourceProfile,
                TargetExactIdentityMatches = existingRows.ActiveCount + existingRows.SoftDeletedCount,
                TargetMaDkConflictsOtherProfiles = collisionCount,
                SoftDeletedIdentityConflicts = existingRows.SoftDeletedCount,
                SourceProfileConstraintExists = constraintEvaluation.Exists,
                SourceProfileAllowedByConstraint = constraintEvaluation.AllowsSourceProfile,
            };
        }, cancellationToken);
    }

    private async Task<string> ResolveSourceProfileAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken)
    {
        var profile = await _profileRepository.GetByCodeAsync(sourceProfileCode, cancellationToken);
        if (profile is null)
        {
            throw new QlhvImportReadException($"Profile {sourceProfileCode} khong ton tai.");
        }

        if (!profile.IsActive)
        {
            throw new QlhvImportReadException($"Profile {sourceProfileCode} dang tat.");
        }

        return BuildProfileConnectionString(profile);
    }

    private string BuildProfileConnectionString(CsdtConnectionProfileRecord profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ServerName) || string.IsNullOrWhiteSpace(profile.DatabaseName))
        {
            throw new QlhvImportReadException(
                $"Profile {profile.ProfileCode} chua co ServerName hoac DatabaseName.");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.ServerName,
            InitialCatalog = profile.DatabaseName,
            ConnectTimeout = Math.Clamp(_options.TimeoutSeconds, 5, 30),
            TrustServerCertificate = true,
            MultipleActiveResultSets = false,
        };

        if (string.Equals(profile.AuthMode, AuthModeSqlLogin, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(profile.UserName) ||
                profile.PasswordCipherText is null ||
                !profile.IsPasswordConfigured)
            {
                throw new QlhvImportReadException(
                    $"Profile {profile.ProfileCode} SQL Login chua du UserName/password da ma hoa.");
            }

            if (!_passwordProtector.IsAvailable)
            {
                throw new QlhvImportReadException(
                    $"Profile {profile.ProfileCode} can giai ma password nhung password protector chua san sang.");
            }

            builder.IntegratedSecurity = false;
            builder.UserID = profile.UserName;
            builder.Password = _passwordProtector.Unprotect(profile.PasswordCipherText);
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    private async Task<string> ResolveTargetAsync(CancellationToken cancellationToken)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new QlhvImportReadException(
                "QLHV_APP chua co cau hinh ket noi dung duoc (thieu hoac dang la placeholder).");
        }

        return target.ConnectionString;
    }

    private async Task<ExistingRowsSnapshot> ReadExistingRowsAsync(
        SqlConnection connection,
        string sourceProfileCode,
        IReadOnlyCollection<string> sourceMaDks,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var activeCount = 0;
        var softDeletedCount = 0;
        foreach (var batch in NormalizeKeys(sourceMaDks).Chunk(1000))
        {
            var rows = await connection.QueryAsync<ExistingHashRow>(new CommandDefinition(
                ExistingHashesSql,
                new { SourceProfileCode = sourceProfileCode, SourceMaDks = batch },
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
            foreach (var row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.SourceMaDK))
                {
                    result[HocVienSourceIdentityKey.Create(sourceProfileCode, row.SourceMaDK)] =
                        row.V2RowHash ?? string.Empty;
                    if (row.IsDeleted)
                    {
                        softDeletedCount++;
                    }
                    else
                    {
                        activeCount++;
                    }
                }
            }
        }

        return new ExistingRowsSnapshot(result, activeCount, softDeletedCount);
    }

    private async Task<int> CountCrossProfileMaDkCollisionsAsync(
        SqlConnection connection,
        string sourceProfileCode,
        IReadOnlyCollection<string> sourceMaDks,
        CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var batch in NormalizeKeys(sourceMaDks).Chunk(1000))
        {
            total += await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                CrossProfileCollisionCountSql,
                new { SourceProfileCode = sourceProfileCode, SourceMaDks = batch },
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
        }

        return total;
    }

    private static string[] NormalizeKeys(IReadOnlyCollection<string> values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private const string SourceSchemaSql = @"
SELECT
    CAST(CASE WHEN OBJECT_ID(N'dbo.NguoiLX', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS NguoiLxExists,
    CAST(CASE WHEN OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS NguoiLxHoSoExists,
    CAST(CASE WHEN OBJECT_ID(N'dbo.KhoaHoc', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS KhoaHocExists,
    CAST(CASE WHEN OBJECT_ID(N'dbo.DM_HangDT', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS DmHangDtExists,
    CAST(CASE WHEN OBJECT_ID(N'dbo.DM_DVHC', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS DmDvhcExists,
    CAST(CASE WHEN COL_LENGTH(N'dbo.KhoaHoc', N'MaCSDT') IS NULL THEN 0 ELSE 1 END AS bit) AS KhoaHocHasMaCsdt;";

    private const string TargetImportSchemaSql = @"
SELECT
    CAST(CASE WHEN OBJECT_ID(N'dbo.App_KhoaHoc', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS AppKhoaHocExists,
    CAST(CASE WHEN COL_LENGTH(N'dbo.App_KhoaHoc', N'MaKhoa') IS NULL THEN 0 ELSE 1 END AS bit) AS AppKhoaHocHasMaKhoa,
    CAST(CASE WHEN COL_LENGTH(N'dbo.App_KhoaHoc', N'IsDeleted') IS NULL THEN 0 ELSE 1 END AS bit) AS AppKhoaHocHasIsDeleted;";

    private const string TargetHocVienWriteColumnsSql = @"
SELECT
    requiredColumns.ColumnName,
    CAST(CASE WHEN targetColumn.column_id IS NULL THEN 0 ELSE 1 END AS bit) AS [Exists]
FROM (
    VALUES
        (1, N'SourceProfileCode'),
        (2, N'SourceMaDK'),
        (3, N'SourceSystem'),
        (4, N'SourceVersion'),
        (5, N'MaDK'),
        (6, N'MaKhoa'),
        (7, N'TenKhoa'),
        (8, N'MaHangDT'),
        (9, N'HangGPLXHoc'),
        (10, N'HoTen'),
        (11, N'NgaySinh'),
        (12, N'GioiTinh'),
        (13, N'SoCCCD'),
        (14, N'DiaChiThuongTru'),
        (15, N'SoGPLXDaCo'),
        (16, N'HangGPLXDaCo'),
        (17, N'NguoiNhanHoSo'),
        (18, N'SourceOfTruth'),
        (19, N'V2RowHash'),
        (20, N'LastSyncFromV2At'),
        (21, N'LastSyncStatus'),
        (22, N'LastSyncMessage'),
        (23, N'IsDeleted'),
        (24, N'UpdatedAt'),
        (25, N'UpdatedBy'),
        (26, N'CreatedBy')
) AS requiredColumns(SortOrder, ColumnName)
LEFT JOIN sys.columns AS targetColumn
    ON targetColumn.object_id = OBJECT_ID(N'dbo.App_HocVien', N'U')
   AND targetColumn.name = requiredColumns.ColumnName
ORDER BY requiredColumns.SortOrder;";

    private const string CurrentAppHocVienRowsSql = @"
SELECT COUNT(1)
FROM dbo.App_HocVien
WHERE SourceProfileCode = @SourceProfileCode
  AND IsDeleted = 0
  AND (@MaKhoaHoc IS NULL OR LTRIM(RTRIM(MaKhoa)) = @MaKhoaHoc);";

    private const string TargetRowsForSourceProfileSql = @"
SELECT COUNT(1)
FROM dbo.App_HocVien
WHERE SourceProfileCode = @SourceProfileCode;";

    private const string SourceProfileConstraintsSql = @"
DECLARE @TargetObjectId int = OBJECT_ID(@QualifiedTableName, N'U');
DECLARE @TargetColumnId int = (
    SELECT column_id
    FROM sys.columns
    WHERE object_id = @TargetObjectId
      AND name = @ColumnName
);

SELECT DISTINCT
    checkConstraint.name AS ConstraintName,
    checkConstraint.definition AS Definition
FROM sys.check_constraints AS checkConstraint
WHERE checkConstraint.parent_object_id = @TargetObjectId
  AND checkConstraint.is_disabled = 0
  AND (
      checkConstraint.parent_column_id = @TargetColumnId
      OR EXISTS (
          SELECT 1
          FROM sys.sql_expression_dependencies AS dependency
          WHERE dependency.referencing_id = checkConstraint.object_id
            AND dependency.referenced_id = @TargetObjectId
            AND dependency.referenced_minor_id = @TargetColumnId
      )
  );";

    private const string ExistingHashesSql = @"
SELECT SourceMaDK, V2RowHash, IsDeleted
FROM dbo.App_HocVien
WHERE SourceProfileCode = @SourceProfileCode
  AND SourceMaDK IN @SourceMaDks;";

    private const string CrossProfileCollisionCountSql = @"
SELECT COUNT(DISTINCT LTRIM(RTRIM(MaDK)))
FROM dbo.App_HocVien
WHERE MaDK IN @SourceMaDks
  AND (SourceProfileCode IS NULL OR SourceProfileCode <> @SourceProfileCode);";

    private sealed class SourceSchemaRow
    {
        public bool NguoiLxExists { get; init; }
        public bool NguoiLxHoSoExists { get; init; }
        public bool KhoaHocExists { get; init; }
        public bool DmHangDtExists { get; init; }
        public bool DmDvhcExists { get; init; }
        public bool KhoaHocHasMaCsdt { get; init; }

        public IReadOnlyList<string> MissingRequiredTables()
        {
            var missing = new List<string>();
            if (!NguoiLxExists) missing.Add("dbo.NguoiLX");
            if (!NguoiLxHoSoExists) missing.Add("dbo.NguoiLX_HoSo");
            if (!KhoaHocExists) missing.Add("dbo.KhoaHoc");
            if (!DmHangDtExists) missing.Add("dbo.DM_HangDT");
            if (!DmDvhcExists) missing.Add("dbo.DM_DVHC");
            return missing;
        }
    }

    private sealed class TargetImportSchemaRow
    {
        public bool AppKhoaHocExists { get; init; }
        public bool AppKhoaHocHasMaKhoa { get; init; }
        public bool AppKhoaHocHasIsDeleted { get; init; }
    }

    private sealed class ExistingHashRow
    {
        public string SourceMaDK { get; init; } = string.Empty;
        public string? V2RowHash { get; init; }
        public bool IsDeleted { get; init; }
    }

    private sealed class CheckConstraintDefinitionRow
    {
        public string ConstraintName { get; init; } = string.Empty;
        public string Definition { get; init; } = string.Empty;
    }

    private sealed record ExistingRowsSnapshot(
        IReadOnlyDictionary<string, string> Hashes,
        int ActiveCount,
        int SoftDeletedCount);
}

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

public sealed class QlhvImportReadRepository :
    IQlhvImportReadRepository,
    IQlhvFreshnessSourceRepository
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

    public Task<QlhvImportSourceSnapshot> ReadSourceAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken = default)
        => ReadBackupSourceAsync(request, cancellationToken);

    public Task<QlhvImportSourceSnapshot> ReadBackupSourceAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken = default)
        => ReadSourceCoreAsync(request, ResolveSourceBinding(request.SourceProfileCode), cancellationToken);

    public Task<QlhvImportSourceSnapshot> ReadLiveSourceAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken = default)
        => ReadSourceCoreAsync(request, ResolveLiveSourceBinding(request.SourceProfileCode), cancellationToken);

    private async Task<QlhvImportSourceSnapshot> ReadSourceCoreAsync(
        QlhvImportRequest request,
        SourceBinding binding,
        CancellationToken cancellationToken)
    {
        var connectionString = await ResolveSourceProfileAsync(binding.ConnectionProfileCode, cancellationToken);
        return await SyncRetryPolicyFactory.CreateDefault(_options.MaxRetryAttempts).ExecuteAsync(async ct =>
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            var sourceDatabaseName = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT DB_NAME();",
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct)) ?? string.Empty;
            if (!string.Equals(sourceDatabaseName, binding.ExpectedDatabaseName, StringComparison.Ordinal))
            {
                throw new QlhvImportReadException(
                    $"Profile {binding.ConnectionProfileCode} dang ket noi database " +
                    $"{sourceDatabaseName ?? "(null)"}; bat buoc phai la {binding.ExpectedDatabaseName}.");
            }

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

            var missingImageColumns = schema.MissingRequiredImageColumns();
            if (missingImageColumns.Count > 0)
            {
                throw new QlhvImportReadException(
                    "Khong the map hinh anh an toan; source thieu cot: " +
                    string.Join(", ", missingImageColumns) + ".");
            }

            var snapshotBefore = await ReadSnapshotMetadataAsync(connection, ct);
            var tokenBefore = ResolveSnapshotToken(sourceDatabaseName, snapshotBefore);
            var query = QlhvImportSqlBuilder.BuildSourceRead(request, schema.KhoaHocHasMaCsdt);
            using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
                query.Sql,
                query.Parameters,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct));
            var hocVienRows = (await grid.ReadAsync<V2HocVienSourceRow>()).ToList();
            var khoaHocRows = (await grid.ReadAsync<QlhvKhoaHocSourceRow>()).ToList();
            var giaoVienRows = (await grid.ReadAsync<QlhvGiaoVienSourceRow>()).ToList();
            var relationRows = (await grid.ReadAsync<QlhvKhoaHocGiaoVienSourceRow>()).ToList();
            grid.Dispose();
            var snapshotAfter = await ReadSnapshotMetadataAsync(connection, ct);
            var snapshotToken = ResolveSnapshotToken(sourceDatabaseName, snapshotAfter);
            if (!string.Equals(tokenBefore, snapshotToken, StringComparison.Ordinal))
            {
                throw new QlhvImportReadException(
                    "Snapshot BAK thay doi trong luc doc plan; hay lap plan lai sau khi refresh ket thuc.");
            }

            return new QlhvImportSourceSnapshot
            {
                SourceDatabaseName = sourceDatabaseName,
                BackupSnapshotToken = snapshotToken,
                GeneratedAtUtc = DateTime.UtcNow,
                HocVienRows = hocVienRows,
                KhoaHocRows = khoaHocRows.Count,
                KhoaHocSourceRows = khoaHocRows,
                GiaoVienRows = giaoVienRows,
                KhoaHocGiaoVienRows = relationRows,
            };
        }, cancellationToken);
    }

    private async Task<BackupSnapshotMetadataRow> ReadSnapshotMetadataAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
        => await connection.QuerySingleAsync<BackupSnapshotMetadataRow>(new CommandDefinition(
            BackupSnapshotMetadataSql,
            new { SnapshotPropertyName = QlhvBackupSnapshotToken.ExtendedPropertyName },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

    private static string ResolveSnapshotToken(
        string databaseName,
        BackupSnapshotMetadataRow metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.SnapshotToken))
        {
            return metadata.SnapshotToken.Trim();
        }

        return QlhvBackupSnapshotToken.CreateImportMetadataFallback(
            databaseName,
            DateTime.SpecifyKind(metadata.CreateDate, DateTimeKind.Utc),
            new QlhvOperationRowCountsDto
            {
                NguoiLX = metadata.NguoiLXRows,
                NguoiLXHoSo = metadata.NguoiLXHoSoRows,
                KhoaHoc = metadata.KhoaHocRows,
            },
            metadata.GiaoVienRows,
            metadata.KhoaHocGiaoVienRows);
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

            var entityWriteColumns = await connection.QueryAsync<RequiredTableColumnCheckRow>(new CommandDefinition(
                TargetCourseTeacherWriteColumnsSql,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct));
            var missingEntityWriteColumns = entityWriteColumns
                .Where(column => !column.Exists)
                .Select(column => $"{column.TableName}.{column.ColumnName}")
                .ToArray();
            if (missingEntityWriteColumns.Length > 0)
            {
                throw new QlhvImportReadException(
                    "Target chua ap dung patch full sync khoa hoc/giao vien: " +
                    string.Join(", ", missingEntityWriteColumns) + ".");
            }

            var currentAppHocVienRows = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                CurrentAppHocVienRowsSql,
                new
                {
                    request.SourceProfileCode,
                    request.MaKhoaHoc,
                },
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct));

            using var entityGrid = await connection.QueryMultipleAsync(new CommandDefinition(
                TargetEntityPartitionRowsSql,
                new { request.SourceProfileCode },
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct));
            var khoaHocTargetRows = MapEntityTargetRows(
                (await entityGrid.ReadAsync<EntityHashRow>()).ToList());
            var giaoVienTargetRows = MapEntityTargetRows(
                (await entityGrid.ReadAsync<EntityHashRow>()).ToList());
            var relationTargetRows = MapEntityTargetRows(
                (await entityGrid.ReadAsync<EntityHashRow>()).ToList());
            entityGrid.Dispose();
            var appKhoaHocRows = khoaHocTargetRows.Count(row => !row.IsDeleted);

            var targetPartitionRows = (await connection.QueryAsync<ExistingHashRow>(new CommandDefinition(
                TargetPartitionRowsSql,
                new { request.SourceProfileCode },
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct))).ToList();
            var normalizedSourceKeys = new HashSet<string>(
                NormalizeKeys(sourceMaDks),
                StringComparer.OrdinalIgnoreCase);
            var exactIdentityRows = targetPartitionRows
                .Where(row => normalizedSourceKeys.Contains(row.SourceMaDK?.Trim() ?? string.Empty))
                .ToList();
            var duplicateHocVienTargetIdentities = targetPartitionRows
                .Where(row => !string.IsNullOrWhiteSpace(row.SourceMaDK))
                .GroupBy(row => row.SourceMaDK.Trim(), StringComparer.OrdinalIgnoreCase)
                .Count(group => group.Skip(1).Any());
            var duplicateTargetIdentities = duplicateHocVienTargetIdentities +
                CountDuplicateEntityTargetIdentities(khoaHocTargetRows) +
                CountDuplicateEntityTargetIdentities(giaoVienTargetRows) +
                CountDuplicateEntityTargetIdentities(relationTargetRows);
            var targetPlannerRows = targetPartitionRows
                .Select(row => new QlhvFullSyncTargetRow(
                    row.SourceMaDK?.Trim() ?? string.Empty,
                    row.V2RowHash,
                    row.IsDeleted))
                .ToArray();
            var existingHashes = targetPartitionRows
                .Where(row => !string.IsNullOrWhiteSpace(row.SourceMaDK))
                .GroupBy(row => row.SourceMaDK.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => HocVienSourceIdentityKey.Create(request.SourceProfileCode, group.Key),
                    group => group.First().V2RowHash ?? string.Empty,
                    StringComparer.Ordinal);
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
                ExistingHocVienHashes = existingHashes,
                HocVienRows = targetPlannerRows,
                KhoaHocRows = khoaHocTargetRows,
                GiaoVienRows = giaoVienTargetRows,
                RelationRows = relationTargetRows,
                DuplicateTargetIdentityRows = duplicateTargetIdentities,
                TargetRowsForSourceProfile = targetRowsForSourceProfile,
                TargetExactIdentityMatches = exactIdentityRows.Count,
                TargetMaDkConflictsOtherProfiles = collisionCount,
                SoftDeletedIdentityConflicts = exactIdentityRows.Count(row => row.IsDeleted),
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

    private static SourceBinding ResolveSourceBinding(string sourceProfileCode)
        => sourceProfileCode switch
        {
            CsdtConnectionProfileCodes.CsdtOto => new SourceBinding(
                CsdtConnectionProfileCodes.CsdtOtoBak,
                "CSDL_OTO_BAK"),
            CsdtConnectionProfileCodes.CsdtMoto => new SourceBinding(
                CsdtConnectionProfileCodes.CsdtMotoBak,
                "CSDL_MOTO_BAK"),
            _ => throw new QlhvImportReadException(
                $"SourceProfileCode {sourceProfileCode} khong co binding database BAK an toan."),
        };

    private static SourceBinding ResolveLiveSourceBinding(string sourceProfileCode)
        => sourceProfileCode switch
        {
            CsdtConnectionProfileCodes.CsdtOto => new SourceBinding(
                CsdtConnectionProfileCodes.CsdtOto,
                "CSDL_OTO"),
            CsdtConnectionProfileCodes.CsdtMoto => new SourceBinding(
                CsdtConnectionProfileCodes.CsdtMoto,
                "CSDL_MOTO"),
            _ => throw new QlhvImportReadException(
                $"SourceProfileCode {sourceProfileCode} khong co binding database live an toan."),
        };

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

    private static IReadOnlyList<QlhvEntityFullSyncTargetRow> MapEntityTargetRows(
        IReadOnlyList<EntityHashRow> rows)
        => rows
            .Select(row => new QlhvEntityFullSyncTargetRow(
                row.SourceKey?.Trim() ?? string.Empty,
                row.SourceHash,
                row.IsDeleted))
            .ToArray();

    private static int CountDuplicateEntityTargetIdentities(
        IReadOnlyList<QlhvEntityFullSyncTargetRow> rows)
        => rows
            .Where(row => !string.IsNullOrWhiteSpace(row.SourceKey))
            .GroupBy(row => row.SourceKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Skip(1).Any());

    private const string SourceSchemaSql = @"
SELECT
    CAST(CASE WHEN OBJECT_ID(N'dbo.NguoiLX', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS NguoiLxExists,
    CAST(CASE WHEN OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS NguoiLxHoSoExists,
    CAST(CASE WHEN OBJECT_ID(N'dbo.KhoaHoc', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS KhoaHocExists,
    CAST(CASE WHEN OBJECT_ID(N'dbo.GiaoVien', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS GiaoVienExists,
    CAST(CASE WHEN OBJECT_ID(N'dbo.KhoaHoc_GiaoVien', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS KhoaHocGiaoVienExists,
    CAST(CASE WHEN OBJECT_ID(N'dbo.DM_HangDT', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS DmHangDtExists,
    CAST(CASE WHEN OBJECT_ID(N'dbo.DM_DVHC', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS DmDvhcExists,
    CAST(CASE WHEN COL_LENGTH(N'dbo.KhoaHoc', N'MaCSDT') IS NULL THEN 0 ELSE 1 END AS bit) AS KhoaHocHasMaCsdt,
    CAST(CASE WHEN COL_LENGTH(N'dbo.NguoiLX_HoSo', N'DuongDanAnh') IS NULL THEN 0 ELSE 1 END AS bit) AS HasDuongDanAnh,
    CAST(CASE WHEN COL_LENGTH(N'dbo.NguoiLX_HoSo', N'ChatLuongAnh') IS NULL THEN 0 ELSE 1 END AS bit) AS HasChatLuongAnh,
    CAST(CASE WHEN COL_LENGTH(N'dbo.NguoiLX_HoSo', N'NgayThuNhanAnh') IS NULL THEN 0 ELSE 1 END AS bit) AS HasNgayThuNhanAnh,
    CAST(CASE WHEN COL_LENGTH(N'dbo.NguoiLX_HoSo', N'NguoiThuNhanAnh') IS NULL THEN 0 ELSE 1 END AS bit) AS HasNguoiThuNhanAnh;";

    private const string BackupSnapshotMetadataSql = @"
SELECT
    databaseRow.create_date AS CreateDate,
    CAST(snapshotProperty.value AS nvarchar(512)) AS SnapshotToken,
    (SELECT COUNT(1) FROM dbo.NguoiLX) AS NguoiLXRows,
    (SELECT COUNT(1) FROM dbo.NguoiLX_HoSo) AS NguoiLXHoSoRows,
    (SELECT COUNT(1) FROM dbo.KhoaHoc) AS KhoaHocRows,
    (SELECT COUNT(1) FROM dbo.GiaoVien) AS GiaoVienRows,
    (SELECT COUNT(1) FROM dbo.KhoaHoc_GiaoVien) AS KhoaHocGiaoVienRows
FROM sys.databases AS databaseRow
OUTER APPLY
(
    SELECT TOP (1) extendedProperty.value
    FROM sys.extended_properties AS extendedProperty
    WHERE extendedProperty.class = 0
      AND extendedProperty.name = @SnapshotPropertyName
) AS snapshotProperty
WHERE databaseRow.name = DB_NAME();";

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
        (18, N'AnhRelativePath'),
        (19, N'ChatLuongAnh'),
        (20, N'NgayThuNhanAnh'),
        (21, N'NguoiThuNhanAnh'),
        (22, N'SourceOfTruth'),
        (23, N'V2RowHash'),
        (24, N'LastSyncFromV2At'),
        (25, N'LastSyncStatus'),
        (26, N'LastSyncMessage'),
        (27, N'IsDeleted'),
        (28, N'DeletedAt'),
        (29, N'DeletedBy'),
        (30, N'DeleteReason'),
        (31, N'UpdatedAt'),
        (32, N'UpdatedBy'),
        (33, N'CreatedBy')
) AS requiredColumns(SortOrder, ColumnName)
LEFT JOIN sys.columns AS targetColumn
    ON targetColumn.object_id = OBJECT_ID(N'dbo.App_HocVien', N'U')
   AND targetColumn.name = requiredColumns.ColumnName
ORDER BY requiredColumns.SortOrder;";

    private const string TargetCourseTeacherWriteColumnsSql = @"
SELECT
    requiredColumns.TableName,
    requiredColumns.ColumnName,
    CAST(CASE WHEN targetColumn.column_id IS NULL THEN 0 ELSE 1 END AS bit) AS [Exists]
FROM (
    VALUES
        (N'dbo.App_KhoaHoc', N'SourceProfileCode'),
        (N'dbo.App_KhoaHoc', N'SourceMaKhoaHoc'),
        (N'dbo.App_KhoaHoc', N'SourceHash'),
        (N'dbo.App_KhoaHoc', N'MaKhoa'),
        (N'dbo.App_KhoaHoc', N'TenKhoa'),
        (N'dbo.App_KhoaHoc', N'MaCSDT'),
        (N'dbo.App_KhoaHoc', N'MaSoGTVT'),
        (N'dbo.App_KhoaHoc', N'HangGPLX'),
        (N'dbo.App_KhoaHoc', N'HangDaoTao'),
        (N'dbo.App_KhoaHoc', N'SoQuyetDinhKhaiGiang'),
        (N'dbo.App_KhoaHoc', N'NgayQuyetDinhKhaiGiang'),
        (N'dbo.App_KhoaHoc', N'NgayKhaiGiang'),
        (N'dbo.App_KhoaHoc', N'NgayBeGiang'),
        (N'dbo.App_KhoaHoc', N'MucTieuDaoTao'),
        (N'dbo.App_KhoaHoc', N'NgayThi'),
        (N'dbo.App_KhoaHoc', N'NgaySatHach'),
        (N'dbo.App_KhoaHoc', N'TongSoHocVien'),
        (N'dbo.App_KhoaHoc', N'SoHocVienTotNghiep'),
        (N'dbo.App_KhoaHoc', N'SoHocVienDuocCapGPLX'),
        (N'dbo.App_KhoaHoc', N'ThoiGianDaoTao'),
        (N'dbo.App_KhoaHoc', N'SoNgayOnKiemTra'),
        (N'dbo.App_KhoaHoc', N'SoNgayThucHoc'),
        (N'dbo.App_KhoaHoc', N'SoNgayNghiLe'),
        (N'dbo.App_KhoaHoc', N'TongSoNgay'),
        (N'dbo.App_KhoaHoc', N'GhiChuV2'),
        (N'dbo.App_KhoaHoc', N'TrangThaiNguon'),
        (N'dbo.App_KhoaHoc', N'TtXuLy'),
        (N'dbo.App_KhoaHoc', N'HinhThucDaoTao'),
        (N'dbo.App_KhoaHoc', N'V2RowHash'),
        (N'dbo.App_KhoaHoc', N'LastSyncFromV2At'),
        (N'dbo.App_KhoaHoc', N'IsDeleted'),
        (N'dbo.App_KhoaHoc', N'CreatedAtUtc'),
        (N'dbo.App_KhoaHoc', N'UpdatedAtUtc'),

        (N'dbo.App_GiaoVien', N'SourceProfileCode'),
        (N'dbo.App_GiaoVien', N'SourceMaGV'),
        (N'dbo.App_GiaoVien', N'SourceHash'),
        (N'dbo.App_GiaoVien', N'MaGV'),
        (N'dbo.App_GiaoVien', N'HoTen'),
        (N'dbo.App_GiaoVien', N'HoTenDem'),
        (N'dbo.App_GiaoVien', N'TenGV'),
        (N'dbo.App_GiaoVien', N'NgaySinh'),
        (N'dbo.App_GiaoVien', N'SoCCCD'),
        (N'dbo.App_GiaoVien', N'GioiTinh'),
        (N'dbo.App_GiaoVien', N'DienThoai'),
        (N'dbo.App_GiaoVien', N'DiaChi'),
        (N'dbo.App_GiaoVien', N'NoiCtMaDvhc'),
        (N'dbo.App_GiaoVien', N'NoiCtMaDvql'),
        (N'dbo.App_GiaoVien', N'HinhThucTuyenDung'),
        (N'dbo.App_GiaoVien', N'TrinhDoVanHoa'),
        (N'dbo.App_GiaoVien', N'TrinhDoChuyenMon'),
        (N'dbo.App_GiaoVien', N'TrinhDoSuPham'),
        (N'dbo.App_GiaoVien', N'HangGPLX'),
        (N'dbo.App_GiaoVien', N'NgayCapGPLX'),
        (N'dbo.App_GiaoVien', N'NgayHetHanGPLX'),
        (N'dbo.App_GiaoVien', N'ThamNienLaiXe'),
        (N'dbo.App_GiaoVien', N'MonHocGiangDay'),
        (N'dbo.App_GiaoVien', N'AnhRelativePath'),
        (N'dbo.App_GiaoVien', N'GhiChu'),
        (N'dbo.App_GiaoVien', N'MaCSDT'),
        (N'dbo.App_GiaoVien', N'MaSoGTVT'),
        (N'dbo.App_GiaoVien', N'SoQuyetDinhGCN'),
        (N'dbo.App_GiaoVien', N'NgayQuyetDinhGCN'),
        (N'dbo.App_GiaoVien', N'LoaiHinhDaoTao'),
        (N'dbo.App_GiaoVien', N'CacHangGPLXDuocDaoTao'),
        (N'dbo.App_GiaoVien', N'CauTaoSuaChua'),
        (N'dbo.App_GiaoVien', N'DaoDucLaiXe'),
        (N'dbo.App_GiaoVien', N'NghiepVuVanTai'),
        (N'dbo.App_GiaoVien', N'LuatGTDB'),
        (N'dbo.App_GiaoVien', N'KyThuatLaiXe'),
        (N'dbo.App_GiaoVien', N'MaFileTiepNhanXml'),
        (N'dbo.App_GiaoVien', N'ThoiGianTiepNhanXml'),
        (N'dbo.App_GiaoVien', N'NoiCapGCN'),
        (N'dbo.App_GiaoVien', N'LoaiGiaoVien'),
        (N'dbo.App_GiaoVien', N'CacHangDaCo'),
        (N'dbo.App_GiaoVien', N'TrangThaiNguon'),
        (N'dbo.App_GiaoVien', N'V2RowHash'),
        (N'dbo.App_GiaoVien', N'LastSyncFromV2At'),
        (N'dbo.App_GiaoVien', N'IsDeleted'),
        (N'dbo.App_GiaoVien', N'CreatedAtUtc'),
        (N'dbo.App_GiaoVien', N'UpdatedAtUtc'),

        (N'dbo.App_KhoaHoc_GiaoVien', N'SourceProfileCode'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'SourceMaLichLV'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'SourceMaKhoaHoc'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'SourceMaGV'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'SourceHash'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'MaKhoa'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'MaGV'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'TenGV'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'MaMonHoc'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'TenMonHoc'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'BienSoXe'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'LoaiGV'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'SoHocVien'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'NgayHieuLuc'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'NgayHetHieuLuc'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'NgayBatDau'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'NgayKetThuc'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'IsKhoaHocGiaoVien'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'GhiChu'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'TrangThaiNguon'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'V2RowHash'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'LastSyncFromV2At'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'IsDeleted'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'CreatedAtUtc'),
        (N'dbo.App_KhoaHoc_GiaoVien', N'UpdatedAtUtc')
) AS requiredColumns(TableName, ColumnName)
LEFT JOIN sys.columns AS targetColumn
    ON targetColumn.object_id = OBJECT_ID(requiredColumns.TableName, N'U')
   AND targetColumn.name = requiredColumns.ColumnName
ORDER BY requiredColumns.TableName, requiredColumns.ColumnName;";

    private const string TargetEntityPartitionRowsSql = @"
SELECT SourceMaKhoaHoc AS SourceKey, SourceHash, IsDeleted
FROM dbo.App_KhoaHoc
WHERE SourceProfileCode = @SourceProfileCode
ORDER BY SourceMaKhoaHoc, KhoaHocId;

SELECT SourceMaGV AS SourceKey, SourceHash, IsDeleted
FROM dbo.App_GiaoVien
WHERE SourceProfileCode = @SourceProfileCode
ORDER BY SourceMaGV, GiaoVienId;

SELECT CONVERT(nvarchar(30), SourceMaLichLV) AS SourceKey, SourceHash, IsDeleted
FROM dbo.App_KhoaHoc_GiaoVien
WHERE SourceProfileCode = @SourceProfileCode
ORDER BY SourceMaLichLV, Id;";

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

    private const string TargetPartitionRowsSql = @"
SELECT SourceMaDK, V2RowHash, IsDeleted
FROM dbo.App_HocVien
WHERE SourceProfileCode = @SourceProfileCode
ORDER BY SourceMaDK, HocVienId;";

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
        public bool GiaoVienExists { get; init; }
        public bool KhoaHocGiaoVienExists { get; init; }
        public bool DmHangDtExists { get; init; }
        public bool DmDvhcExists { get; init; }
        public bool KhoaHocHasMaCsdt { get; init; }
        public bool HasDuongDanAnh { get; init; }
        public bool HasChatLuongAnh { get; init; }
        public bool HasNgayThuNhanAnh { get; init; }
        public bool HasNguoiThuNhanAnh { get; init; }

        public IReadOnlyList<string> MissingRequiredTables()
        {
            var missing = new List<string>();
            if (!NguoiLxExists) missing.Add("dbo.NguoiLX");
            if (!NguoiLxHoSoExists) missing.Add("dbo.NguoiLX_HoSo");
            if (!KhoaHocExists) missing.Add("dbo.KhoaHoc");
            if (!GiaoVienExists) missing.Add("dbo.GiaoVien");
            if (!KhoaHocGiaoVienExists) missing.Add("dbo.KhoaHoc_GiaoVien");
            if (!DmHangDtExists) missing.Add("dbo.DM_HangDT");
            if (!DmDvhcExists) missing.Add("dbo.DM_DVHC");
            return missing;
        }

        public IReadOnlyList<string> MissingRequiredImageColumns()
        {
            var missing = new List<string>();
            if (!HasDuongDanAnh) missing.Add("dbo.NguoiLX_HoSo.DuongDanAnh");
            if (!HasChatLuongAnh) missing.Add("dbo.NguoiLX_HoSo.ChatLuongAnh");
            if (!HasNgayThuNhanAnh) missing.Add("dbo.NguoiLX_HoSo.NgayThuNhanAnh");
            if (!HasNguoiThuNhanAnh) missing.Add("dbo.NguoiLX_HoSo.NguoiThuNhanAnh");
            return missing;
        }
    }

    private sealed class TargetImportSchemaRow
    {
        public bool AppKhoaHocExists { get; init; }
        public bool AppKhoaHocHasMaKhoa { get; init; }
        public bool AppKhoaHocHasIsDeleted { get; init; }
    }

    private sealed class BackupSnapshotMetadataRow
    {
        public DateTime CreateDate { get; init; }
        public string? SnapshotToken { get; init; }
        public int NguoiLXRows { get; init; }
        public int NguoiLXHoSoRows { get; init; }
        public int KhoaHocRows { get; init; }
        public int GiaoVienRows { get; init; }
        public int KhoaHocGiaoVienRows { get; init; }
    }

    private sealed class ExistingHashRow
    {
        public string SourceMaDK { get; init; } = string.Empty;
        public string? V2RowHash { get; init; }
        public bool IsDeleted { get; init; }
    }

    private sealed class EntityHashRow
    {
        public string? SourceKey { get; init; }
        public string? SourceHash { get; init; }
        public bool IsDeleted { get; init; }
    }

    private sealed class RequiredTableColumnCheckRow
    {
        public string TableName { get; init; } = string.Empty;
        public string ColumnName { get; init; } = string.Empty;
        public bool Exists { get; init; }
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

    private sealed record SourceBinding(string ConnectionProfileCode, string ExpectedDatabaseName);
}

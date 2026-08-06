using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.CsdtConnections;
using QLHV.Application.CsdtConnections.Dtos;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using QLHV.Application.Sync.Rt01;

namespace QLHV.Infrastructure.Sync.Rt01;

/// <summary>
/// Explicitly invoked SELECT-only evidence reader for RT-01A. This type is not
/// registered in either production host and has no dependency on a writer.
/// </summary>
public sealed class Rt01aOtoDriftEvidenceReader
{
    private const string AuthModeSqlLogin = "SqlLogin";

    private readonly QlhvImportReadRepository _importReads;
    private readonly ICsdtConnectionProfileRepository _profiles;
    private readonly IConnectionPasswordProtector _passwordProtector;
    private readonly IConnectionSettingsProvider _connections;
    private readonly SyncOptions _options;
    private readonly TimeProvider _timeProvider;

    public Rt01aOtoDriftEvidenceReader(
        QlhvImportReadRepository importReads,
        ICsdtConnectionProfileRepository profiles,
        IConnectionPasswordProtector passwordProtector,
        IConnectionSettingsProvider connections,
        IOptions<SyncOptions> options,
        TimeProvider? timeProvider = null)
    {
        _importReads = importReads;
        _profiles = profiles;
        _passwordProtector = passwordProtector;
        _connections = connections;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Rt01aRawProbe> ReadAsync(CancellationToken cancellationToken = default)
        => await ReadAsync(Rt01ShadowRouteCatalog.Oto, cancellationToken);

    public async Task<Rt01aRawProbe> ReadAsync(
        Rt01ShadowRoute route,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!Rt01ShadowRouteCatalog.Ordered.Contains(route))
        {
            throw new ArgumentException(
                "The production drift route must be the fixed live OTO or MOTO route.",
                nameof(route));
        }

        var request = new QlhvImportRequest
        {
            SourceProfileCode = route.SourceProfileCode,
            MaCSDT = route.MaCsdt,
        };

        var sourceStarted = UtcNow();
        var sourceSnapshot = await _importReads.ReadLiveSourceAsync(request, cancellationToken);
        if (!string.Equals(
                sourceSnapshot.SourceDatabaseName,
                route.SourceDatabaseName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"RT-01A source database must be {route.SourceDatabaseName}.");
        }

        var sourceConnectionString = await ResolveLiveAsync(route, cancellationToken);
        await using var sourceConnection = new SqlConnection(sourceConnectionString);
        await sourceConnection.OpenAsync(cancellationToken);
        var sourceSchemaRows = (await sourceConnection.QueryAsync<SchemaColumnRow>(
            new CommandDefinition(
                SourceSchemaFingerprintSql,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken))).ToArray();
        var sourceIdentity = new HocVienSourceIdentityContext(route.SourceProfileCode, "V2");
        var mappedRows = sourceSnapshot.HocVienRows
            .Select(row => QlhvImportHocVienMapper.MapAndValidate(row, sourceIdentity))
            .Where(result => !result.ShouldSkip && result.Model is not null)
            .Select(result => result.Model!)
            .ToArray();

        var targetStarted = UtcNow();
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new InvalidOperationException("RT-01A target connection is not usable.");
        }

        await using var targetConnection = new SqlConnection(target.ConnectionString);
        await targetConnection.OpenAsync(cancellationToken);
        var targetSchemaRows = (await targetConnection.QueryAsync<SchemaColumnRow>(
            new CommandDefinition(
                TargetSchemaFingerprintSql,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken))).ToArray();
        var targetCollation = await targetConnection.ExecuteScalarAsync<string>(
            new CommandDefinition(
                TargetIdentityCollationSql,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken)) ?? string.Empty;

        var sourceKeysJson = JsonSerializer.Serialize(mappedRows.Select((row, ordinal) => new
        {
            Ordinal = ordinal,
            Key = row.SourceMaDK,
        }));
        using var targetGrid = await targetConnection.QueryMultipleAsync(new CommandDefinition(
            TargetEvidenceSql,
            new
            {
                SourceProfileCode = route.SourceProfileCode,
                SourceKeysJson = sourceKeysJson,
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        var targetRows = (await targetGrid.ReadAsync<Rt01aTargetHocVienRow>()).ToArray();
        var sqlIdentityMatches = (await targetGrid.ReadAsync<Rt01aSqlIdentityMatch>()).ToArray();
        var targetCompleted = UtcNow();
        var sourceIdentitySet = mappedRows
            .Select(row => row.SourceMaDK.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetOnlyRows = targetRows
            .Where(row =>
                !row.IsDeleted &&
                string.Equals(
                    row.SourceProfileCode?.Trim(),
                    route.SourceProfileCode,
                    StringComparison.OrdinalIgnoreCase) &&
                !sourceIdentitySet.Contains(row.SourceMaDK?.Trim() ?? string.Empty))
            .Select(row => new
            {
                TargetHocVienId = row.HocVienId,
                Key = row.SourceMaDK,
            })
            .ToArray();
        var targetOnlySourcePresence = new List<Rt01aSourcePresenceEvidence>();
        foreach (var targetOnly in targetOnlyRows)
        {
            var presence = await sourceConnection.QuerySingleAsync<SourcePresenceRow>(
                new CommandDefinition(
                    SourcePresenceSql,
                    new
                    {
                        SourceKey = targetOnly.Key,
                        MaCsdt = route.MaCsdt,
                        MaDkPrefix = route.MaCsdt + "%",
                    },
                    commandTimeout: _options.TimeoutSeconds,
                    cancellationToken: cancellationToken));
            targetOnlySourcePresence.Add(new Rt01aSourcePresenceEvidence(
                targetOnly.TargetHocVienId,
                presence.NguoiLxExists,
                presence.NguoiLxHoSoExists,
                presence.WouldPassCurrentSourceScope));
        }
        var sourceCompleted = UtcNow();

        return new Rt01aRawProbe(
            mappedRows,
            targetRows,
            sqlIdentityMatches,
            targetOnlySourcePresence,
            new Rt01aReadWindow(
                sourceStarted,
                sourceCompleted,
                targetStarted,
                targetCompleted),
            FingerprintSchema(sourceSchemaRows),
            FingerprintSchema(targetSchemaRows),
            targetCollation);
    }

    private async Task<string> ResolveLiveAsync(
        Rt01ShadowRoute route,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetByCodeAsync(
            route.SourceProfileCode,
            cancellationToken);
        if (profile is null || !profile.IsActive)
        {
            throw new InvalidOperationException(
                $"{route.SourceProfileCode} profile is missing or inactive.");
        }

        if (string.IsNullOrWhiteSpace(profile.ServerName) ||
            !string.Equals(
                profile.DatabaseName,
                route.SourceDatabaseName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{route.SourceProfileCode} does not resolve to {route.SourceDatabaseName}.");
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
                !profile.IsPasswordConfigured ||
                !_passwordProtector.IsAvailable)
            {
                throw new InvalidOperationException("CSDT_OTO SQL credentials are unavailable.");
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

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string FingerprintSchema(IEnumerable<SchemaColumnRow> rows)
    {
        var canonical = string.Join(
            "\n",
            rows.OrderBy(row => row.SchemaName, StringComparer.Ordinal)
                .ThenBy(row => row.TableName, StringComparer.Ordinal)
                .ThenBy(row => row.ColumnId)
                .Select(row =>
                    $"{row.SchemaName}|{row.TableName}|{row.ColumnId}|" +
                    $"{row.ColumnName}|{row.TypeName}|{row.MaxLength}|" +
                    $"{row.Precision}|{row.Scale}|{row.IsNullable}|" +
                    $"{row.CollationName ?? string.Empty}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private const string SourceSchemaFingerprintSql = @"
SELECT
    schemaRow.name AS SchemaName,
    tableRow.name AS TableName,
    columnRow.column_id AS ColumnId,
    columnRow.name AS ColumnName,
    typeRow.name AS TypeName,
    columnRow.max_length AS MaxLength,
    columnRow.precision AS [Precision],
    columnRow.scale AS Scale,
    columnRow.is_nullable AS IsNullable,
    columnRow.collation_name AS CollationName
FROM sys.tables AS tableRow
INNER JOIN sys.schemas AS schemaRow ON schemaRow.schema_id = tableRow.schema_id
INNER JOIN sys.columns AS columnRow ON columnRow.object_id = tableRow.object_id
INNER JOIN sys.types AS typeRow ON typeRow.user_type_id = columnRow.user_type_id
WHERE schemaRow.name = N'dbo'
  AND tableRow.name IN (
      N'NguoiLX',
      N'NguoiLX_HoSo',
      N'KhoaHoc',
      N'DM_HangDT',
      N'DM_DVHC'
  )
ORDER BY schemaRow.name, tableRow.name, columnRow.column_id;";

    private const string TargetSchemaFingerprintSql = @"
SELECT
    schemaRow.name AS SchemaName,
    tableRow.name AS TableName,
    columnRow.column_id AS ColumnId,
    columnRow.name AS ColumnName,
    typeRow.name AS TypeName,
    columnRow.max_length AS MaxLength,
    columnRow.precision AS [Precision],
    columnRow.scale AS Scale,
    columnRow.is_nullable AS IsNullable,
    columnRow.collation_name AS CollationName
FROM sys.tables AS tableRow
INNER JOIN sys.schemas AS schemaRow ON schemaRow.schema_id = tableRow.schema_id
INNER JOIN sys.columns AS columnRow ON columnRow.object_id = tableRow.object_id
INNER JOIN sys.types AS typeRow ON typeRow.user_type_id = columnRow.user_type_id
WHERE schemaRow.name = N'dbo'
  AND tableRow.name IN (
      N'App_HocVien',
      N'App_QlhvSyncOperationHistory',
      N'App_QlhvAutoSyncRun',
      N'App_QlhvSyncPartitionState'
  )
ORDER BY schemaRow.name, tableRow.name, columnRow.column_id;";

    private const string TargetIdentityCollationSql = @"
SELECT columnRow.collation_name
FROM sys.columns AS columnRow
WHERE columnRow.object_id = OBJECT_ID(N'dbo.App_HocVien', N'U')
  AND columnRow.name = N'SourceMaDK';";

    private const string TargetEvidenceSql = @"
WITH sourceKeys AS
(
    SELECT
        sourceJson.Ordinal,
        sourceJson.SourceKey
    FROM OPENJSON(@SourceKeysJson)
    WITH
    (
        Ordinal int '$.Ordinal',
        SourceKey nvarchar(50) '$.Key'
    ) AS sourceJson
)
SELECT
    target.HocVienId,
    target.SourceProfileCode,
    target.SourceMaDK,
    target.SourceSystem,
    target.SourceVersion,
    target.MaDK,
    target.MaKhoa,
    target.TenKhoa,
    target.MaHangDT,
    target.HangGPLXHoc,
    target.HoTen,
    target.NgaySinh,
    target.GioiTinh,
    target.SoCCCD,
    target.DiaChiThuongTru,
    target.SoGPLXDaCo,
    target.HangGPLXDaCo,
    target.NguoiNhanHoSo,
    target.AnhRelativePath,
    target.ChatLuongAnh,
    target.NgayThuNhanAnh,
    target.NguoiThuNhanAnh,
    target.SourceOfTruth,
    target.V2RowHash,
    target.IsDeleted,
    target.CreatedAt,
    target.UpdatedAt,
    target.LastSyncFromV2At,
    target.CreatedBy,
    target.UpdatedBy,
    target.DeletedBy,
    target.DeleteReason,
    target.GhiChuNoiBo,
    target.DaDoiChieuCCCD AS DaDoiChieuCccd,
    target.DaInThe,
    target.DaTaoXML AS DaTaoXml
FROM dbo.App_HocVien AS target
WHERE target.SourceProfileCode = @SourceProfileCode
   OR EXISTS
      (
          SELECT 1
          FROM sourceKeys AS sourceRow
          WHERE target.SourceMaDK = sourceRow.SourceKey
      )
ORDER BY target.SourceProfileCode, target.SourceMaDK, target.HocVienId;

WITH sourceKeys AS
(
    SELECT
        sourceJson.Ordinal,
        sourceJson.SourceKey
    FROM OPENJSON(@SourceKeysJson)
    WITH
    (
        Ordinal int '$.Ordinal',
        SourceKey nvarchar(50) '$.Key'
    ) AS sourceJson
)
SELECT
    sourceRow.Ordinal AS SourceOrdinal,
    target.HocVienId AS TargetHocVienId
FROM sourceKeys AS sourceRow
INNER JOIN dbo.App_HocVien AS target
    ON target.SourceMaDK = sourceRow.SourceKey
ORDER BY sourceRow.Ordinal, target.HocVienId;";

    private const string SourcePresenceSql = @"
SELECT
    CAST(CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.NguoiLX AS learner
        WHERE learner.MaDK = @SourceKey
    ) THEN 1 ELSE 0 END AS bit) AS NguoiLxExists,
    CAST(CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.NguoiLX_HoSo AS dossier
        WHERE dossier.MaDK = @SourceKey
    ) THEN 1 ELSE 0 END AS bit) AS NguoiLxHoSoExists,
    CAST(CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.NguoiLX AS learner
        INNER JOIN dbo.NguoiLX_HoSo AS dossier ON dossier.MaDK = learner.MaDK
        LEFT JOIN dbo.KhoaHoc AS course ON course.MaKH = dossier.MaKhoaHoc
        WHERE learner.MaDK = @SourceKey
          AND
          (
              LTRIM(RTRIM(learner.MaDK)) LIKE @MaDkPrefix
              OR LTRIM(RTRIM(course.MaCSDT)) = @MaCsdt
          )
    ) THEN 1 ELSE 0 END AS bit) AS WouldPassCurrentSourceScope;";

    private sealed class SchemaColumnRow
    {
        public string SchemaName { get; init; } = string.Empty;
        public string TableName { get; init; } = string.Empty;
        public int ColumnId { get; init; }
        public string ColumnName { get; init; } = string.Empty;
        public string TypeName { get; init; } = string.Empty;
        public short MaxLength { get; init; }
        public byte Precision { get; init; }
        public byte Scale { get; init; }
        public bool IsNullable { get; init; }
        public string? CollationName { get; init; }
    }

    private sealed class SourcePresenceRow
    {
        public bool NguoiLxExists { get; init; }
        public bool NguoiLxHoSoExists { get; init; }
        public bool WouldPassCurrentSourceScope { get; init; }
    }
}

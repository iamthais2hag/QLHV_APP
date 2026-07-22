using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

public sealed class QlhvOperationsRepository : IQlhvOperationsRepository
{
    private readonly QlhvOperationConnectionResolver _resolver;
    private readonly IConnectionSettingsProvider _connections;

    public QlhvOperationsRepository(
        QlhvOperationConnectionResolver resolver,
        IConnectionSettingsProvider connections)
    {
        _resolver = resolver;
        _connections = connections;
    }

    public async Task<QlhvOperationDataSnapshot> ReadStatusSnapshotAsync(
        QlhvOperationSourceDefinition source,
        CancellationToken cancellationToken = default)
    {
        var allowed = QlhvOperationSourceCatalog.GetRequired(source.SourceType);
        if (source != allowed)
        {
            throw new InvalidOperationException("Status source khong nam trong allowlist.");
        }

        var liveProfile = await _resolver.ResolveAsync(
            allowed.LiveProfileCode,
            allowed.LiveDatabaseName,
            cancellationToken);
        var backupProfile = await _resolver.ResolveAsync(
            allowed.BackupReadProfileCode,
            allowed.BackupDatabaseName,
            cancellationToken);

        await using var liveConnection = new SqlConnection(liveProfile.ConnectionString);
        await using var backupConnection = new SqlConnection(backupProfile.ConnectionString);
        await liveConnection.OpenAsync(cancellationToken);
        await backupConnection.OpenAsync(cancellationToken);

        var liveIdentity = await ReadAndValidateIdentityAsync(
            liveConnection,
            allowed.LiveDatabaseName,
            cancellationToken);
        var backupIdentity = await ReadAndValidateIdentityAsync(
            backupConnection,
            allowed.BackupDatabaseName,
            cancellationToken);
        if (!string.Equals(liveIdentity.ServerName, backupIdentity.ServerName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Live DB va BAK DB phai nam tren cung mot SQL Server.");
        }

        var liveRows = await ReadCountsAsync(liveConnection, cancellationToken);
        var backupMetadata = await backupConnection.QuerySingleAsync<SnapshotMetadataRow>(new CommandDefinition(
            SnapshotMetadataSql,
            new { SnapshotPropertyName = QlhvBackupSnapshotToken.ExtendedPropertyName },
            cancellationToken: cancellationToken));
        var backupRows = backupMetadata.ToCounts();
        var token = string.IsNullOrWhiteSpace(backupMetadata.SnapshotToken)
            ? QlhvBackupSnapshotToken.CreateMetadataFallback(
                allowed.BackupDatabaseName,
                DateTime.SpecifyKind(backupMetadata.CreateDate, DateTimeKind.Utc),
                backupRows)
            : backupMetadata.SnapshotToken.Trim();

        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new InvalidOperationException("QLHV_APP connection chua san sang.");
        }

        await using var targetConnection = new SqlConnection(target.ConnectionString);
        var targetActiveRows = await targetConnection.ExecuteScalarAsync<int>(new CommandDefinition(
            TargetRowsSql,
            new { allowed.SourceProfileCode },
            cancellationToken: cancellationToken));

        return new QlhvOperationDataSnapshot(liveRows, backupRows, targetActiveRows, token);
    }

    internal static async Task<DatabaseIdentityRow> ReadAndValidateIdentityAsync(
        SqlConnection connection,
        string expectedDatabaseName,
        CancellationToken cancellationToken)
    {
        var identity = await connection.QuerySingleAsync<DatabaseIdentityRow>(new CommandDefinition(
            DatabaseIdentitySql,
            cancellationToken: cancellationToken));
        if (!string.Equals(identity.DatabaseName, expectedDatabaseName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Connection dang mo database {identity.DatabaseName}; bat buoc phai la {expectedDatabaseName}.");
        }

        return identity;
    }

    internal static Task<QlhvOperationRowCountsDto> ReadCountsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
        => connection.QuerySingleAsync<QlhvOperationRowCountsDto>(new CommandDefinition(
            RowCountsSql,
            cancellationToken: cancellationToken));

    internal sealed class DatabaseIdentityRow
    {
        public string DatabaseName { get; init; } = string.Empty;
        public string ServerName { get; init; } = string.Empty;
    }

    private sealed class SnapshotMetadataRow
    {
        public DateTime CreateDate { get; init; }
        public string? SnapshotToken { get; init; }
        public int NguoiLX { get; init; }
        public int NguoiLXHoSo { get; init; }
        public int KhoaHoc { get; init; }

        public QlhvOperationRowCountsDto ToCounts() => new()
        {
            NguoiLX = NguoiLX,
            NguoiLXHoSo = NguoiLXHoSo,
            KhoaHoc = KhoaHoc,
        };
    }

    private const string DatabaseIdentitySql = @"
SELECT
    DB_NAME() AS DatabaseName,
    CAST(SERVERPROPERTY(N'ServerName') AS nvarchar(256)) AS ServerName;";

    private const string RowCountsSql = @"
SELECT
    (SELECT COUNT(1) FROM dbo.NguoiLX) AS NguoiLX,
    (SELECT COUNT(1) FROM dbo.NguoiLX_HoSo) AS NguoiLXHoSo,
    (SELECT COUNT(1) FROM dbo.KhoaHoc) AS KhoaHoc;";

    private const string SnapshotMetadataSql = @"
SELECT
    databaseRow.create_date AS CreateDate,
    CAST(snapshotProperty.value AS nvarchar(512)) AS SnapshotToken,
    (SELECT COUNT(1) FROM dbo.NguoiLX) AS NguoiLX,
    (SELECT COUNT(1) FROM dbo.NguoiLX_HoSo) AS NguoiLXHoSo,
    (SELECT COUNT(1) FROM dbo.KhoaHoc) AS KhoaHoc
FROM sys.databases AS databaseRow
OUTER APPLY
(
    SELECT TOP (1) extendedProperty.value
    FROM sys.extended_properties AS extendedProperty
    WHERE extendedProperty.class = 0
      AND extendedProperty.name = @SnapshotPropertyName
) AS snapshotProperty
WHERE databaseRow.name = DB_NAME();";

    private const string TargetRowsSql = @"
SELECT COUNT(1)
FROM dbo.App_HocVien
WHERE SourceProfileCode = @SourceProfileCode
  AND IsDeleted = 0;";
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync.VehicleRealtime;

namespace QLHV.Infrastructure.Sync.VehicleRealtime;

internal sealed class SqlVehicleRealtimeSourceFeed : IVehicleRealtimeSourceFeed
{
    private readonly QlhvOperationConnectionResolver _connections;

    public SqlVehicleRealtimeSourceFeed(QlhvOperationConnectionResolver connections)
    {
        _connections = connections;
    }

    public async Task<VehicleSourceBatch> ReadNextAsync(
        VehicleRealtimeRoute route,
        VehicleRealtimeCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(checkpoint);
        var profile = await _connections.ResolveAsync(
            route.SourceProfileCode,
            route.SourceDatabaseName,
            cancellationToken);
        await using var connection = new SqlConnection(profile.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Snapshot,
            cancellationToken);
        try
        {
            var capabilityRow = await ReadCapabilityAsync(
                connection,
                transaction,
                cancellationToken);
            var schemaFingerprint = await ReadSchemaFingerprintAsync(
                connection,
                transaction,
                cancellationToken);
            var capability = ToCapability(route, capabilityRow, schemaFingerprint);
            ValidateCapability(route, checkpoint, capability);

            var rows = (await connection.QueryAsync<SourceChangeRow>(
                new CommandDefinition(
                    ReadNextChangeVersionSql,
                    new
                    {
                        CheckpointVersion = checkpoint.LastCtVersion,
                        SealedCurrentVersion = capability.CurrentCtVersion,
                    },
                    transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken))).ToArray();
            var checkpointAfter = rows.Length == 0
                ? capability.CurrentCtVersion
                : rows[0].SourceCtVersion;
            if (rows.Any(row => row.SourceCtVersion != checkpointAfter))
            {
                throw new VehicleRealtimeSafetyException(
                    VehicleRealtimeErrorCodes.UnsafePlan,
                    "The source reader returned more than one dbo.XeTap CT version.");
            }

            var changes = rows.Select(row => ToChange(route, row)).ToArray();
            return new VehicleSourceBatch(
                capability,
                checkpoint.LastCtVersion,
                checkpointAfter,
                capability.CurrentCtVersion,
                changes);
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    public async Task<bool> RevalidateKeysAsync(
        VehicleRealtimeRoute route,
        VehicleSourceBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(batch);
        var keys = batch.Changes
            .Select(change => change.Identity.SourceBienSoXe)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (keys.Length == 0)
        {
            return true;
        }

        var profile = await _connections.ResolveAsync(
            route.SourceProfileCode,
            route.SourceDatabaseName,
            cancellationToken);
        await using var connection = new SqlConnection(profile.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var identity = await connection.QuerySingleAsync<SourceIdentityRow>(
            new CommandDefinition(
                SourceIdentitySql,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        if (!string.Equals(identity.DatabaseName, route.SourceDatabaseName,
                StringComparison.Ordinal) ||
            identity.DatabaseGuid != route.ExpectedProductionDatabaseGuid)
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.SourceIdentityRejected,
                "Vehicle source identity changed during key revalidation.");
        }

        var laterChanges = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                RevalidateKeysSql,
                new
                {
                    SealedCurrentVersion = batch.SealedCurrentVersion,
                    SourceBienSoXe = keys,
                },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        return laterChanges == 0;
    }

    private static async Task<SourceCapabilityRow> ReadCapabilityAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
        => await connection.QuerySingleAsync<SourceCapabilityRow>(
            new CommandDefinition(
                SourceCapabilitySql,
                transaction: transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));

    private static async Task<string> ReadSchemaFingerprintAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var metadata = (await connection.QueryAsync<SourceMetadataRow>(
            new CommandDefinition(
                SourceMetadataSql,
                transaction: transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken))).ToArray();
        ValidateMetadata(metadata);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in metadata.OrderBy(item => item.ColumnId))
        {
            Append(row.ColumnId.ToString(CultureInfo.InvariantCulture));
            Append(row.Name);
            Append(row.SqlType);
            Append(row.MaxLength.ToString(CultureInfo.InvariantCulture));
            Append(row.Precision.ToString(CultureInfo.InvariantCulture));
            Append(row.Scale.ToString(CultureInfo.InvariantCulture));
            Append(row.IsNullable ? "1" : "0");
            Append(row.CollationName);
            Append(row.PrimaryKeyOrdinal?.ToString(CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

        void Append(string? value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
    }

    private static VehicleSourceCapability ToCapability(
        VehicleRealtimeRoute route,
        SourceCapabilityRow row,
        string sourceSchemaFingerprint)
        => new(
            route.SourceProfileCode,
            row.DatabaseName,
            row.DatabaseGuid,
            row.SnapshotIsolationEnabled,
            row.ChangeTrackingEnabled,
            row.TrackColumnsUpdated,
            row.CurrentCtVersion ?? -1,
            row.MinimumValidVersion ?? -1,
            sourceSchemaFingerprint);

    private static void ValidateCapability(
        VehicleRealtimeRoute route,
        VehicleRealtimeCheckpoint checkpoint,
        VehicleSourceCapability capability)
    {
        if (!string.Equals(
                capability.DatabaseName,
                route.SourceDatabaseName,
                StringComparison.Ordinal) ||
            capability.DatabaseGuid != route.ExpectedProductionDatabaseGuid ||
            capability.DatabaseGuid != checkpoint.SourceDatabaseGuid)
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.SourceIdentityRejected,
                "Resolved profile is not the exact production vehicle source database.");
        }

        if (!capability.SnapshotIsolationEnabled ||
            !capability.ChangeTrackingEnabled ||
            !capability.TrackColumnsUpdated ||
            capability.CurrentCtVersion < 0 ||
            capability.MinimumValidVersion < 0)
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.ChangeTrackingUnavailable,
                "dbo.XeTap is not ready for snapshot/Change Tracking ingestion.");
        }

        if (checkpoint.LastCtVersion < capability.MinimumValidVersion)
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.ChangeTrackingExpired,
                "Vehicle checkpoint fell behind its source CT retention window.");
        }
    }

    private static VehicleSourceChange ToChange(
        VehicleRealtimeRoute route,
        SourceChangeRow row)
    {
        var identity = VehicleSourceIdentity.Create(
            route.SourceProfileCode,
            row.SourceBienSoXe);
        if (string.Equals(row.Operation, "D", StringComparison.Ordinal))
        {
            return new VehicleSourceChange(
                row.SourceCtVersion,
                VehicleSourceChangeKind.Delete,
                identity,
                null);
        }

        if (row.Operation is not ("I" or "U") ||
            string.IsNullOrWhiteSpace(row.BienSoXe))
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.UnsafePlan,
                "dbo.XeTap CT operation/current-row pairing is unsafe.");
        }

        return new VehicleSourceChange(
            row.SourceCtVersion,
            VehicleSourceChangeKind.Upsert,
            identity,
            new VehicleSourceRow
            {
                BienSoXe = row.BienSoXe,
                MaSoGTVT = row.MaSoGTVT ?? string.Empty,
                MaCSDT = row.MaCSDT ?? string.Empty,
                SoDK = row.SoDK,
                SoHuu = row.SoHuu,
                NhanHieu = row.NhanHieu,
                LoaiXe = row.LoaiXe,
                MacXe = row.MacXe,
                HangXe = row.HangXe,
                MauXe = row.MauXe,
                SoDongCo = row.SoDongCo,
                SoKhung = row.SoKhung,
                GiayPhepXTL = row.GiayPhepXTL,
                SoGPXTL = row.SoGPXTL,
                CoQuanCapGPXTL = row.CoQuanCapGPXTL,
                NgayCapGPXTL = row.NgayCapGPXTL,
                NgayHHGPXTL = row.NgayHHGPXTL,
                NamSX = row.NamSX,
                HeThongPP = row.HeThongPP,
                NgayCapGCNKD = row.NgayCapGCNKD,
                NgayHHGCNKD = row.NgayHHGCNKD,
                BaoHiem = row.BaoHiem,
                TuyenDuong = row.TuyenDuong,
                ChatLuong = row.ChatLuong,
                GhiChu = row.GhiChu,
                TrangThai = row.TrangThai,
                NguoiTao = row.NguoiTao,
                NguoiSua = row.NguoiSua,
                NgayTao = row.NgayTao,
                NgaySua = row.NgaySua,
                DuongDanAnh = row.DuongDanAnh,
                HangGPLXXe = row.HangGPLXXe,
                MaFileTiepNhanXML = row.MaFileTiepNhanXML,
                ThoiGianTiepNhanXML = row.ThoiGianTiepNhanXML,
            });
    }

    private static void ValidateMetadata(IReadOnlyCollection<SourceMetadataRow> metadata)
    {
        if (metadata.Count != ExpectedColumns.Count)
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.SourceSchemaMismatch,
                "dbo.XeTap does not have the exact 34-column source contract.");
        }

        var expectedOrdinal = 0;
        foreach (var expected in ExpectedColumns)
        {
            expectedOrdinal++;
            var actual = metadata.SingleOrDefault(column =>
                string.Equals(column.Name, expected.Key, StringComparison.Ordinal));
            if (actual is null ||
                actual.ColumnId != expectedOrdinal ||
                !string.Equals(actual.SqlType, expected.Value.Type, StringComparison.Ordinal) ||
                actual.MaxLength != expected.Value.MaxLength ||
                actual.IsNullable != expected.Value.Nullable ||
                !string.Equals(
                    actual.CollationName,
                    expected.Value.Type is "varchar" or "nvarchar"
                        ? "SQL_Latin1_General_CP1_CI_AS"
                        : null,
                    StringComparison.Ordinal) ||
                actual.PrimaryKeyOrdinal != expected.Value.PrimaryKeyOrdinal)
            {
                throw new VehicleRealtimeSafetyException(
                    VehicleRealtimeErrorCodes.SourceSchemaMismatch,
                    $"dbo.XeTap column {expected.Key} does not match its exact source contract.");
            }
        }
    }

    internal static IReadOnlyDictionary<string, ExpectedColumn> ExpectedColumns { get; } =
        new Dictionary<string, ExpectedColumn>(StringComparer.Ordinal)
        {
            ["BienSoXe"] = new("varchar", 10, false, 1),
            ["MaSoGTVT"] = new("varchar", 6, false, null),
            ["MaCSDT"] = new("varchar", 6, false, null),
            ["SoDK"] = new("nvarchar", 100, true, null),
            ["SoHuu"] = new("bit", 1, false, null),
            ["NhanHieu"] = new("nvarchar", 100, true, null),
            ["LoaiXe"] = new("nvarchar", 100, true, null),
            ["MacXe"] = new("nvarchar", 100, true, null),
            ["HangXe"] = new("nvarchar", 100, true, null),
            ["MauXe"] = new("nvarchar", 100, true, null),
            ["SoDongCo"] = new("varchar", 20, true, null),
            ["SoKhung"] = new("varchar", 20, true, null),
            ["GiayPhepXTL"] = new("bit", 1, true, null),
            ["SoGPXTL"] = new("nvarchar", 60, true, null),
            ["CoQuanCapGPXTL"] = new("nvarchar", 100, true, null),
            ["NgayCapGPXTL"] = new("datetime", 8, true, null),
            ["NgayHHGPXTL"] = new("datetime", 8, true, null),
            ["NamSX"] = new("int", 4, true, null),
            ["HeThongPP"] = new("bit", 1, true, null),
            ["NgayCapGCNKD"] = new("datetime", 8, true, null),
            ["NgayHHGCNKD"] = new("datetime", 8, true, null),
            ["BaoHiem"] = new("bit", 1, true, null),
            ["TuyenDuong"] = new("nvarchar", 100, true, null),
            ["ChatLuong"] = new("nvarchar", 100, true, null),
            ["GhiChu"] = new("nvarchar", 510, true, null),
            ["TrangThai"] = new("bit", 1, false, null),
            ["NguoiTao"] = new("nvarchar", 60, true, null),
            ["NguoiSua"] = new("nvarchar", 60, true, null),
            ["NgayTao"] = new("datetime", 8, false, null),
            ["NgaySua"] = new("datetime", 8, false, null),
            ["DuongDanAnh"] = new("nvarchar", 300, true, null),
            ["HangGPLXXe"] = new("varchar", 10, true, null),
            ["MaFileTiepNhanXML"] = new("nvarchar", 100, true, null),
            ["ThoiGianTiepNhanXML"] = new("datetime", 8, true, null),
        };

    internal sealed record ExpectedColumn(
        string Type,
        short MaxLength,
        bool Nullable,
        int? PrimaryKeyOrdinal);

    private sealed class SourceCapabilityRow
    {
        public string DatabaseName { get; init; } = string.Empty;
        public Guid DatabaseGuid { get; init; }
        public bool SnapshotIsolationEnabled { get; init; }
        public bool ChangeTrackingEnabled { get; init; }
        public bool TrackColumnsUpdated { get; init; }
        public long? CurrentCtVersion { get; init; }
        public long? MinimumValidVersion { get; init; }
    }

    private sealed class SourceIdentityRow
    {
        public string DatabaseName { get; init; } = string.Empty;
        public Guid DatabaseGuid { get; init; }
    }

    private sealed class SourceMetadataRow
    {
        public int ColumnId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string SqlType { get; init; } = string.Empty;
        public short MaxLength { get; init; }
        public byte Precision { get; init; }
        public byte Scale { get; init; }
        public bool IsNullable { get; init; }
        public string? CollationName { get; init; }
        public int? PrimaryKeyOrdinal { get; init; }
    }

    private sealed class SourceChangeRow
    {
        public long SourceCtVersion { get; init; }
        public string Operation { get; init; } = string.Empty;
        public string SourceBienSoXe { get; init; } = string.Empty;
        public string? BienSoXe { get; init; }
        public string? MaSoGTVT { get; init; }
        public string? MaCSDT { get; init; }
        public string? SoDK { get; init; }
        public bool SoHuu { get; init; }
        public string? NhanHieu { get; init; }
        public string? LoaiXe { get; init; }
        public string? MacXe { get; init; }
        public string? HangXe { get; init; }
        public string? MauXe { get; init; }
        public string? SoDongCo { get; init; }
        public string? SoKhung { get; init; }
        public bool? GiayPhepXTL { get; init; }
        public string? SoGPXTL { get; init; }
        public string? CoQuanCapGPXTL { get; init; }
        public DateTime? NgayCapGPXTL { get; init; }
        public DateTime? NgayHHGPXTL { get; init; }
        public int? NamSX { get; init; }
        public bool? HeThongPP { get; init; }
        public DateTime? NgayCapGCNKD { get; init; }
        public DateTime? NgayHHGCNKD { get; init; }
        public bool? BaoHiem { get; init; }
        public string? TuyenDuong { get; init; }
        public string? ChatLuong { get; init; }
        public string? GhiChu { get; init; }
        public bool TrangThai { get; init; }
        public string? NguoiTao { get; init; }
        public string? NguoiSua { get; init; }
        public DateTime NgayTao { get; init; }
        public DateTime NgaySua { get; init; }
        public string? DuongDanAnh { get; init; }
        public string? HangGPLXXe { get; init; }
        public string? MaFileTiepNhanXML { get; init; }
        public DateTime? ThoiGianTiepNhanXML { get; init; }
    }

    internal const string SourceIdentitySql = """
        SELECT DB_NAME() AS DatabaseName, identityRow.database_guid AS DatabaseGuid
        FROM sys.database_recovery_status identityRow
        WHERE identityRow.database_id=DB_ID();
        """;

    internal const string SourceCapabilitySql = """
        SELECT DB_NAME() AS DatabaseName,
               identityRow.database_guid AS DatabaseGuid,
               CONVERT(bit, CASE WHEN databaseRow.snapshot_isolation_state=1 THEN 1 ELSE 0 END)
                   AS SnapshotIsolationEnabled,
               CONVERT(bit, CASE WHEN trackingDatabase.database_id IS NULL THEN 0 ELSE 1 END)
                   AS ChangeTrackingEnabled,
               CONVERT(bit, ISNULL(trackingTable.is_track_columns_updated_on,0))
                   AS TrackColumnsUpdated,
               CONVERT(bigint, CHANGE_TRACKING_CURRENT_VERSION()) AS CurrentCtVersion,
               CONVERT(bigint, CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.XeTap')))
                   AS MinimumValidVersion
        FROM sys.database_recovery_status identityRow
        INNER JOIN sys.databases databaseRow
          ON databaseRow.database_id=identityRow.database_id
        LEFT JOIN sys.change_tracking_databases trackingDatabase
          ON trackingDatabase.database_id=identityRow.database_id
        LEFT JOIN sys.change_tracking_tables trackingTable
          ON trackingTable.object_id=OBJECT_ID(N'dbo.XeTap',N'U')
        WHERE identityRow.database_id=DB_ID();
        """;

    internal const string SourceMetadataSql = """
        SELECT columnRow.column_id AS ColumnId, columnRow.name AS Name,
               typeRow.name AS SqlType, columnRow.max_length AS MaxLength,
               columnRow.precision AS [Precision], columnRow.scale AS Scale,
               columnRow.is_nullable AS IsNullable,
               columnRow.collation_name AS CollationName,
               indexColumn.key_ordinal AS PrimaryKeyOrdinal
        FROM sys.columns columnRow
        INNER JOIN sys.types typeRow
          ON typeRow.user_type_id=columnRow.user_type_id
        LEFT JOIN sys.indexes primaryIndex
          ON primaryIndex.object_id=columnRow.object_id
         AND primaryIndex.is_primary_key=1
        LEFT JOIN sys.index_columns indexColumn
          ON indexColumn.object_id=columnRow.object_id
         AND indexColumn.index_id=primaryIndex.index_id
         AND indexColumn.column_id=columnRow.column_id
        WHERE columnRow.object_id=OBJECT_ID(N'dbo.XeTap',N'U')
        ORDER BY columnRow.column_id;
        """;

    internal const string ReadNextChangeVersionSql = """
        ;WITH Pending AS
        (
            SELECT CONVERT(bigint, changeRow.SYS_CHANGE_VERSION) AS SourceCtVersion,
                   CONVERT(nvarchar(1), changeRow.SYS_CHANGE_OPERATION) AS Operation,
                   CONVERT(varchar(10), changeRow.BienSoXe) AS SourceBienSoXe
            FROM CHANGETABLE(CHANGES dbo.XeTap,@CheckpointVersion) changeRow
            WHERE changeRow.SYS_CHANGE_VERSION<=@SealedCurrentVersion
        ),
        NextVersion AS
        (
            SELECT MIN(SourceCtVersion) AS SourceCtVersion FROM Pending
        )
        SELECT pending.SourceCtVersion,pending.Operation,pending.SourceBienSoXe,
               currentRow.BienSoXe,currentRow.MaSoGTVT,currentRow.MaCSDT,currentRow.SoDK,
               currentRow.SoHuu,currentRow.NhanHieu,currentRow.LoaiXe,currentRow.MacXe,
               currentRow.HangXe,currentRow.MauXe,currentRow.SoDongCo,currentRow.SoKhung,
               currentRow.GiayPhepXTL,currentRow.SoGPXTL,currentRow.CoQuanCapGPXTL,
               currentRow.NgayCapGPXTL,currentRow.NgayHHGPXTL,currentRow.NamSX,
               currentRow.HeThongPP,currentRow.NgayCapGCNKD,currentRow.NgayHHGCNKD,
               currentRow.BaoHiem,currentRow.TuyenDuong,currentRow.ChatLuong,
               currentRow.GhiChu,currentRow.TrangThai,currentRow.NguoiTao,
               currentRow.NguoiSua,currentRow.NgayTao,currentRow.NgaySua,
               currentRow.DuongDanAnh,currentRow.HangGPLXXe,
               currentRow.MaFileTiepNhanXML,currentRow.ThoiGianTiepNhanXML
        FROM Pending pending
        INNER JOIN NextVersion nextVersion
          ON nextVersion.SourceCtVersion=pending.SourceCtVersion
        LEFT JOIN dbo.XeTap currentRow
          ON currentRow.BienSoXe=pending.SourceBienSoXe
        ORDER BY pending.SourceBienSoXe;
        """;

    internal const string RevalidateKeysSql = """
        SELECT COUNT(1)
        FROM CHANGETABLE(CHANGES dbo.XeTap,@SealedCurrentVersion) changeRow
        WHERE changeRow.SYS_CHANGE_VERSION>@SealedCurrentVersion
          AND changeRow.BienSoXe IN @SourceBienSoXe;
        """;
}

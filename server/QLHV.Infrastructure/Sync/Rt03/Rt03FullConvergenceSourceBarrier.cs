using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using QLHV.Application.Sync.Rt03;
using QLHV.Application.Sync.VehicleRealtime;

namespace QLHV.Infrastructure.Sync.Rt03;

internal sealed class Rt03FullConvergenceSourceBarrierFactory
{
    private readonly IConfiguration _configuration;

    public Rt03FullConvergenceSourceBarrierFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<Rt03FullConvergenceSourceBarrier> AcquireAsync(
        string sourceProfileCode,
        long committedCheckpoint,
        CancellationToken cancellationToken)
    {
        var route = VehicleRealtimeRouteCatalog.GetRequired(sourceProfileCode);
        var connectionString =
            _configuration.GetConnectionString(route.SourceDatabaseName);
        if (string.IsNullOrWhiteSpace(connectionString) ||
            connectionString.Contains("__", StringComparison.Ordinal))
        {
            throw new Rt03SafetyException(
                Rt03Errors.ConfigurationRejected,
                "Live source connection is not configured.");
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(
                builder.InitialCatalog,
                route.SourceDatabaseName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new Rt03SafetyException(
                Rt03Errors.ProductionIdentityRejected,
                "Live source Initial Catalog does not match the fixed route.");
        }

        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var transaction =
                (SqlTransaction)await connection.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
            try
            {
                var identity = await connection.QuerySingleAsync<SourceIdentityRow>(
                    new CommandDefinition(
                        IdentitySql,
                        transaction: transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));
                if (!string.Equals(
                        identity.DatabaseName,
                        route.SourceDatabaseName,
                        StringComparison.Ordinal) ||
                    identity.DatabaseGuid != route.ExpectedProductionDatabaseGuid ||
                    identity.AnchorVersion < 0)
                {
                    throw new Rt03SafetyException(
                        Rt03Errors.ProductionIdentityRejected,
                        "Live source database name/GUID/anchor is not exact.");
                }

                // Shared table locks are held until the recovery attempt finishes.
                // Other readers remain compatible while source writers are blocked.
                await connection.ExecuteAsync(new CommandDefinition(
                    AcquireReadBarrierSql,
                    transaction: transaction,
                    commandTimeout: 120,
                    cancellationToken: cancellationToken));
                var metadata = (await connection.QueryAsync<SchemaRow>(
                    new CommandDefinition(
                        SchemaFingerprintSql,
                        transaction: transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken))).ToArray();
                var audits = (await connection.QueryAsync<TableAuditRow>(
                    new CommandDefinition(
                        TableAuditSql,
                        new { CommittedCheckpoint = committedCheckpoint },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken)))
                    .Select(row => row.ToContract(sourceProfileCode))
                    .ToArray();
                if (audits.Length != 8 ||
                    audits.Any(row =>
                        Rt03ChangeTrackingRecoveryClassifier.Classify(row) is
                            Rt03RecoveryClassifications.Unclassified or
                            Rt03RecoveryClassifications.UnsafeDeleteContract))
                {
                    throw new Rt03SafetyException(
                        Rt03Errors.ChangeTrackingWindowRejected,
                        "One or more recovery source tables are unclassified.");
                }

                return new Rt03FullConvergenceSourceBarrier(
                    connection,
                    transaction,
                    route,
                    identity.DatabaseGuid,
                    identity.AnchorVersion,
                    ComputeSchemaFingerprint(metadata),
                    audits);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
                throw;
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static string ComputeSchemaFingerprint(IEnumerable<SchemaRow> rows)
    {
        var canonical = string.Join(
            "\n",
            rows.Select(row =>
                string.Join(
                    "|",
                    row.TableName,
                    row.ColumnId,
                    row.ColumnName,
                    row.TypeName,
                    row.MaxLength,
                    row.Precision,
                    row.Scale,
                    row.IsNullable,
                    row.PrimaryKeyOrdinal)));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private sealed class SourceIdentityRow
    {
        public string DatabaseName { get; init; } = string.Empty;
        public Guid DatabaseGuid { get; init; }
        public long AnchorVersion { get; init; }
    }

    private sealed class SchemaRow
    {
        public string TableName { get; init; } = string.Empty;
        public int ColumnId { get; init; }
        public string ColumnName { get; init; } = string.Empty;
        public string TypeName { get; init; } = string.Empty;
        public short MaxLength { get; init; }
        public byte Precision { get; init; }
        public byte Scale { get; init; }
        public bool IsNullable { get; init; }
        public int PrimaryKeyOrdinal { get; init; }
    }

    private sealed class TableAuditRow
    {
        public string TableName { get; init; } = string.Empty;
        public bool TableExists { get; init; }
        public bool ChangeTrackingEnabled { get; init; }
        public long? MinimumValidVersion { get; init; }

        public Rt03TrackedTableAudit ToContract(string sourceProfileCode)
            => new(
                sourceProfileCode,
                $"dbo.{TableName}",
                TableExists,
                ChangeTrackingEnabled,
                MinimumValidVersion,
                CommittedCheckpoint,
                DeleteContractVerified: true);

        public long CommittedCheckpoint { get; init; }
    }

    internal const string IdentitySql = """
        SELECT DB_NAME() DatabaseName,identityRow.database_guid DatabaseGuid,
               CONVERT(bigint,CHANGE_TRACKING_CURRENT_VERSION()) AnchorVersion
        FROM sys.database_recovery_status identityRow
        WHERE identityRow.database_id=DB_ID();
        """;

    internal const string AcquireReadBarrierSql = """
        DECLARE @Rows bigint;
        SELECT @Rows=COUNT_BIG(*) FROM dbo.KhoaHoc WITH(TABLOCK,HOLDLOCK);
        SELECT @Rows=COUNT_BIG(*) FROM dbo.GiaoVien WITH(TABLOCK,HOLDLOCK);
        SELECT @Rows=COUNT_BIG(*) FROM dbo.XeTap WITH(TABLOCK,HOLDLOCK);
        SELECT @Rows=COUNT_BIG(*) FROM dbo.NguoiLX WITH(TABLOCK,HOLDLOCK);
        SELECT @Rows=COUNT_BIG(*) FROM dbo.NguoiLX_HoSo WITH(TABLOCK,HOLDLOCK);
        SELECT @Rows=COUNT_BIG(*) FROM dbo.DM_HangDT WITH(TABLOCK,HOLDLOCK);
        SELECT @Rows=COUNT_BIG(*) FROM dbo.DM_DVHC WITH(TABLOCK,HOLDLOCK);
        SELECT @Rows=COUNT_BIG(*) FROM dbo.KhoaHoc_GiaoVien WITH(TABLOCK,HOLDLOCK);
        """;

    internal const string SchemaFingerprintSql = """
        WITH Expected(TableName) AS
        (
            SELECT value FROM
            (VALUES
                (N'KhoaHoc'),(N'GiaoVien'),(N'XeTap'),(N'NguoiLX'),
                (N'NguoiLX_HoSo'),(N'DM_HangDT'),(N'DM_DVHC'),
                (N'KhoaHoc_GiaoVien')
            ) item(value)
        )
        SELECT tableRow.name TableName,columnRow.column_id ColumnId,
               columnRow.name ColumnName,typeRow.name TypeName,
               columnRow.max_length MaxLength,columnRow.precision [Precision],
               columnRow.scale Scale,columnRow.is_nullable IsNullable,
               COALESCE(indexColumn.key_ordinal,0) PrimaryKeyOrdinal
        FROM Expected expected
        INNER JOIN sys.tables tableRow
          ON tableRow.schema_id=SCHEMA_ID(N'dbo')
         AND tableRow.name=expected.TableName
        INNER JOIN sys.columns columnRow
          ON columnRow.object_id=tableRow.object_id
        INNER JOIN sys.types typeRow
          ON typeRow.user_type_id=columnRow.user_type_id
        LEFT JOIN sys.indexes primaryIndex
          ON primaryIndex.object_id=tableRow.object_id
         AND primaryIndex.is_primary_key=1
        LEFT JOIN sys.index_columns indexColumn
          ON indexColumn.object_id=columnRow.object_id
         AND indexColumn.index_id=primaryIndex.index_id
         AND indexColumn.column_id=columnRow.column_id
        ORDER BY tableRow.name,columnRow.column_id;
        """;

    internal const string TableAuditSql = """
        WITH Expected(TableName) AS
        (
            SELECT value FROM
            (VALUES
                (N'KhoaHoc'),(N'GiaoVien'),(N'XeTap'),(N'NguoiLX'),
                (N'NguoiLX_HoSo'),(N'DM_HangDT'),(N'DM_DVHC'),
                (N'KhoaHoc_GiaoVien')
            ) item(value)
        )
        SELECT expected.TableName,
               CONVERT(bit,CASE WHEN tableRow.object_id IS NULL THEN 0 ELSE 1 END)
                   TableExists,
               CONVERT(bit,CASE WHEN tracking.object_id IS NULL THEN 0 ELSE 1 END)
                   ChangeTrackingEnabled,
               CONVERT(bigint,CASE WHEN tracking.object_id IS NULL THEN NULL
                    ELSE CHANGE_TRACKING_MIN_VALID_VERSION(tableRow.object_id) END)
                   MinimumValidVersion,
               CONVERT(bigint,@CommittedCheckpoint) CommittedCheckpoint
        FROM Expected expected
        LEFT JOIN sys.tables tableRow
          ON tableRow.schema_id=SCHEMA_ID(N'dbo')
         AND tableRow.name=expected.TableName
        LEFT JOIN sys.change_tracking_tables tracking
          ON tracking.object_id=tableRow.object_id
        ORDER BY expected.TableName;
        """;
}

internal sealed class Rt03FullConvergenceSourceBarrier : IAsyncDisposable
{
    private SqlConnection? _connection;
    private SqlTransaction? _transaction;

    public Rt03FullConvergenceSourceBarrier(
        SqlConnection connection,
        SqlTransaction transaction,
        VehicleRealtimeRoute route,
        Guid sourceDatabaseGuid,
        long anchorVersion,
        string sourceSchemaFingerprint,
        IReadOnlyList<Rt03TrackedTableAudit> audits)
    {
        _connection = connection;
        _transaction = transaction;
        Route = route;
        SourceDatabaseGuid = sourceDatabaseGuid;
        AnchorVersion = anchorVersion;
        SourceSchemaFingerprint = sourceSchemaFingerprint;
        Audits = audits;
    }

    public VehicleRealtimeRoute Route { get; }
    public Guid SourceDatabaseGuid { get; }
    public long AnchorVersion { get; }
    public string SourceSchemaFingerprint { get; }
    public IReadOnlyList<Rt03TrackedTableAudit> Audits { get; }

    internal string SourceConnectionString =>
        _connection?.ConnectionString ??
        throw new ObjectDisposedException(nameof(Rt03FullConvergenceSourceBarrier));

    public async Task<IReadOnlyList<VehicleSourceRow>> ReadVehiclesAsync(
        CancellationToken cancellationToken)
    {
        var connection = _connection ??
            throw new ObjectDisposedException(nameof(Rt03FullConvergenceSourceBarrier));
        var transaction = _transaction ??
            throw new ObjectDisposedException(nameof(Rt03FullConvergenceSourceBarrier));
        return (await connection.QueryAsync<VehicleSourceRow>(
            new CommandDefinition(
                VehicleSnapshotSql,
                new { MaCSDT = Route.ExpectedMaCsdt },
                transaction,
                commandTimeout: 120,
                cancellationToken: cancellationToken))).ToArray();
    }

    public async Task<long> ReadCurrentVersionAsync(
        CancellationToken cancellationToken)
    {
        var connection = _connection ??
            throw new ObjectDisposedException(nameof(Rt03FullConvergenceSourceBarrier));
        var transaction = _transaction ??
            throw new ObjectDisposedException(nameof(Rt03FullConvergenceSourceBarrier));
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT CONVERT(bigint,CHANGE_TRACKING_CURRENT_VERSION());",
            transaction: transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        var transaction = Interlocked.Exchange(ref _transaction, null);
        var connection = Interlocked.Exchange(ref _connection, null);
        if (transaction is not null)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            finally
            {
                await transaction.DisposeAsync();
            }
        }

        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
    }

    internal const string VehicleSnapshotSql = """
        SELECT BienSoXe,MaSoGTVT,MaCSDT,SoDK,SoHuu,NhanHieu,LoaiXe,MacXe,
               HangXe,MauXe,SoDongCo,SoKhung,GiayPhepXTL,SoGPXTL,
               CoQuanCapGPXTL,NgayCapGPXTL,NgayHHGPXTL,NamSX,HeThongPP,
               NgayCapGCNKD,NgayHHGCNKD,BaoHiem,TuyenDuong,ChatLuong,
               GhiChu,TrangThai,NguoiTao,NguoiSua,NgayTao,NgaySua,
               DuongDanAnh,HangGPLXXe,MaFileTiepNhanXML,ThoiGianTiepNhanXML
        FROM dbo.XeTap
        WHERE LTRIM(RTRIM(MaCSDT))=@MaCSDT
        ORDER BY BienSoXe;
        """;
}

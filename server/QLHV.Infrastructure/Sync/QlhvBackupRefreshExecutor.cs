using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Dtos;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Infrastructure.Sync;

public sealed class QlhvBackupRefreshExecutor : IQlhvBackupRefreshExecutor
{
    internal const string BackupDirectory = @"D:\SQL_BACKUP";

    private readonly QlhvOperationConnectionResolver _resolver;
    private readonly AppSyncOptions _syncOptions;
    private readonly SyncExecutionOptions _executionOptions;
    private readonly QlhvOperationsOptions _operationsOptions;

    public QlhvBackupRefreshExecutor(
        QlhvOperationConnectionResolver resolver,
        IOptions<AppSyncOptions> syncOptions,
        IOptions<SyncExecutionOptions> executionOptions,
        IOptions<QlhvOperationsOptions> operationsOptions)
    {
        _resolver = resolver;
        _syncOptions = syncOptions.Value;
        _executionOptions = executionOptions.Value;
        _operationsOptions = operationsOptions.Value;
    }

    public async Task<QlhvRefreshBackupExecutionResult> ExecuteAsync(
        QlhvOperationSourceDefinition source,
        CancellationToken cancellationToken = default)
    {
        var allowed = QlhvOperationSourceCatalog.GetRequired(source.SourceType);
        if (source != allowed)
        {
            throw new InvalidOperationException("Refresh source khong nam trong allowlist.");
        }

        if (_syncOptions.DryRun || !_executionOptions.EnableTargetWrites)
        {
            throw new InvalidOperationException("Refresh BAK bi chan boi cau hinh an toan.");
        }

        var live = await _resolver.ResolveAsync(
            allowed.LiveProfileCode,
            allowed.LiveDatabaseName,
            cancellationToken);
        var backup = await _resolver.ResolveAsync(
            allowed.BackupReadProfileCode,
            allowed.BackupDatabaseName,
            cancellationToken);

        // Validate actual opened catalogs and SQL Server identity before touching a backup device.
        await using (var liveConnection = new SqlConnection(live.ConnectionString))
        await using (var backupConnection = new SqlConnection(backup.ConnectionString))
        {
            await liveConnection.OpenAsync(cancellationToken);
            await backupConnection.OpenAsync(cancellationToken);
            var liveIdentity = await QlhvOperationsRepository.ReadAndValidateIdentityAsync(
                liveConnection,
                allowed.LiveDatabaseName,
                cancellationToken);
            var backupIdentity = await QlhvOperationsRepository.ReadAndValidateIdentityAsync(
                backupConnection,
                allowed.BackupDatabaseName,
                cancellationToken);
            if (!string.Equals(liveIdentity.ServerName, backupIdentity.ServerName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Live DB va BAK DB phai nam tren cung mot SQL Server.");
            }
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        var suffix = Guid.NewGuid().ToString("N");
        var liveBackupPath = Path.Combine(
            BackupDirectory,
            $"{allowed.LiveDatabaseName}_live_{stamp}_{suffix}.bak");
        var preRefreshPath = Path.Combine(
            BackupDirectory,
            $"{allowed.BackupDatabaseName}_pre_refresh_{stamp}_{suffix}.bak");

        await using var master = new SqlConnection(live.MasterConnectionString);
        await master.OpenAsync(cancellationToken);
        var timeout = Math.Clamp(_operationsOptions.DatabaseCommandTimeoutSeconds, 60, 7200);
        await EnsureDatabaseReadyAsync(master, allowed, timeout, cancellationToken);
        var currentPhysicalFiles = await ReadCurrentPhysicalFilesAsync(
            master,
            allowed.BackupDatabaseName,
            timeout,
            cancellationToken);

        await master.ExecuteAsync(new CommandDefinition(
            BuildBackupSql(allowed.LiveDatabaseName),
            new { BackupPath = liveBackupPath },
            commandTimeout: timeout,
            cancellationToken: cancellationToken));
        await VerifyBackupAsync(master, liveBackupPath, timeout, cancellationToken);

        await master.ExecuteAsync(new CommandDefinition(
            BuildBackupSql(allowed.BackupDatabaseName),
            new { BackupPath = preRefreshPath },
            commandTimeout: timeout,
            cancellationToken: cancellationToken));
        await VerifyBackupAsync(master, preRefreshPath, timeout, cancellationToken);

        var liveFiles = await ReadBackupFilesAsync(master, liveBackupPath, timeout, cancellationToken);
        var preRefreshFiles = await ReadBackupFilesAsync(master, preRefreshPath, timeout, cancellationToken);
        var liveMappings = MapRestoreFiles(liveFiles, currentPhysicalFiles);
        var rollbackMappings = MapRestoreFiles(preRefreshFiles, currentPhysicalFiles);

        try
        {
            await RestoreAsync(
                master,
                allowed.BackupDatabaseName,
                liveBackupPath,
                liveMappings,
                timeout,
                cancellationToken);
            await EnsureOnlineAndMultiUserAsync(
                master,
                allowed.BackupDatabaseName,
                timeout,
                cancellationToken);

            var liveRows = await ReadCountsFromDatabaseAsync(
                master,
                allowed.LiveDatabaseName,
                timeout,
                cancellationToken);
            var backupRows = await ReadCountsFromDatabaseAsync(
                master,
                allowed.BackupDatabaseName,
                timeout,
                cancellationToken);
            if (!CountsEqual(liveRows, backupRows))
            {
                throw new InvalidOperationException("Row count live va BAK khong khop sau restore.");
            }

            var snapshotToken = QlhvBackupSnapshotToken.CreateAfterRefresh(
                allowed,
                backupRows,
                DateTime.UtcNow);
            await WriteSnapshotTokenAsync(backup.ConnectionString, snapshotToken, timeout, cancellationToken);
            var imagePathRows = await ReadImagePathRowsAsync(
                master,
                allowed.BackupDatabaseName,
                timeout,
                cancellationToken);
            var detailJson = JsonSerializer.Serialize(new
            {
                allowed.LiveDatabaseName,
                allowed.BackupDatabaseName,
                LiveRows = liveRows,
                BackupRows = backupRows,
                ImagePathRows = imagePathRows,
                ImageFilesCopied = false,
                Warning = imagePathRows > 0
                    ? "Snapshot co duong dan anh; file .jp2 vat ly khong nam trong pham vi database refresh/full sync."
                    : null,
            });
            return new QlhvRefreshBackupExecutionResult(
                liveRows,
                backupRows,
                snapshotToken,
                imagePathRows,
                detailJson);
        }
        catch (Exception primaryException)
        {
            var rollbackSucceeded = false;
            try
            {
                await using var rollbackMaster = new SqlConnection(live.MasterConnectionString);
                await rollbackMaster.OpenAsync(CancellationToken.None);
                await RestoreAsync(
                    rollbackMaster,
                    allowed.BackupDatabaseName,
                    preRefreshPath,
                    rollbackMappings,
                    timeout,
                    CancellationToken.None);
                rollbackSucceeded = await BestEffortOnlineAndMultiUserAsync(
                    live.MasterConnectionString,
                    allowed.BackupDatabaseName,
                    timeout);
            }
            catch
            {
                rollbackSucceeded = false;
            }

            throw new QlhvBackupRefreshException(
                rollbackSucceeded
                    ? $"Refresh BAK that bai ({primaryException.GetType().Name}); da khoi phuc pre-refresh va MULTI_USER/ONLINE."
                    : $"Refresh BAK that bai ({primaryException.GetType().Name}); khong xac nhan duoc trang thai phuc hoi BAK.",
                primaryException);
        }
        finally
        {
            await BestEffortOnlineAndMultiUserAsync(
                live.MasterConnectionString,
                allowed.BackupDatabaseName,
                timeout);
        }
    }

    public async Task<bool> TryRecoverDatabaseAccessAsync(
        QlhvOperationSourceDefinition source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var allowed = QlhvOperationSourceCatalog.GetRequired(source.SourceType);
        if (source != allowed)
        {
            throw new InvalidOperationException("Recovery source khong nam trong allowlist.");
        }

        var live = await _resolver.ResolveAsync(
            allowed.LiveProfileCode,
            allowed.LiveDatabaseName,
            cancellationToken);
        var backup = await _resolver.ResolveAsync(
            allowed.BackupReadProfileCode,
            allowed.BackupDatabaseName,
            cancellationToken);
        await using (var liveMaster = new SqlConnection(live.MasterConnectionString))
        await using (var backupMaster = new SqlConnection(backup.MasterConnectionString))
        {
            await liveMaster.OpenAsync(cancellationToken);
            await backupMaster.OpenAsync(cancellationToken);
            var liveServer = await ReadServerIdentityAsync(liveMaster, cancellationToken);
            var backupServer = await ReadServerIdentityAsync(backupMaster, cancellationToken);
            if (string.IsNullOrWhiteSpace(liveServer) ||
                string.IsNullOrWhiteSpace(backupServer) ||
                !string.Equals(liveServer, backupServer, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Live DB va BAK DB phai nam tren cung mot SQL Server de recovery.");
            }
        }

        var timeout = Math.Clamp(_operationsOptions.DatabaseCommandTimeoutSeconds, 60, 7200);
        return await BestEffortOnlineAndMultiUserAsync(
            live.MasterConnectionString,
            allowed.BackupDatabaseName,
            timeout);
    }

    private static Task<string?> ReadServerIdentityAsync(
        SqlConnection master,
        CancellationToken cancellationToken)
        => master.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT CAST(SERVERPROPERTY(N'ServerName') AS nvarchar(256));",
            cancellationToken: cancellationToken));

    internal static string BuildBackupSql(string databaseName)
        => $"BACKUP DATABASE {QuoteAllowedDatabase(databaseName)} TO DISK = @BackupPath " +
           "WITH COPY_ONLY, CHECKSUM, COMPRESSION, STATS = 10;";

    internal static string BuildRestoreSql(
        string databaseName,
        IReadOnlyList<RestoreFileMapping> mappings)
    {
        if (mappings.Count == 0)
        {
            throw new InvalidOperationException("Khong co file mapping an toan cho RESTORE.");
        }

        var moves = string.Join(
            ",\n    ",
            mappings.Select(mapping =>
                $"MOVE {QuoteSqlString(mapping.LogicalName)} TO {QuoteSqlString(mapping.PhysicalPath)}"));
        return $"RESTORE DATABASE {QuoteAllowedDatabase(databaseName)} FROM DISK = @BackupPath\n" +
               $"WITH REPLACE, RECOVERY, CHECKSUM,\n    {moves}, STATS = 10;";
    }

    private static async Task RestoreAsync(
        SqlConnection master,
        string databaseName,
        string backupPath,
        IReadOnlyList<RestoreFileMapping> mappings,
        int timeout,
        CancellationToken cancellationToken)
    {
        // Keep SINGLE_USER and RESTORE in one server batch so status/plan connections cannot
        // occupy the restore window between two separate client commands.
        var restoreBatch = BuildPrepareRestoreSql(databaseName) + Environment.NewLine +
                           BuildRestoreSql(databaseName, mappings);
        await master.ExecuteAsync(new CommandDefinition(
            restoreBatch,
            new { BackupPath = backupPath },
            commandTimeout: timeout,
            cancellationToken: cancellationToken));
    }

    private static async Task VerifyBackupAsync(
        SqlConnection master,
        string path,
        int timeout,
        CancellationToken cancellationToken)
        => await master.ExecuteAsync(new CommandDefinition(
            "RESTORE VERIFYONLY FROM DISK = @BackupPath WITH CHECKSUM;",
            new { BackupPath = path },
            commandTimeout: timeout,
            cancellationToken: cancellationToken));

    private static async Task<IReadOnlyList<BackupFileRow>> ReadBackupFilesAsync(
        SqlConnection master,
        string path,
        int timeout,
        CancellationToken cancellationToken)
    {
        var rows = await master.QueryAsync<BackupFileRow>(new CommandDefinition(
            "RESTORE FILELISTONLY FROM DISK = @BackupPath;",
            new { BackupPath = path },
            commandTimeout: timeout,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private static async Task<IReadOnlyList<PhysicalFileRow>> ReadCurrentPhysicalFilesAsync(
        SqlConnection master,
        string databaseName,
        int timeout,
        CancellationToken cancellationToken)
    {
        var rows = await master.QueryAsync<PhysicalFileRow>(new CommandDefinition(
            CurrentPhysicalFilesSql,
            new { DatabaseName = databaseName },
            commandTimeout: timeout,
            cancellationToken: cancellationToken));
        var result = rows.AsList();
        if (result.Count == 0)
        {
            throw new InvalidOperationException("Khong doc duoc physical path hien tai cua DB BAK.");
        }

        return result;
    }

    internal static IReadOnlyList<RestoreFileMapping> MapRestoreFiles(
        IReadOnlyList<BackupFileRow> sourceFiles,
        IReadOnlyList<PhysicalFileRow> targetFiles)
    {
        var unsupported = sourceFiles.Where(file => file.Type is not ("D" or "L")).ToArray();
        if (unsupported.Length > 0)
        {
            throw new InvalidOperationException("Backup co file type chua duoc ho tro de map an toan.");
        }

        var result = new List<RestoreFileMapping>();
        foreach (var type in new[] { "D", "L" })
        {
            var sources = sourceFiles.Where(file => file.Type == type).OrderBy(file => file.FileId).ToArray();
            var targets = targetFiles
                .Where(file => type == "D" ? file.Type == 0 : file.Type == 1)
                .OrderBy(file => file.FileId)
                .ToArray();
            if (sources.Length == 0 || sources.Length != targets.Length)
            {
                throw new InvalidOperationException(
                    $"So luong file {type} cua live backup va DB BAK khong khop.");
            }

            result.AddRange(sources.Zip(
                targets,
                (source, target) => new RestoreFileMapping(source.LogicalName, target.PhysicalName)));
        }

        return result;
    }

    private static async Task<QlhvOperationRowCountsDto> ReadCountsFromDatabaseAsync(
        SqlConnection master,
        string databaseName,
        int timeout,
        CancellationToken cancellationToken)
        => await master.QuerySingleAsync<QlhvOperationRowCountsDto>(new CommandDefinition(
            BuildCountsSql(databaseName),
            commandTimeout: timeout,
            cancellationToken: cancellationToken));

    private static async Task<int> ReadImagePathRowsAsync(
        SqlConnection master,
        string databaseName,
        int timeout,
        CancellationToken cancellationToken)
    {
        var database = QuoteAllowedDatabase(databaseName);
        var imageCountSql =
            $"SELECT COUNT(1) FROM {database}.dbo.NguoiLX_HoSo " +
            "WHERE NULLIF(LTRIM(RTRIM(DuongDanAnh)), N'') IS NOT NULL;";
        var sql =
            $"IF COL_LENGTH(N'{databaseName}.dbo.NguoiLX_HoSo', N'DuongDanAnh') IS NULL " +
            "SELECT CAST(0 AS int); " +
            $"ELSE EXEC({QuoteSqlString(imageCountSql)});";
        return await master.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            commandTimeout: timeout,
            cancellationToken: cancellationToken));
    }

    private static async Task WriteSnapshotTokenAsync(
        string backupConnectionString,
        string token,
        int timeout,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(backupConnectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            WriteSnapshotTokenSql,
            new
            {
                PropertyName = QlhvBackupSnapshotToken.ExtendedPropertyName,
                SnapshotToken = token,
            },
            commandTimeout: timeout,
            cancellationToken: cancellationToken));
    }

    private static async Task EnsureDatabaseReadyAsync(
        SqlConnection master,
        QlhvOperationSourceDefinition source,
        int timeout,
        CancellationToken cancellationToken)
    {
        var rows = await master.QueryAsync<DatabaseStateRow>(new CommandDefinition(
            DatabaseStateSql,
            new { LiveDatabase = source.LiveDatabaseName, BackupDatabase = source.BackupDatabaseName },
            commandTimeout: timeout,
            cancellationToken: cancellationToken));
        var states = rows.AsList();
        if (states.Count != 2 || states.Any(row => !string.Equals(row.StateDesc, "ONLINE", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Live DB va BAK DB phai ton tai va ONLINE truoc refresh.");
        }
    }

    private static async Task EnsureOnlineAndMultiUserAsync(
        SqlConnection master,
        string databaseName,
        int timeout,
        CancellationToken cancellationToken)
    {
        var database = QuoteAllowedDatabase(databaseName);
        var status = await master.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT CAST(DATABASEPROPERTYEX(@DatabaseName, N'Status') AS nvarchar(60));",
            new { DatabaseName = databaseName },
            commandTimeout: timeout,
            cancellationToken: cancellationToken));
        if (string.Equals(status, "RESTORING", StringComparison.OrdinalIgnoreCase))
        {
            await master.ExecuteAsync(new CommandDefinition(
                $"RESTORE DATABASE {database} WITH RECOVERY;",
                commandTimeout: timeout,
                cancellationToken: cancellationToken));
        }

        await master.ExecuteAsync(new CommandDefinition(
            $"ALTER DATABASE {database} SET ONLINE;",
            commandTimeout: timeout,
            cancellationToken: cancellationToken));
        await master.ExecuteAsync(new CommandDefinition(
            $"ALTER DATABASE {database} SET MULTI_USER WITH ROLLBACK IMMEDIATE;",
            commandTimeout: timeout,
            cancellationToken: cancellationToken));
    }

    private static async Task<bool> BestEffortOnlineAndMultiUserAsync(
        string masterConnectionString,
        string databaseName,
        int timeout)
    {
        var database = QuoteAllowedDatabase(databaseName);
        var recoveryOk = await TryExecuteRecoveryStepAsync(
            masterConnectionString,
            $@"IF DATABASEPROPERTYEX(@DatabaseName, N'Status') = N'RESTORING'
                    RESTORE DATABASE {database} WITH RECOVERY;",
            new { DatabaseName = databaseName },
            timeout);
        var onlineOk = await TryExecuteRecoveryStepAsync(
            masterConnectionString,
            $"ALTER DATABASE {database} SET ONLINE;",
            null,
            timeout);
        var multiUserOk = await TryExecuteRecoveryStepAsync(
            masterConnectionString,
            $"ALTER DATABASE {database} SET MULTI_USER WITH ROLLBACK IMMEDIATE;",
            null,
            timeout);

        return recoveryOk && onlineOk && multiUserOk;
    }

    private static async Task<bool> TryExecuteRecoveryStepAsync(
        string masterConnectionString,
        string sql,
        object? parameters,
        int timeout)
    {
        try
        {
            await using var connection = new SqlConnection(masterConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                parameters,
                commandTimeout: timeout,
                cancellationToken: CancellationToken.None));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool CountsEqual(QlhvOperationRowCountsDto left, QlhvOperationRowCountsDto right)
        => left.NguoiLX == right.NguoiLX &&
           left.NguoiLXHoSo == right.NguoiLXHoSo &&
           left.KhoaHoc == right.KhoaHoc;

    private static string BuildCountsSql(string databaseName)
    {
        var database = QuoteAllowedDatabase(databaseName);
        return $@"
SELECT
    (SELECT COUNT(1) FROM {database}.dbo.NguoiLX) AS NguoiLX,
    (SELECT COUNT(1) FROM {database}.dbo.NguoiLX_HoSo) AS NguoiLXHoSo,
    (SELECT COUNT(1) FROM {database}.dbo.KhoaHoc) AS KhoaHoc;";
    }

    private static string BuildPrepareRestoreSql(string databaseName)
    {
        var database = QuoteAllowedDatabase(databaseName);
        return $@"
IF DATABASEPROPERTYEX(N'{databaseName}', N'Status') <> N'RESTORING'
    ALTER DATABASE {database} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;";
    }

    private static string QuoteAllowedDatabase(string databaseName)
    {
        var allowed = QlhvOperationSourceCatalog.All.Any(source =>
            string.Equals(source.LiveDatabaseName, databaseName, StringComparison.Ordinal) ||
            string.Equals(source.BackupDatabaseName, databaseName, StringComparison.Ordinal));
        if (!allowed)
        {
            throw new InvalidOperationException("Database identifier khong nam trong allowlist.");
        }

        return $"[{databaseName.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static string QuoteSqlString(string value)
        => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private const string CurrentPhysicalFilesSql = @"
SELECT file_id AS FileId, type AS Type, physical_name AS PhysicalName
FROM sys.master_files
WHERE database_id = DB_ID(@DatabaseName)
ORDER BY type, file_id;";

    private const string DatabaseStateSql = @"
SELECT name AS DatabaseName, state_desc AS StateDesc
FROM sys.databases
WHERE name IN (@LiveDatabase, @BackupDatabase);";

    private const string WriteSnapshotTokenSql = @"
IF EXISTS
(
    SELECT 1 FROM sys.extended_properties
    WHERE class = 0 AND name = @PropertyName
)
    EXEC sys.sp_updateextendedproperty @name = @PropertyName, @value = @SnapshotToken;
ELSE
    EXEC sys.sp_addextendedproperty @name = @PropertyName, @value = @SnapshotToken;";

    internal sealed class BackupFileRow
    {
        public string LogicalName { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public int FileId { get; init; }
    }

    internal sealed class PhysicalFileRow
    {
        public int FileId { get; init; }
        public int Type { get; init; }
        public string PhysicalName { get; init; } = string.Empty;
    }

    internal sealed record RestoreFileMapping(string LogicalName, string PhysicalPath);

    private sealed class DatabaseStateRow
    {
        public string DatabaseName { get; init; } = string.Empty;
        public string StateDesc { get; init; } = string.Empty;
    }
}

public sealed class QlhvBackupRefreshException : Exception
{
    public QlhvBackupRefreshException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

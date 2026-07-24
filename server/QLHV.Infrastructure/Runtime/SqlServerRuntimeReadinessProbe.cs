using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QLHV.Application.Auth;
using QLHV.Application.CsdtConnections;
using QLHV.Application.Runtime;
using QLHV.Application.Sync.Connections;
using QLHV.Infrastructure.HocVien;
using QLHV.Infrastructure.Sync;

namespace QLHV.Infrastructure.Runtime;

public sealed class SqlServerRuntimeReadinessProbe : IRuntimeReadinessProbe
{
    private static readonly string[] RequiredTables =
    [
        "App_User",
        "App_Role",
        "App_UserRole",
        "App_HocVien",
        "App_QlhvSyncOperationHistory",
        "App_QlhvAutoSyncRun",
        "App_QlhvSyncPartitionState",
        "App_DataVersion",
    ];

    private readonly IConnectionSettingsProvider _connections;
    private readonly QlhvOperationConnectionResolver _operationConnections;
    private readonly HocVienPhotoPathResolver _photoPaths;
    private readonly QlhvRuntimeOptions _options;
    private readonly IHostEnvironment _environment;

    public SqlServerRuntimeReadinessProbe(
        IConnectionSettingsProvider connections,
        QlhvOperationConnectionResolver operationConnections,
        HocVienPhotoPathResolver photoPaths,
        IOptions<QlhvRuntimeOptions> options,
        IHostEnvironment environment)
    {
        _connections = connections;
        _operationConnections = operationConnections;
        _photoPaths = photoPaths;
        _options = options.Value;
        _environment = environment;
    }

    public async Task<RuntimeReadinessProbeResult> ProbeAsync(
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        var databaseConnected = false;
        string? databaseName = null;
        var requiredSchemaReady = false;
        var authenticationReady = false;
        var backupProfilesReady = false;
        var backupDirectoryVisibleToSql = false;

        ResolvedConnection target;
        try
        {
            target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            target = ResolvedConnection.NotConfigured("QLHV_APP");
        }

        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            messages.Add("Chưa có cấu hình ConnectionStrings:QLHV_APP hợp lệ.");
        }
        else
        {
            try
            {
                var connectionString = WithShortConnectTimeout(target.ConnectionString);
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                databaseConnected = true;
                databaseName = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                    "SELECT DB_NAME();",
                    commandTimeout: CommandTimeoutSeconds,
                    cancellationToken: cancellationToken));

                if (string.Equals(
                        databaseName,
                        RuntimeReadinessService.ExpectedDatabaseName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    var tables = (await connection.QueryAsync<string>(new CommandDefinition(
                        RequiredTablesSql,
                        new { RequiredTables },
                        commandTimeout: CommandTimeoutSeconds,
                        cancellationToken: cancellationToken))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var missingTables = RequiredTables.Where(table => !tables.Contains(table)).ToArray();
                    requiredSchemaReady = missingTables.Length == 0;
                    if (!requiredSchemaReady)
                    {
                        messages.Add($"Thiếu bảng bắt buộc: {string.Join(", ", missingTables)}.");
                    }

                    if (requiredSchemaReady)
                    {
                        var accountSchemaReady = await connection.ExecuteScalarAsync<bool>(
                            new CommandDefinition(
                                AccountManagementSchemaReadinessSql,
                                commandTimeout: CommandTimeoutSeconds,
                                cancellationToken: cancellationToken));
                        if (!accountSchemaReady)
                        {
                            requiredSchemaReady = false;
                            messages.Add(
                                "Schema quản lý tài khoản Employee chưa sẵn sàng.");
                        }
                    }

                    if (requiredSchemaReady)
                    {
                        var activeSlotReady = await connection.ExecuteScalarAsync<bool>(
                            new CommandDefinition(
                                AutoSyncActiveSlotReadinessSql,
                                commandTimeout: CommandTimeoutSeconds,
                                cancellationToken: cancellationToken));
                        if (!activeSlotReady)
                        {
                            requiredSchemaReady = false;
                            messages.Add(
                                "Auto Sync active-slot schema chua san sang hoac khong bao dam duy nhat.");
                        }
                    }

                    if (requiredSchemaReady)
                    {
                        var auth = await connection.QuerySingleAsync<AuthReadinessRow>(new CommandDefinition(
                            AuthenticationReadinessSql,
                            new
                            {
                                AdminRole = AppRoles.Admin,
                                EmployeeRole = AppRoles.Employee,
                                ViewerRole = AppRoles.Viewer,
                            },
                            commandTimeout: CommandTimeoutSeconds,
                            cancellationToken: cancellationToken));
                        var activeAdminHashes = await connection.QueryAsync<string>(
                            new CommandDefinition(
                                ActiveAdminCredentialHashesSql,
                                new { AdminRole = AppRoles.Admin },
                                commandTimeout: CommandTimeoutSeconds,
                                cancellationToken: cancellationToken));
                        var activeAdminExists =
                            activeAdminHashes.Any(AppPasswordHashFormat.IsSupported);
                        authenticationReady = auth.AdminRoleExists &&
                            auth.EmployeeRoleExists &&
                            auth.ViewerRoleExists &&
                            activeAdminExists;
                        if (!auth.AdminRoleExists ||
                            !auth.EmployeeRoleExists ||
                            !auth.ViewerRoleExists)
                        {
                            messages.Add("Thiếu role Admin, Employee hoặc Viewer.");
                        }

                        if (!activeAdminExists)
                        {
                            messages.Add("Chưa có tài khoản Admin đang hoạt động.");
                        }
                    }

                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                messages.Add(databaseConnected
                    ? "Không thể kiểm tra schema QLHV_APP."
                    : "Không thể kết nối SQL Server cho QLHV_APP.");
            }
        }

        if (databaseConnected &&
            string.Equals(databaseName, RuntimeReadinessService.ExpectedDatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            var backupValidation = await ValidateBackupProfilesAsync(messages, cancellationToken);
            backupProfilesReady = backupValidation.ProfilesReady;
            backupDirectoryVisibleToSql = backupValidation.BackupDirectoryReady;
        }

        var localBackupDirectoryReady = Directory.Exists(QlhvBackupRefreshExecutor.BackupDirectory);
        if (!localBackupDirectoryReady)
        {
            messages.Add("Không tìm thấy thư mục D:\\SQL_BACKUP trên máy chủ ứng dụng.");
        }

        var fileStorageReady = IsPhotoStorageReady();
        if (!fileStorageReady)
        {
            messages.Add("Không tìm thấy thư mục IM_GPLX đã cấu hình.");
        }

        var runtimeStorageReady = !_environment.IsProduction() || IsRuntimeStorageWritable();
        if (!runtimeStorageReady)
        {
            messages.Add("Runtime không ghi được thư mục logs hoặc run.");
        }

        return new RuntimeReadinessProbeResult
        {
            DatabaseConnected = databaseConnected,
            DatabaseName = databaseName,
            RequiredSchemaReady = requiredSchemaReady,
            AuthenticationReady = authenticationReady,
            BackupProfilesReady = backupProfilesReady,
            BackupStorageReady = localBackupDirectoryReady && backupDirectoryVisibleToSql,
            FileStorageReady = fileStorageReady,
            RuntimeStorageReady = runtimeStorageReady,
            Messages = messages,
        };
    }

    private int CommandTimeoutSeconds => Math.Clamp(_options.ReadinessTimeoutSeconds, 3, 30);

    private string WithShortConnectTimeout(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = CommandTimeoutSeconds,
        };
        return builder.ConnectionString;
    }

    private async Task<BackupValidationResult> ValidateBackupProfilesAsync(
        ICollection<string> messages,
        CancellationToken cancellationToken)
    {
        var checks = new[]
        {
            (CsdtConnectionProfileCodes.CsdtOtoBak, "CSDL_OTO_BAK"),
            (CsdtConnectionProfileCodes.CsdtMotoBak, "CSDL_MOTO_BAK"),
        };

        var profilesReady = true;
        var backupDirectoryReady = true;
        foreach (var (profileCode, expectedDatabase) in checks)
        {
            try
            {
                var profile = await _operationConnections.ResolveAsync(
                    profileCode,
                    expectedDatabase,
                    cancellationToken);
                var connectionString = WithShortConnectTimeout(profile.ConnectionString);
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                var actualDatabase = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                    "SELECT DB_NAME();",
                    commandTimeout: CommandTimeoutSeconds,
                    cancellationToken: cancellationToken));
                if (!string.Equals(actualDatabase, expectedDatabase, StringComparison.Ordinal))
                {
                    profilesReady = false;
                    backupDirectoryReady = false;
                    messages.Add($"Profile {profileCode} không mở đúng database BAK.");
                    continue;
                }

                if (!await IsDirectoryVisibleToSqlServerAsync(
                        connection,
                        QlhvBackupRefreshExecutor.BackupDirectory,
                        cancellationToken))
                {
                    backupDirectoryReady = false;
                    messages.Add(
                        $"SQL Server của profile {profileCode} không truy cập được thư mục D:\\SQL_BACKUP.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                profilesReady = false;
                backupDirectoryReady = false;
                messages.Add($"Profile {profileCode} chưa sẵn sàng.");
            }
        }

        return new BackupValidationResult(profilesReady, backupDirectoryReady);
    }

    private async Task<bool> IsDirectoryVisibleToSqlServerAsync(
        SqlConnection connection,
        string directory,
        CancellationToken cancellationToken)
    {
        try
        {
            return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                SqlDirectoryProbeSql,
                new { Directory = directory },
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: cancellationToken)) == 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private bool IsPhotoStorageReady()
    {
        try
        {
            return Directory.Exists(_photoPaths.PhotoRoot);
        }
        catch
        {
            return false;
        }
    }

    private bool IsRuntimeStorageWritable()
    {
        try
        {
            var root = Path.GetFullPath(_options.Root);
            return IsDirectoryWritable(Path.Combine(root, "logs")) &&
                IsDirectoryWritable(Path.Combine(root, "run"));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDirectoryWritable(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        var probePath = Path.Combine(directory, $".qlhv-ready-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(probePath);
            }
            catch
            {
                // The readiness probe never leaves a failure marker behind intentionally.
            }
        }
    }

    private sealed class AuthReadinessRow
    {
        public bool AdminRoleExists { get; init; }

        public bool EmployeeRoleExists { get; init; }

        public bool ViewerRoleExists { get; init; }
    }

    private sealed record BackupValidationResult(
        bool ProfilesReady,
        bool BackupDirectoryReady);

    private const string RequiredTablesSql = """
SELECT tableRow.name
FROM sys.tables AS tableRow
INNER JOIN sys.schemas AS schemaRow
    ON schemaRow.schema_id = tableRow.schema_id
WHERE schemaRow.name = N'dbo'
  AND tableRow.name IN @RequiredTables;
""";

    private const string AccountManagementSchemaReadinessSql = """
SELECT CAST
(
    CASE
        WHEN COL_LENGTH(N'dbo.App_User', N'MustChangePassword') IS NOT NULL
         AND COL_LENGTH(N'dbo.App_User', N'LastFailedLoginAt') IS NOT NULL
         AND COL_LENGTH(N'dbo.App_User', N'SecurityStamp') IS NOT NULL
         AND OBJECT_ID(N'dbo.App_UserManagementAudit', N'U') IS NOT NULL
         AND EXISTS
         (
             SELECT 1
             FROM sys.computed_columns AS computedColumn
             WHERE computedColumn.object_id = OBJECT_ID(N'dbo.App_User', N'U')
               AND computedColumn.name = N'NormalizedUserName'
               AND computedColumn.is_persisted = 1
         )
         AND EXISTS
         (
             SELECT 1
             FROM sys.indexes AS indexRow
             INNER JOIN sys.index_columns AS keyColumn
                 ON keyColumn.object_id = indexRow.object_id
                AND keyColumn.index_id = indexRow.index_id
                AND keyColumn.key_ordinal = 1
             INNER JOIN sys.columns AS columnRow
                 ON columnRow.object_id = keyColumn.object_id
                AND columnRow.column_id = keyColumn.column_id
             WHERE indexRow.object_id = OBJECT_ID(N'dbo.App_User', N'U')
               AND indexRow.is_unique = 1
               AND indexRow.is_disabled = 0
               AND columnRow.name = N'NormalizedUserName'
         )
        THEN 1
        ELSE 0
    END AS bit
);
""";

    private const string AutoSyncActiveSlotReadinessSql = """
SELECT CAST
(
    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM sys.columns AS activeSlotColumn
            INNER JOIN sys.types AS activeSlotType
                ON activeSlotType.user_type_id = activeSlotColumn.user_type_id
            WHERE activeSlotColumn.object_id =
                    OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
              AND activeSlotColumn.name = N'ActiveSlot'
              AND activeSlotType.name = N'tinyint'
              AND activeSlotColumn.max_length = 1
              AND activeSlotColumn.is_nullable = 1
              AND activeSlotColumn.is_computed = 0
        )
        AND EXISTS
        (
            SELECT 1
            FROM sys.check_constraints AS activeSlotCheck
            WHERE activeSlotCheck.parent_object_id =
                    OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
              AND activeSlotCheck.name = N'CK_App_QlhvAutoSyncRun_ActiveSlot'
              AND activeSlotCheck.is_disabled = 0
              AND activeSlotCheck.is_not_trusted = 0
        )
        AND EXISTS
        (
            SELECT 1
            FROM sys.indexes AS activeSlotIndex
            INNER JOIN sys.index_columns AS activeSlotKey
                ON activeSlotKey.object_id = activeSlotIndex.object_id
               AND activeSlotKey.index_id = activeSlotIndex.index_id
               AND activeSlotKey.key_ordinal = 1
            INNER JOIN sys.columns AS activeSlotKeyColumn
                ON activeSlotKeyColumn.object_id = activeSlotKey.object_id
               AND activeSlotKeyColumn.column_id = activeSlotKey.column_id
            WHERE activeSlotIndex.object_id =
                    OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
              AND activeSlotIndex.name = N'UX_App_QlhvAutoSyncRun_ActiveSlot'
              AND activeSlotIndex.is_unique = 1
              AND activeSlotIndex.is_disabled = 0
              AND activeSlotIndex.is_hypothetical = 0
              AND activeSlotIndex.type = 2
              AND activeSlotIndex.is_primary_key = 0
              AND activeSlotIndex.is_unique_constraint = 0
              AND activeSlotIndex.has_filter = 1
              AND activeSlotIndex.filter_definition IS NOT NULL
              AND REPLACE(
                    REPLACE(
                        REPLACE(activeSlotIndex.filter_definition, N'[', N''),
                        N']',
                        N''),
                    N' ',
                    N'') = N'(ActiveSlot=(1))'
              AND activeSlotKeyColumn.name = N'ActiveSlot'
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM sys.index_columns AS extraActiveSlotKey
                  WHERE extraActiveSlotKey.object_id = activeSlotIndex.object_id
                    AND extraActiveSlotKey.index_id = activeSlotIndex.index_id
                    AND extraActiveSlotKey.key_ordinal > 1
              )
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM sys.index_columns AS includedActiveSlotColumn
                  WHERE includedActiveSlotColumn.object_id = activeSlotIndex.object_id
                    AND includedActiveSlotColumn.index_id = activeSlotIndex.index_id
                    AND includedActiveSlotColumn.is_included_column = 1
              )
        )
        THEN 1
        ELSE 0
    END AS bit
);
""";

    private const string AuthenticationReadinessSql = """
SELECT
    CAST(CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.App_Role
        WHERE RoleCode = @AdminRole
          AND IsDeleted = 0
    ) THEN 1 ELSE 0 END AS bit) AS AdminRoleExists,
    CAST(CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.App_Role
        WHERE RoleCode = @EmployeeRole
          AND IsDeleted = 0
    ) THEN 1 ELSE 0 END AS bit) AS EmployeeRoleExists,
    CAST(CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.App_Role
        WHERE RoleCode = @ViewerRole
          AND IsDeleted = 0
    ) THEN 1 ELSE 0 END AS bit) AS ViewerRoleExists;
""";

    private const string ActiveAdminCredentialHashesSql = """
SELECT DISTINCT userRow.PasswordHash
FROM dbo.App_User AS userRow
INNER JOIN dbo.App_UserRole AS userRole
    ON userRole.UserId = userRow.UserId
INNER JOIN dbo.App_Role AS roleRow
    ON roleRow.RoleId = userRole.RoleId
WHERE userRow.IsActive = 1
  AND userRow.IsDeleted = 0
  AND userRow.SecurityStamp <>
        '00000000-0000-0000-0000-000000000000'
  AND roleRow.IsDeleted = 0
  AND roleRow.RoleCode = @AdminRole;
""";

    private const string SqlDirectoryProbeSql = """
DECLARE @result TABLE
(
    [File Exists] int,
    [File is a Directory] int,
    [Parent Directory Exists] int
);
INSERT INTO @result
EXEC master.dbo.xp_fileexist @Directory;
SELECT TOP (1) [File is a Directory]
FROM @result;
""";
}

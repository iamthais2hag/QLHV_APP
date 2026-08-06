using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace QLHV.Infrastructure.Sync.Realtime;

internal sealed class CsdtRealtimeSourceReader
{
    public async Task<long> GetCurrentVersionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var version = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT CHANGE_TRACKING_CURRENT_VERSION();",
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        return version ?? throw new CsdtRealtimeSchemaException(
            "Change Tracking is not enabled for the source database.");
    }

    public async Task<long> GetMinimumValidVersionAsync(
        string connectionString,
        CsdtRealtimeDomainDefinition domain,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var version = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(@TableName, N'U'));",
            new { TableName = $"dbo.{domain.TableName}" },
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        return version ?? throw new CsdtRealtimeSchemaException(
            $"Change Tracking is not enabled for dbo.{domain.TableName}.");
    }

    public async Task<CsdtRealtimeSnapshot> ReadPartitionSnapshotAsync(
        string connectionString,
        CsdtRealtimeDomainDefinition domain,
        string maCsdt,
        CancellationToken cancellationToken)
        => await ReadPartitionSnapshotCoreAsync(
            connectionString,
            domain,
            maCsdt,
            forwardOwnershipScope: false,
            cancellationToken);

    public async Task<CsdtRealtimeSnapshot> ReadForwardPartitionSnapshotAsync(
        string connectionString,
        CsdtRealtimeDomainDefinition domain,
        string maCsdt,
        CancellationToken cancellationToken)
        => await ReadPartitionSnapshotCoreAsync(
            connectionString,
            domain,
            maCsdt,
            forwardOwnershipScope: true,
            cancellationToken);

    private static async Task<CsdtRealtimeSnapshot> ReadPartitionSnapshotCoreAsync(
        string connectionString,
        CsdtRealtimeDomainDefinition domain,
        string maCsdt,
        bool forwardOwnershipScope,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await ReadPartitionSnapshotCoreAsync(
            connection,
            transaction: null,
            domain,
            maCsdt,
            forwardOwnershipScope,
            cancellationToken);
    }

    internal Task<CsdtRealtimeSnapshot> ReadForwardPartitionSnapshotAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtRealtimeDomainDefinition domain,
        string maCsdt,
        CancellationToken cancellationToken)
        => ReadPartitionSnapshotCoreAsync(
            connection,
            transaction,
            domain,
            maCsdt,
            forwardOwnershipScope: true,
            cancellationToken);

    private static async Task<CsdtRealtimeSnapshot> ReadPartitionSnapshotCoreAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CsdtRealtimeDomainDefinition domain,
        string maCsdt,
        bool forwardOwnershipScope,
        CancellationToken cancellationToken)
    {
        ValidateCallerConnection(connection, transaction);
        var metadata = await ReadMetadataAsync(
            connection,
            domain,
            cancellationToken,
            transaction);
        ValidatePrimaryKey(metadata);
        var selectedColumns = forwardOwnershipScope
            ? CsdtRealtimeColumnOwnershipPolicy.GetRequired(domain.Name)
                .SelectForwardReadColumns(metadata)
            : metadata.WritableColumns;

        var columnList = string.Join(
            ", ",
            selectedColumns.Select(column =>
                $"src.{CsdtRealtimeColumnMetadata.Quote(column.Name)}"));
        var sql = $"""
            SELECT {columnList}
            FROM {domain.QualifiedTableName} AS src
            WHERE ({domain.PartitionPredicate})
            ORDER BY {string.Join(", ", metadata.PrimaryKey.Select(key => $"src.{CsdtRealtimeColumnMetadata.Quote(key.Name)}"))};
            """;

        await using var command = new SqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 120,
        };
        command.Parameters.Add(new SqlParameter("@MaCSDT", SqlDbType.VarChar, 6)
        {
            Value = maCsdt,
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new DataTable
        {
            CaseSensitive = true,
            Locale = System.Globalization.CultureInfo.InvariantCulture,
        };
        rows.Load(reader);
        return new CsdtRealtimeSnapshot(metadata, rows);
    }

    public async Task<IReadOnlyList<CsdtRealtimeChange>> ReadChangesAsync(
        string connectionString,
        CsdtRealtimeDomainDefinition domain,
        long fromVersion,
        long throughVersion,
        string maCsdt,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await ReadChangesAsync(
            connection,
            transaction: null,
            domain,
            fromVersion,
            throughVersion,
            maCsdt,
            cancellationToken);
    }

    internal async Task<IReadOnlyList<CsdtRealtimeChange>> ReadChangesAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CsdtRealtimeDomainDefinition domain,
        long fromVersion,
        long throughVersion,
        string maCsdt,
        CancellationToken cancellationToken)
    {
        ValidateCallerConnection(connection, transaction);
        if (fromVersion < 0 || throughVersion < fromVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(fromVersion));
        }

        var metadata = await ReadMetadataAsync(
            connection,
            domain,
            cancellationToken,
            transaction);
        ValidatePrimaryKey(metadata);
        CsdtRealtimeColumnOwnershipPolicy.GetRequired(domain.Name)
            .ValidateSourceSchema(metadata);

        var exactJoin = BuildExactKeyJoin("src", "ct", metadata.PrimaryKey);
        var selectKeys = string.Join(
            ", ",
            metadata.PrimaryKey.Select(key =>
                $"ct.{CsdtRealtimeColumnMetadata.Quote(key.Name)} AS {CsdtRealtimeColumnMetadata.Quote(key.Name)}"));
        var projectedKeys = string.Join(
            ", ",
            metadata.PrimaryKey.Select(key =>
                $"boundedResult.{CsdtRealtimeColumnMetadata.Quote(key.Name)}"));
        var sql = $"""
            WITH boundedChanges AS
            (
                SELECT *
                FROM CHANGETABLE(CHANGES {domain.QualifiedTableName}, @FromVersion) AS bounded
                WHERE SYS_CHANGE_VERSION <= @ThroughVersion
            )
            SELECT
                boundedResult.SYS_CHANGE_VERSION,
                boundedResult.SYS_CHANGE_OPERATION,
                {projectedKeys},
                boundedResult.CurrentRowIsInPartition
            FROM
            (
                SELECT
                    ct.SYS_CHANGE_VERSION,
                    ct.SYS_CHANGE_OPERATION,
                    {selectKeys},
                    CASE
                        WHEN src.{CsdtRealtimeColumnMetadata.Quote(metadata.PrimaryKey[0].Name)} IS NOT NULL
                         AND ({domain.PartitionPredicate})
                        THEN CONVERT(bit, 1)
                        ELSE CONVERT(bit, 0)
                    END AS CurrentRowIsInPartition
                FROM boundedChanges AS ct
                LEFT JOIN {domain.QualifiedTableName} AS src
                  ON {exactJoin}
                WHERE ct.SYS_CHANGE_OPERATION <> 'D'

                UNION ALL

                SELECT
                    ct.SYS_CHANGE_VERSION,
                    ct.SYS_CHANGE_OPERATION,
                    {selectKeys},
                    CONVERT(bit, 0) AS CurrentRowIsInPartition
                FROM boundedChanges AS ct
                WHERE ct.SYS_CHANGE_OPERATION = 'D'
            ) AS boundedResult
            ORDER BY boundedResult.SYS_CHANGE_VERSION;
            """;

        await using var command = new SqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 120,
        };
        command.Parameters.Add(new SqlParameter("@FromVersion", SqlDbType.BigInt)
        {
            Value = fromVersion,
        });
        command.Parameters.Add(new SqlParameter("@ThroughVersion", SqlDbType.BigInt)
        {
            Value = throughVersion,
        });
        command.Parameters.Add(new SqlParameter("@MaCSDT", SqlDbType.VarChar, 6)
        {
            Value = maCsdt,
        });

        var changes = new List<CsdtRealtimeChange>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var keyColumn in metadata.PrimaryKey)
            {
                var ordinal = reader.GetOrdinal(keyColumn.Name);
                key[keyColumn.Name] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
            }

            changes.Add(new CsdtRealtimeChange(
                reader.GetInt64(reader.GetOrdinal("SYS_CHANGE_VERSION")),
                reader.GetString(reader.GetOrdinal("SYS_CHANGE_OPERATION")),
                JsonSerializer.Serialize(key),
                reader.GetBoolean(reader.GetOrdinal("CurrentRowIsInPartition"))));
        }

        return changes;
    }

    public async Task<CsdtRealtimeSnapshot> ReadChangedPartitionSnapshotAsync(
        string connectionString,
        CsdtRealtimeDomainDefinition domain,
        long fromVersion,
        long throughVersion,
        string maCsdt,
        CancellationToken cancellationToken)
        => await ReadChangedPartitionSnapshotCoreAsync(
            connectionString,
            domain,
            fromVersion,
            throughVersion,
            maCsdt,
            forwardOwnershipScope: false,
            cancellationToken);

    public async Task<CsdtRealtimeSnapshot> ReadForwardChangedPartitionSnapshotAsync(
        string connectionString,
        CsdtRealtimeDomainDefinition domain,
        long fromVersion,
        long throughVersion,
        string maCsdt,
        CancellationToken cancellationToken)
        => await ReadChangedPartitionSnapshotCoreAsync(
            connectionString,
            domain,
            fromVersion,
            throughVersion,
            maCsdt,
            forwardOwnershipScope: true,
            cancellationToken);

    private static async Task<CsdtRealtimeSnapshot> ReadChangedPartitionSnapshotCoreAsync(
        string connectionString,
        CsdtRealtimeDomainDefinition domain,
        long fromVersion,
        long throughVersion,
        string maCsdt,
        bool forwardOwnershipScope,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await ReadChangedPartitionSnapshotCoreAsync(
            connection,
            transaction: null,
            domain,
            fromVersion,
            throughVersion,
            maCsdt,
            forwardOwnershipScope,
            cancellationToken);
    }

    internal Task<CsdtRealtimeSnapshot> ReadForwardChangedPartitionSnapshotAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtRealtimeDomainDefinition domain,
        long fromVersion,
        long throughVersion,
        string maCsdt,
        CancellationToken cancellationToken)
        => ReadChangedPartitionSnapshotCoreAsync(
            connection,
            transaction,
            domain,
            fromVersion,
            throughVersion,
            maCsdt,
            forwardOwnershipScope: true,
            cancellationToken);

    private static async Task<CsdtRealtimeSnapshot> ReadChangedPartitionSnapshotCoreAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CsdtRealtimeDomainDefinition domain,
        long fromVersion,
        long throughVersion,
        string maCsdt,
        bool forwardOwnershipScope,
        CancellationToken cancellationToken)
    {
        ValidateCallerConnection(connection, transaction);
        var metadata = await ReadMetadataAsync(
            connection,
            domain,
            cancellationToken,
            transaction);
        ValidatePrimaryKey(metadata);
        var selectedColumns = forwardOwnershipScope
            ? CsdtRealtimeColumnOwnershipPolicy.GetRequired(domain.Name)
                .SelectForwardReadColumns(metadata)
            : metadata.WritableColumns;

        var exactJoin = BuildExactKeyJoin("src", "ct", metadata.PrimaryKey);
        var columnList = string.Join(
            ", ",
            selectedColumns.Select(column =>
                $"src.{CsdtRealtimeColumnMetadata.Quote(column.Name)}"));
        var orderBy = string.Join(
            ", ",
            metadata.PrimaryKey.Select(key =>
                $"src.{CsdtRealtimeColumnMetadata.Quote(key.Name)}"));
        var sql = $"""
            SELECT {columnList}
            FROM CHANGETABLE(CHANGES {domain.QualifiedTableName}, @FromVersion) AS ct
            INNER JOIN {domain.QualifiedTableName} AS src
              ON {exactJoin}
            WHERE ct.SYS_CHANGE_VERSION <= @ThroughVersion
              AND ({domain.PartitionPredicate})
            ORDER BY {orderBy};
            """;

        await using var command = new SqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 120,
        };
        command.Parameters.Add(new SqlParameter("@FromVersion", SqlDbType.BigInt)
        {
            Value = fromVersion,
        });
        command.Parameters.Add(new SqlParameter("@ThroughVersion", SqlDbType.BigInt)
        {
            Value = throughVersion,
        });
        command.Parameters.Add(new SqlParameter("@MaCSDT", SqlDbType.VarChar, 6)
        {
            Value = maCsdt,
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new DataTable
        {
            CaseSensitive = true,
            Locale = System.Globalization.CultureInfo.InvariantCulture,
        };
        rows.Load(reader);
        return new CsdtRealtimeSnapshot(metadata, rows);
    }

    internal static async Task<CsdtRealtimeTableMetadata> ReadMetadataAsync(
        SqlConnection connection,
        CsdtRealtimeDomainDefinition domain,
        CancellationToken cancellationToken,
        SqlTransaction? transaction = null)
    {
        const string sql = """
            SELECT
                c.[name] AS [Name],
                ty.[name] AS [SqlType],
                c.[max_length] AS [MaxLength],
                c.[precision] AS [Precision],
                c.[scale] AS [Scale],
                c.[is_nullable] AS [IsNullable],
                c.[is_identity] AS [IsIdentity],
                c.[is_computed] AS [IsComputed],
                CONVERT(bit, CASE WHEN c.[default_object_id] = 0 THEN 0 ELSE 1 END) AS [HasDefault],
                c.[column_id] AS [ColumnId],
                pk.[key_ordinal] AS [PrimaryKeyOrdinal]
            FROM sys.tables AS t
            INNER JOIN sys.schemas AS s ON s.[schema_id] = t.[schema_id]
            INNER JOIN sys.columns AS c ON c.[object_id] = t.[object_id]
            INNER JOIN sys.types AS ty ON ty.[user_type_id] = c.[user_type_id]
            LEFT JOIN
            (
                SELECT ic.[object_id], ic.[column_id], ic.[key_ordinal]
                FROM sys.indexes AS i
                INNER JOIN sys.index_columns AS ic
                    ON ic.[object_id] = i.[object_id]
                   AND ic.[index_id] = i.[index_id]
                WHERE i.[is_primary_key] = 1
            ) AS pk
                ON pk.[object_id] = c.[object_id]
               AND pk.[column_id] = c.[column_id]
            WHERE s.[name] = N'dbo'
              AND t.[name] = @TableName
            ORDER BY c.[column_id];
            """;
        var rows = (await connection.QueryAsync<CsdtRealtimeColumnMetadata>(new CommandDefinition(
            sql,
            new { domain.TableName },
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken))).AsList();
        if (rows.Count == 0)
        {
            throw new CsdtRealtimeSchemaException($"Required table dbo.{domain.TableName} is missing.");
        }

        return new CsdtRealtimeTableMetadata(domain, rows);
    }

    internal static void ValidatePrimaryKey(CsdtRealtimeTableMetadata metadata)
    {
        var actual = metadata.PrimaryKey.Select(item => item.Name).ToArray();
        if (!actual.SequenceEqual(metadata.Domain.KeyColumns, StringComparer.Ordinal))
        {
            throw new CsdtRealtimeSchemaException(
                $"dbo.{metadata.Domain.TableName} primary key does not match the fixed mapping.");
        }
    }

    internal static string BuildExactKeyJoin(
        string leftAlias,
        string rightAlias,
        IReadOnlyList<CsdtRealtimeColumnMetadata> keyColumns)
        => string.Join(
            " AND ",
            keyColumns.Select(key =>
            {
                var name = CsdtRealtimeColumnMetadata.Quote(key.Name);
                return key.IsText
                    ? $"CONVERT(varbinary(max), {leftAlias}.{name}) = CONVERT(varbinary(max), {rightAlias}.{name})"
                    : $"{leftAlias}.{name} = {rightAlias}.{name}";
            }));

    private static void ValidateCallerConnection(
        SqlConnection connection,
        SqlTransaction? transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != ConnectionState.Open ||
            transaction is not null &&
            (transaction.Connection is null ||
             !ReferenceEquals(transaction.Connection, connection)))
        {
            throw new ArgumentException(
                "An open connection and its caller-owned transaction are required.");
        }
    }
}

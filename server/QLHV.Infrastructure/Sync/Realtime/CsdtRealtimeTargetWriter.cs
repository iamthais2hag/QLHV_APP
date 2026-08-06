using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync.Realtime;

namespace QLHV.Infrastructure.Sync.Realtime;

internal sealed class CsdtRealtimeTargetWriter
{
    private const string StageTable = "#CsdtRealtimeStage";

    public async Task<CsdtRealtimeWriteResult> UpsertAsync(
        string targetConnectionString,
        CsdtRealtimeSnapshot snapshot,
        string maCsdt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var policy = CsdtRealtimeColumnOwnershipPolicy.GetRequired(
            snapshot.SourceMetadata.Domain.Name);
        var stageColumns = policy.SelectForwardReadColumns(snapshot.SourceMetadata);
        EnsureSnapshotColumns(snapshot.Rows, stageColumns);
        ApplyRequiredBusinessMappings(snapshot, maCsdt);
        await using var connection = new SqlConnection(targetConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var targetMetadata = await CsdtRealtimeSourceReader.ReadMetadataAsync(
                connection,
                snapshot.SourceMetadata.Domain,
                cancellationToken,
                transaction);
            CsdtRealtimeSourceReader.ValidatePrimaryKey(targetMetadata);
            var insertColumns = policy.SelectInsertColumns(snapshot.SourceMetadata);
            var updateColumns = policy.SelectUpdateColumns(snapshot.SourceMetadata);
            ValidateForwardSchemaAndRows(
                snapshot,
                targetMetadata,
                stageColumns,
                insertColumns,
                maCsdt);

            await CreateStageAsync(connection, transaction, stageColumns, cancellationToken);
            await BulkCopyAsync(
                connection,
                transaction,
                snapshot.Rows,
                stageColumns,
                cancellationToken);
            await ValidateTargetLookupsAsync(
                connection,
                transaction,
                snapshot.SourceMetadata.Domain,
                cancellationToken);
            await ThrowOnCollationCollisionAsync(
                connection,
                transaction,
                targetMetadata,
                cancellationToken);

            var lockedTargets = await ReadLockedTargetRowsAsync(
                connection,
                transaction,
                targetMetadata,
                cancellationToken);
            var planningContext = await ReadForwardPlanningContextAsync(
                connection,
                transaction,
                targetMetadata,
                cancellationToken);
            var plan = CsdtRealtimeForwardWritePlanner.Plan(
                snapshot,
                targetMetadata,
                lockedTargets,
                planningContext);
            await ReplaceStageRowsAsync(
                connection,
                transaction,
                plan.Rows,
                stageColumns,
                cancellationToken);
            var inserted = await InsertMissingAsync(
                connection,
                transaction,
                targetMetadata,
                insertColumns,
                cancellationToken);
            var updated = await UpdateChangedAsync(
                connection,
                transaction,
                targetMetadata,
                updateColumns,
                cancellationToken);

            await VerifyForwardIdentityReadbackAsync(
                connection,
                transaction,
                targetMetadata,
                cancellationToken);
            var sourceRows = snapshot.Rows.Rows.Count;
            var targetRows = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                $"""
                 SELECT COUNT_BIG(1)
                 FROM {targetMetadata.Domain.QualifiedTableName} AS src
                 WHERE ({targetMetadata.Domain.PartitionPredicate});
                 """,
                new { MaCSDT = maCsdt },
                transaction,
                commandTimeout: 120,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            var conflictKeys = plan.Conflicts
                .Select(item => item.KeyJson)
                .ToHashSet(StringComparer.Ordinal);
            var entities = BuildEntitySnapshots(
                    plan.Rows,
                    snapshot.SourceMetadata,
                    policy.SelectHashColumns(snapshot.SourceMetadata))
                .Where(item => !conflictKeys.Contains(item.KeyJson))
                .ToArray();
            return new CsdtRealtimeWriteResult(
                sourceRows,
                targetRows,
                inserted,
                updated,
                Math.Max(0, sourceRows - inserted - updated),
                entities,
                plan.Conflicts);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the original domain failure. The unadvanced checkpoint
                // guarantees a safe replay after the connection is recovered.
            }

            throw;
        }
    }

    /// <summary>
    /// Atomic-cycle overload. The caller owns the connection, transaction,
    /// commit, and rollback for all six core domains.
    /// </summary>
    internal async Task<CsdtRealtimeWriteResult> UpsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtRealtimeSnapshot snapshot,
        string maCsdt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (connection.State != ConnectionState.Open ||
            transaction.Connection is null ||
            !ReferenceEquals(transaction.Connection, connection))
        {
            throw new ArgumentException(
                "The atomic writer requires an open caller-owned target transaction.");
        }

        var policy = CsdtRealtimeColumnOwnershipPolicy.GetRequired(
            snapshot.SourceMetadata.Domain.Name);
        var stageColumns = policy.SelectForwardReadColumns(snapshot.SourceMetadata);
        EnsureSnapshotColumns(snapshot.Rows, stageColumns);
        ApplyRequiredBusinessMappings(snapshot, maCsdt);
        var targetMetadata = await CsdtRealtimeSourceReader.ReadMetadataAsync(
            connection,
            snapshot.SourceMetadata.Domain,
            cancellationToken,
            transaction);
        CsdtRealtimeSourceReader.ValidatePrimaryKey(targetMetadata);
        var insertColumns = policy.SelectInsertColumns(snapshot.SourceMetadata);
        var updateColumns = policy.SelectUpdateColumns(snapshot.SourceMetadata);
        ValidateForwardSchemaAndRows(
            snapshot,
            targetMetadata,
            stageColumns,
            insertColumns,
            maCsdt);

        await CreateStageAsync(connection, transaction, stageColumns, cancellationToken);
        await BulkCopyAsync(
            connection,
            transaction,
            snapshot.Rows,
            stageColumns,
            cancellationToken);
        await ValidateTargetLookupsAsync(
            connection,
            transaction,
            snapshot.SourceMetadata.Domain,
            cancellationToken);
        await ThrowOnCollationCollisionAsync(
            connection,
            transaction,
            targetMetadata,
            cancellationToken);

        var lockedTargets = await ReadLockedTargetRowsAsync(
            connection,
            transaction,
            targetMetadata,
            cancellationToken);
        var planningContext = await ReadForwardPlanningContextAsync(
            connection,
            transaction,
            targetMetadata,
            cancellationToken,
            useAtomicMappedTableContract: true);
        var plan = CsdtRealtimeForwardWritePlanner.Plan(
            snapshot,
            targetMetadata,
            lockedTargets,
            planningContext,
            useAtomicMappedTableContract: true);
        await ReplaceStageRowsAsync(
            connection,
            transaction,
            plan.Rows,
            stageColumns,
            cancellationToken);
        var inserted = await InsertMissingAsync(
            connection,
            transaction,
            targetMetadata,
            insertColumns,
            cancellationToken);
        var updated = await UpdateChangedAsync(
            connection,
            transaction,
            targetMetadata,
            updateColumns,
            cancellationToken);
        await VerifyForwardIdentityReadbackAsync(
            connection,
            transaction,
            targetMetadata,
            cancellationToken);

        var sourceRows = snapshot.Rows.Rows.Count;
        var targetRows = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            $"""
             SELECT COUNT_BIG(1)
             FROM {targetMetadata.Domain.QualifiedTableName} AS src
             WHERE ({targetMetadata.Domain.PartitionPredicate});
             """,
            new { MaCSDT = maCsdt },
            transaction,
            commandTimeout: 120,
            cancellationToken: cancellationToken));
        var conflictKeys = plan.Conflicts
            .Select(item => item.KeyJson)
            .ToHashSet(StringComparer.Ordinal);
        var entities = BuildEntitySnapshots(
                plan.Rows,
                snapshot.SourceMetadata,
                policy.SelectHashColumns(snapshot.SourceMetadata))
            .Where(item => !conflictKeys.Contains(item.KeyJson))
            .ToArray();
        return new CsdtRealtimeWriteResult(
            sourceRows,
            targetRows,
            inserted,
            updated,
            Math.Max(0, sourceRows - inserted - updated),
            entities,
            plan.Conflicts);
    }

    /// <summary>
    /// Applies a previously planned reverse-sync set. This path deliberately
    /// has no INSERT or DELETE operation: every staged key must still exist and
    /// every V2 row must still match the hash observed by the plan.
    /// </summary>
    public async Task<CsdtReverseAtomicWriteResult> UpdateExistingAtomicallyAsync(
        string targetConnectionString,
        IReadOnlyList<CsdtReverseDomainWrite> writes,
        string maCsdt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writes);
        var remaining = writes.ToList();
        var optionalSkips = new Dictionary<string, CsdtReverseAtomicWriteDomainResult>(
            StringComparer.Ordinal);

        while (true)
        {
            await using var connection = new SqlConnection(targetConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var attempted = new List<string>();
            var committed = new Dictionary<string, CsdtReverseAtomicWriteDomainResult>(
                StringComparer.Ordinal);
            CsdtReverseDomainWrite? current = null;
            try
            {
                foreach (var write in remaining)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    current = write;
                    if (write.Snapshot.Rows.Rows.Count == 0)
                    {
                        committed.Add(
                            write.Domain.Name,
                            new CsdtReverseAtomicWriteDomainResult(
                                write.Domain.Name,
                                "SUCCEEDED",
                                write.SourceRows,
                                0,
                                write.SourceRows));
                        continue;
                    }

                    attempted.Add(write.Domain.Name);
                    var updated = await UpdateExistingInTransactionAsync(
                        connection,
                        transaction,
                        write.Snapshot,
                        maCsdt,
                        write.ExpectedTargetHashes,
                        cancellationToken);
                    committed.Add(
                        write.Domain.Name,
                        new CsdtReverseAtomicWriteDomainResult(
                            write.Domain.Name,
                            "SUCCEEDED",
                            write.SourceRows,
                            updated,
                            Math.Max(0, write.SourceRows - updated)));
                }

                await transaction.CommitAsync(cancellationToken);
                return new CsdtReverseAtomicWriteResult(
                    writes.Select(write =>
                            optionalSkips.GetValueOrDefault(write.Domain.Name) ??
                            committed[write.Domain.Name])
                        .ToArray());
            }
            catch (CsdtRealtimeSchemaException exception) when (
                current?.Domain.IsOptional == true &&
                exception is not CsdtRealtimeTargetConflictException)
            {
                await SafeRollbackAsync(transaction);
                optionalSkips[current.Domain.Name] = new CsdtReverseAtomicWriteDomainResult(
                    current.Domain.Name,
                    "SKIPPED",
                    current.SourceRows,
                    0,
                    current.SourceRows,
                    "SKIPPED_UNSUPPORTED_SCHEMA",
                    exception.Message);
                remaining.RemoveAll(write =>
                    string.Equals(
                        write.Domain.Name,
                        current.Domain.Name,
                        StringComparison.Ordinal));
            }
            catch (OperationCanceledException)
            {
                await SafeRollbackAsync(transaction);
                throw;
            }
            catch (Exception exception)
            {
                await SafeRollbackAsync(transaction);
                throw new CsdtReverseAtomicWriteException(
                    current?.Domain.Name ?? "<target-transaction>",
                    attempted,
                    optionalSkips.Values.ToArray(),
                    exception);
            }
        }
    }

    public async Task<long> UpdateExistingAsync(
        string targetConnectionString,
        CsdtRealtimeSnapshot snapshot,
        string maCsdt,
        IReadOnlyDictionary<string, byte[]> expectedTargetHashes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(expectedTargetHashes);
        if (snapshot.Rows.Rows.Count == 0)
        {
            return 0;
        }

        await using var connection = new SqlConnection(targetConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var updated = await UpdateExistingInTransactionAsync(
                connection,
                transaction,
                snapshot,
                maCsdt,
                expectedTargetHashes,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return updated;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the stale-plan/schema error that caused the rollback.
            }

            throw;
        }
    }

    private static async Task<long> UpdateExistingInTransactionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtRealtimeSnapshot snapshot,
        string maCsdt,
        IReadOnlyDictionary<string, byte[]> expectedTargetHashes,
        CancellationToken cancellationToken)
    {
        var targetMetadata = await CsdtRealtimeSourceReader.ReadMetadataAsync(
            connection,
            snapshot.SourceMetadata.Domain,
            cancellationToken,
            transaction);
        CsdtRealtimeSourceReader.ValidatePrimaryKey(targetMetadata);
        var columns = ValidateSchemaAndRows(
            snapshot,
            targetMetadata,
            maCsdt,
            requireInsertCompatibility: false);

        await CreateStageAsync(connection, transaction, columns, cancellationToken);
        await BulkCopyAsync(connection, transaction, snapshot.Rows, columns, cancellationToken);
        await ValidateTargetLookupsAsync(
            connection,
            transaction,
            snapshot.SourceMetadata.Domain,
            cancellationToken);
        await ThrowOnCollationCollisionAsync(
            connection,
            transaction,
            targetMetadata,
            cancellationToken);

        var lockedTargets = await ReadLockedTargetRowsAsync(
            connection,
            transaction,
            targetMetadata,
            cancellationToken);
        ThrowIfTargetChangedOrMissing(
            lockedTargets,
            targetMetadata,
            expectedTargetHashes,
            snapshot.Rows.Rows.Count);

        var updated = await UpdateChangedAsync(
            connection,
            transaction,
            targetMetadata,
            columns,
            cancellationToken);
        await VerifyCriticalIdentityReadbackAsync(
            connection,
            transaction,
            targetMetadata,
            cancellationToken);
        return updated;
    }

    private static async Task SafeRollbackAsync(SqlTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve the domain failure that caused the atomic rollback.
        }
    }

    private static void ApplyRequiredBusinessMappings(
        CsdtRealtimeSnapshot snapshot,
        string maCsdt)
    {
        if (snapshot.SourceMetadata.Domain.Name != "DM_DonViGTVT" ||
            (maCsdt != "66029" && maCsdt != "66030") ||
            !snapshot.Rows.Columns.Contains("CoQuanQL"))
        {
            return;
        }

        // The two transferred centres are directly managed by the provincial
        // construction department. The live legacy row still contains the old
        // company name; never reintroduce that stale authority into V1.
        const string DirectAuthority = "Sở Xây dựng tỉnh Đắk Lắk";
        foreach (DataRow row in snapshot.Rows.Rows)
        {
            row["CoQuanQL"] = DirectAuthority;
        }
    }

    private static void ValidateForwardSchemaAndRows(
        CsdtRealtimeSnapshot snapshot,
        CsdtRealtimeTableMetadata target,
        IReadOnlyList<CsdtRealtimeColumnMetadata> stageColumns,
        IReadOnlyList<CsdtRealtimeColumnMetadata> insertColumns,
        string maCsdt)
    {
        var targetByName = target.Columns.ToDictionary(item => item.Name, StringComparer.Ordinal);
        foreach (var source in stageColumns)
        {
            if (!targetByName.TryGetValue(source.Name, out var targetColumn))
            {
                throw new CsdtRealtimeSchemaException(
                    $"FORWARD_TARGET_COLUMN_MISSING: dbo.{target.Domain.TableName}.{source.Name} " +
                    "is required by the explicit ownership policy but is missing from V1.");
            }

            var specialTeacherClass =
                target.Domain.Name == "GiaoVien" &&
                source.Name == "HangGPLX";
            if (!specialTeacherClass &&
                !string.Equals(source.SqlType, targetColumn.SqlType, StringComparison.Ordinal))
            {
                throw new CsdtRealtimeSchemaException(
                    $"dbo.{target.Domain.TableName}.{source.Name} has an incompatible SQL type.");
            }

            if (source.IsIdentity != targetColumn.IsIdentity)
            {
                throw new CsdtRealtimeSchemaException(
                    $"dbo.{target.Domain.TableName}.{source.Name} identity metadata differs.");
            }
        }

        if (insertColumns.Count != 0)
        {
            var insertedNames = insertColumns
                .Select(item => item.Name)
                .ToHashSet(StringComparer.Ordinal);
            var requiredTargetOnly = target.Columns.Where(column =>
                !insertedNames.Contains(column.Name) &&
                !column.IsNullable &&
                !column.IsIdentity &&
                !column.IsComputed &&
                !column.HasDefault).ToArray();
            if (requiredTargetOnly.Length > 0)
            {
                throw new CsdtRealtimeSchemaException(
                    $"FORWARD_INSERT_POLICY_INCOMPATIBLE: dbo.{target.Domain.TableName} has required " +
                    "target columns outside the explicit insert policy: " +
                    string.Join(", ", requiredTargetOnly.Select(item => item.Name)));
            }
        }

        ValidateIdentityValues(snapshot, maCsdt);
        ValidateValuesFitTarget(snapshot.Rows, stageColumns, targetByName, target.Domain.TableName);
    }

    private static void EnsureSnapshotColumns(
        DataTable rows,
        IReadOnlyList<CsdtRealtimeColumnMetadata> expected)
    {
        var actual = rows.Columns
            .Cast<DataColumn>()
            .Select(column => column.ColumnName)
            .ToHashSet(StringComparer.Ordinal);
        var missing = expected
            .Where(column => !actual.Contains(column.Name))
            .Select(column => column.Name)
            .ToArray();
        if (missing.Length != 0)
        {
            throw new CsdtRealtimeSchemaException(
                "Forward snapshot is missing policy-selected columns: " +
                string.Join(", ", missing));
        }
    }

    private static void ValidateValuesFitTarget(
        DataTable rows,
        IReadOnlyList<CsdtRealtimeColumnMetadata> sourceColumns,
        IReadOnlyDictionary<string, CsdtRealtimeColumnMetadata> targetByName,
        string tableName)
    {
        foreach (DataRow row in rows.Rows)
        {
            foreach (var source in sourceColumns)
            {
                var value = row[source.Name];
                var targetColumn = targetByName[source.Name];
                if (value is DBNull)
                {
                    if (!targetColumn.IsNullable)
                    {
                        throw new CsdtRealtimeSchemaException(
                            $"dbo.{tableName}.{source.Name} cannot store NULL.");
                    }

                    continue;
                }

                if (value is not string text)
                {
                    continue;
                }

                var maximum = targetColumn.MaximumCharacters;
                if (maximum.HasValue && text.Length > maximum.Value)
                {
                    throw new CsdtRealtimeSchemaException(
                        $"dbo.{tableName}.{source.Name} source length {text.Length} " +
                        $"exceeds target length {maximum.Value}; value was not truncated.");
                }

                if (!targetColumn.IsUnicode &&
                    source.IsUnicode &&
                    text.Any(character => character > 0x7f))
                {
                    throw new CsdtRealtimeSchemaException(
                        $"dbo.{tableName}.{source.Name} contains Unicode that V1 cannot store losslessly.");
                }
            }
        }
    }

    private static IReadOnlyList<CsdtRealtimeColumnMetadata> ValidateSchemaAndRows(
        CsdtRealtimeSnapshot snapshot,
        CsdtRealtimeTableMetadata target,
        string maCsdt,
        bool requireInsertCompatibility = true)
    {
        var sourceColumns = snapshot.SourceMetadata.WritableColumns;
        var targetByName = target.Columns.ToDictionary(item => item.Name, StringComparer.Ordinal);
        foreach (var source in sourceColumns)
        {
            if (!targetByName.TryGetValue(source.Name, out var targetColumn))
            {
                throw new CsdtRealtimeSchemaException(
                    $"dbo.{target.Domain.TableName}.{source.Name} is missing from V1.");
            }

            var specialTeacherClass =
                target.Domain.Name == "GiaoVien" &&
                source.Name == "HangGPLX";
            if (!specialTeacherClass &&
                !string.Equals(source.SqlType, targetColumn.SqlType, StringComparison.Ordinal))
            {
                throw new CsdtRealtimeSchemaException(
                    $"dbo.{target.Domain.TableName}.{source.Name} has an incompatible SQL type.");
            }

            if (source.IsIdentity != targetColumn.IsIdentity)
            {
                throw new CsdtRealtimeSchemaException(
                    $"dbo.{target.Domain.TableName}.{source.Name} identity metadata differs.");
            }
        }

        var sourceNames = sourceColumns.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var requiredTargetOnly = target.Columns.Where(column =>
            !sourceNames.Contains(column.Name) &&
            !column.IsNullable &&
            !column.IsIdentity &&
            !column.IsComputed &&
            !column.HasDefault).ToArray();
        if (requireInsertCompatibility && requiredTargetOnly.Length > 0)
        {
            throw new CsdtRealtimeSchemaException(
                $"dbo.{target.Domain.TableName} has required V1-only columns: " +
                string.Join(", ", requiredTargetOnly.Select(item => item.Name)));
        }

        ValidateIdentityValues(snapshot, maCsdt);
        foreach (DataRow row in snapshot.Rows.Rows)
        {
            foreach (var source in sourceColumns)
            {
                var value = row[source.Name];
                var targetColumn = targetByName[source.Name];
                if (value is DBNull)
                {
                    if (!targetColumn.IsNullable)
                    {
                        throw new CsdtRealtimeSchemaException(
                            $"dbo.{target.Domain.TableName}.{source.Name} cannot store NULL.");
                    }

                    continue;
                }

                if (value is string text)
                {
                    var maximum = targetColumn.MaximumCharacters;
                    if (maximum.HasValue && text.Length > maximum.Value)
                    {
                        throw new CsdtRealtimeSchemaException(
                            $"dbo.{target.Domain.TableName}.{source.Name} source length {text.Length} " +
                            $"exceeds target length {maximum.Value}; value was not truncated.");
                    }

                    if (!targetColumn.IsUnicode &&
                        source.IsUnicode &&
                        text.Any(character => character > 0x7f))
                    {
                        throw new CsdtRealtimeSchemaException(
                            $"dbo.{target.Domain.TableName}.{source.Name} contains Unicode that V1 cannot store losslessly.");
                    }
                }
            }
        }

        return sourceColumns;
    }

    private static void ValidateIdentityValues(CsdtRealtimeSnapshot snapshot, string maCsdt)
    {
        foreach (DataRow row in snapshot.Rows.Rows)
        {
            string? Read(string column) =>
                snapshot.Rows.Columns.Contains(column) && row[column] is not DBNull
                    ? Convert.ToString(row[column], CultureInfo.InvariantCulture)
                    : null;

            switch (snapshot.SourceMetadata.Domain.Name)
            {
                case "DM_DonViGTVT":
                    RequireOrdinal(Read("MaDV"), maCsdt, "MaDV");
                    break;
                case "KhoaHoc":
                    RequireOrdinal(Read("MaCSDT"), maCsdt, "MaCSDT");
                    if (!CsdtRealtimeIdentityRules.IsRawCourseCodeOrStorableLegacy(Read("MaKH"), maCsdt))
                    {
                        throw new CsdtRealtimeSchemaException("KhoaHoc.MaKH is not a valid raw current/legacy identity.");
                    }
                    break;
                case "BaoCaoI":
                case "GiaoVien":
                    RequireOrdinal(Read("MaCSDT"), maCsdt, "MaCSDT");
                    break;
                case "NguoiLX":
                    if (!CsdtRealtimeIdentityRules.IsRawStudentCodeOrStorableLegacy(Read("MaDK"), maCsdt))
                    {
                        throw new CsdtRealtimeSchemaException("NguoiLX.MaDK is not a valid raw current/legacy identity.");
                    }
                    break;
                case "NguoiLX_HoSo":
                {
                    var maDk = Read("MaDK");
                    if (!CsdtRealtimeIdentityRules.IsRawStudentCodeOrStorableLegacy(maDk, maCsdt))
                    {
                        throw new CsdtRealtimeSchemaException("NguoiLX_HoSo.MaDK is not a valid raw identity.");
                    }

                    RequireOrdinal(Read("MaCSDT"), maCsdt, "MaCSDT");
                    var certificate = Read("SoGiayCNTN");
                    if (certificate is not null &&
                        !CsdtRealtimeIdentityRules.IsExactCompletionCertificate(
                            certificate,
                            maDk,
                            Read("HangDaoTao")))
                    {
                        throw new CsdtRealtimeSchemaException(
                            "NguoiLX_HoSo.SoGiayCNTN is not an exact MaDK-HangDaoTao value.");
                    }
                    break;
                }
                case "NguoiLX_GPLX":
                case "NguoiLXHS_GiayTo":
                    if (!CsdtRealtimeIdentityRules.IsRawStudentCodeOrStorableLegacy(Read("MaDK"), maCsdt))
                    {
                        throw new CsdtRealtimeSchemaException(
                            $"{snapshot.SourceMetadata.Domain.Name}.MaDK is not a valid raw identity.");
                    }
                    break;
            }
        }
    }

    private static void RequireOrdinal(string? actual, string expected, string column)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new CsdtRealtimeSchemaException($"{column} does not exactly match the fixed center.");
        }
    }

    private static async Task CreateStageAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyList<CsdtRealtimeColumnMetadata> columns,
        CancellationToken cancellationToken)
    {
        var declarations = string.Join(",\n", columns.Select(column => column.ToSqlDeclaration()));
        var sql = $"DROP TABLE IF EXISTS {StageTable}; CREATE TABLE {StageTable} ({declarations});";
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            transaction: transaction,
            commandTimeout: 120,
            cancellationToken: cancellationToken));
    }

    private static async Task BulkCopyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DataTable rows,
        IReadOnlyList<CsdtRealtimeColumnMetadata> columns,
        CancellationToken cancellationToken)
    {
        using var bulk = new SqlBulkCopy(
            connection,
            SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.CheckConstraints,
            transaction)
        {
            DestinationTableName = StageTable,
            BatchSize = 500,
            BulkCopyTimeout = 120,
        };
        foreach (var column in columns)
        {
            bulk.ColumnMappings.Add(column.Name, column.Name);
        }

        await bulk.WriteToServerAsync(rows, cancellationToken);
    }

    private static async Task ReplaceStageRowsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DataTable rows,
        IReadOnlyList<CsdtRealtimeColumnMetadata> columns,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {StageTable};",
            transaction: transaction,
            commandTimeout: 120,
            cancellationToken: cancellationToken));
        if (rows.Rows.Count != 0)
        {
            await BulkCopyAsync(connection, transaction, rows, columns, cancellationToken);
        }
    }

    private static async Task ValidateTargetLookupsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtRealtimeDomainDefinition domain,
        CancellationToken cancellationToken)
    {
        if (domain.Name != "GiaoVien")
        {
            return;
        }

        const string sql = """
            SELECT COUNT_BIG(1)
            FROM #CsdtRealtimeStage AS src
            LEFT JOIN dbo.DM_HangGPLX AS hang
              ON CONVERT(varbinary(8000), hang.MaHang) =
                 CONVERT(varbinary(8000), src.HangGPLX)
            WHERE src.HangGPLX IS NOT NULL
              AND
              (
                  DATALENGTH(src.HangGPLX) > 3
                  OR src.HangGPLX LIKE '%[^ -~]%' COLLATE Latin1_General_100_BIN2
                  OR hang.MaHang IS NULL
              );
            """;
        var invalid = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            sql,
            transaction: transaction,
            commandTimeout: 120,
            cancellationToken: cancellationToken));
        if (invalid > 0)
        {
            throw new CsdtRealtimeSchemaException(
                "GiaoVien.HangGPLX cannot be copied losslessly into the guarded V1 lookup.");
        }
    }

    private static async Task ThrowOnCollationCollisionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtRealtimeTableMetadata target,
        CancellationToken cancellationToken)
    {
        var ordinary = string.Join(
            " AND ",
            target.PrimaryKey.Select(key =>
                $"target.{CsdtRealtimeColumnMetadata.Quote(key.Name)} = stage.{CsdtRealtimeColumnMetadata.Quote(key.Name)}"));
        var exact = CsdtRealtimeSourceReader.BuildExactKeyJoin("target", "stage", target.PrimaryKey);
        var collisions = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            $"""
             SELECT COUNT_BIG(1)
             FROM {target.Domain.QualifiedTableName} AS target
             INNER JOIN {StageTable} AS stage ON {ordinary}
             WHERE NOT ({exact});
             """,
            transaction: transaction,
            commandTimeout: 120,
            cancellationToken: cancellationToken));
        if (collisions > 0)
        {
            throw new CsdtRealtimeTargetConflictException(
                $"dbo.{target.Domain.TableName} has a case/trailing-space identity collision.");
        }
    }

    private static async Task<IReadOnlyList<CsdtRealtimeConflictRecord>> RemoveGuardedIdentityCollisionsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtRealtimeTableMetadata target,
        CancellationToken cancellationToken)
    {
        var guardColumns = target.Domain.IdentityCollisionGuardColumns;
        if (guardColumns is null || guardColumns.Count == 0)
        {
            return [];
        }

        var exactKey = CsdtRealtimeSourceReader.BuildExactKeyJoin("target", "stage", target.PrimaryKey);
        var targetColumns = target.Columns.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var mismatch = string.Join(
            " OR ",
            guardColumns.Select(name =>
            {
                var column = targetColumns[name];
                var quoted = CsdtRealtimeColumnMetadata.Quote(name);
                return column.IsText
                    ? $"CONVERT(varbinary(max), target.{quoted}) <> CONVERT(varbinary(max), stage.{quoted})"
                    : $"target.{quoted} <> stage.{quoted}";
            }));
        var keyProjection = string.Join(
            ", ",
            target.PrimaryKey.Select(column => $"stage.{CsdtRealtimeColumnMetadata.Quote(column.Name)}"));
        var values = (await connection.QueryAsync(new CommandDefinition(
            $"""
             SELECT {keyProjection}
             FROM {target.Domain.QualifiedTableName} AS target
             INNER JOIN {StageTable} AS stage ON {exactKey}
             WHERE ({mismatch});
             """,
            transaction: transaction,
            commandTimeout: 120,
            cancellationToken: cancellationToken))).AsList();
        if (values.Count == 0)
        {
            return [];
        }

        await connection.ExecuteAsync(new CommandDefinition(
            $"""
             DELETE stage
             FROM {StageTable} AS stage
             INNER JOIN {target.Domain.QualifiedTableName} AS target ON {exactKey}
             WHERE ({mismatch});
             """,
            transaction: transaction,
            commandTimeout: 120,
            cancellationToken: cancellationToken));

        return values.Select(row =>
        {
            var dictionary = (IDictionary<string, object?>)row;
            return new CsdtRealtimeConflictRecord(
                JsonSerializer.Serialize(
                    dictionary.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
                "IDENTITY_COLLISION",
                "Target identity exists with different immutable relation fields.");
        }).ToArray();
    }

    private static async Task<long> InsertMissingAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtRealtimeTableMetadata target,
        IReadOnlyList<CsdtRealtimeColumnMetadata> columns,
        CancellationToken cancellationToken)
    {
        if (columns.Count == 0)
        {
            return 0;
        }

        var sql = BuildInsertCommandText(target, columns);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            sql,
            transaction: transaction,
            commandTimeout: 120,
            cancellationToken: cancellationToken));
    }

    internal static string BuildInsertCommandText(
        CsdtRealtimeTableMetadata target,
        IReadOnlyList<CsdtRealtimeColumnMetadata> columns)
    {
        var exactKey = CsdtRealtimeSourceReader.BuildExactKeyJoin("target", "stage", target.PrimaryKey);
        var columnList = string.Join(", ", columns.Select(column =>
            CsdtRealtimeColumnMetadata.Quote(column.Name)));
        var stageList = string.Join(", ", columns.Select(column =>
            $"stage.{CsdtRealtimeColumnMetadata.Quote(column.Name)}"));
        var identityColumn = columns.SingleOrDefault(column => column.IsIdentity);
        var identityOn = identityColumn is null
            ? string.Empty
            : $"SET IDENTITY_INSERT {target.Domain.QualifiedTableName} ON;";
        var identityOff = identityColumn is null
            ? string.Empty
            : $"SET IDENTITY_INSERT {target.Domain.QualifiedTableName} OFF;";
        return $"""
            {identityOn}
            BEGIN TRY
                INSERT INTO {target.Domain.QualifiedTableName} ({columnList})
                SELECT {stageList}
                FROM {StageTable} AS stage
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM {target.Domain.QualifiedTableName} AS target WITH (UPDLOCK, HOLDLOCK)
                    WHERE {exactKey}
                );
                DECLARE @InsertedRows bigint = @@ROWCOUNT;
                {identityOff}
                SELECT @InsertedRows;
            END TRY
            BEGIN CATCH
                {identityOff}
                THROW;
            END CATCH;
            """;
    }

    private static async Task<long> UpdateChangedAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtRealtimeTableMetadata target,
        IReadOnlyList<CsdtRealtimeColumnMetadata> columns,
        CancellationToken cancellationToken)
    {
        var mutable = columns.Where(column => !column.IsPrimaryKey && !column.IsIdentity).ToArray();
        if (mutable.Length == 0)
        {
            return 0;
        }

        var sql = BuildUpdateCommandText(target, mutable);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            sql,
            transaction: transaction,
            commandTimeout: 120,
            cancellationToken: cancellationToken));
    }

    internal static string BuildUpdateCommandText(
        CsdtRealtimeTableMetadata target,
        IReadOnlyList<CsdtRealtimeColumnMetadata> mutable)
    {
        var exactKey = CsdtRealtimeSourceReader.BuildExactKeyJoin("target", "stage", target.PrimaryKey);
        var assignments = string.Join(
            ", ",
            mutable.Select(column =>
            {
                var quoted = CsdtRealtimeColumnMetadata.Quote(column.Name);
                return $"target.{quoted} = stage.{quoted}";
            }));
        var stageValues = string.Join(
            ", ",
            mutable.Select(column => $"stage.{CsdtRealtimeColumnMetadata.Quote(column.Name)}"));
        var targetValues = string.Join(
            ", ",
            mutable.Select(column => $"target.{CsdtRealtimeColumnMetadata.Quote(column.Name)}"));
        return $"""
            UPDATE target
            SET {assignments}
            FROM {target.Domain.QualifiedTableName} AS target WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN {StageTable} AS stage ON {exactKey}
            WHERE EXISTS
            (
                SELECT {stageValues}
                EXCEPT
                SELECT {targetValues}
            );
            SELECT CONVERT(bigint, @@ROWCOUNT);
            """;
    }

    private static async Task<CsdtRealtimeForwardPlanningContext> ReadForwardPlanningContextAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtRealtimeTableMetadata target,
        CancellationToken cancellationToken,
        bool useAtomicMappedTableContract = false)
    {
        var keyProjection = string.Join(
            ", ",
            target.PrimaryKey.Select(column =>
                $"stage.{CsdtRealtimeColumnMetadata.Quote(column.Name)}"));
        var exactKey = CsdtRealtimeSourceReader.BuildExactKeyJoin(
            "target",
            "stage",
            target.PrimaryKey);
        IReadOnlySet<string> locked = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlySet<string> missingParents = new HashSet<string>(StringComparer.Ordinal);
        if (target.Domain.Name == "KhoaHoc")
        {
            var lifecycle = CsdtRealtimeForwardWritePlanner.BuildV1LifecycleSql(
                "hs",
                useAtomicMappedTableContract);
            var sql = $"""
                SELECT {keyProjection}
                FROM {StageTable} AS stage
                WHERE EXISTS
                (
                    SELECT 1
                    FROM dbo.[BaoCaoI] AS bci
                    INNER JOIN dbo.[BaoCaoII] AS bcii
                      ON CONVERT(varbinary(max), bcii.[MaBCI]) =
                         CONVERT(varbinary(max), bci.[MaBCI])
                    WHERE CONVERT(varbinary(max), bci.[MaKH]) =
                          CONVERT(varbinary(max), stage.[MaKH])
                )
                OR EXISTS
                (
                    SELECT 1
                    FROM dbo.[NguoiLX_HoSo] AS hs
                    WHERE CONVERT(varbinary(max), hs.[MaKhoaHoc]) =
                          CONVERT(varbinary(max), stage.[MaKH])
                      AND {lifecycle}
                );
                """;
            locked = await ReadStageKeySetAsync(
                connection,
                transaction,
                sql,
                cancellationToken);
        }
        else if (target.Domain.Name == "BaoCaoI")
        {
            var lifecycle = CsdtRealtimeForwardWritePlanner.BuildV1LifecycleSql(
                "hs",
                useAtomicMappedTableContract);
            var sql = $"""
                SELECT {keyProjection}
                FROM {StageTable} AS stage
                LEFT JOIN dbo.[BaoCaoI] AS target ON {exactKey}
                WHERE EXISTS
                (
                    SELECT 1
                    FROM dbo.[BaoCaoII] AS bcii
                    WHERE CONVERT(varbinary(max), bcii.[MaBCI]) =
                          CONVERT(varbinary(max), stage.[MaBCI])
                )
                OR EXISTS
                (
                    SELECT 1
                    FROM dbo.[NguoiLX_HoSo] AS hs
                    WHERE
                    (
                        CONVERT(varbinary(max), hs.[MaBC1]) =
                            CONVERT(varbinary(max), stage.[MaBCI])
                        OR CONVERT(varbinary(max), hs.[MaKhoaHoc]) =
                           CONVERT(
                               varbinary(max),
                               COALESCE(target.[MaKH], stage.[MaKH]))
                    )
                    AND {lifecycle}
                );
                """;
            locked = await ReadStageKeySetAsync(
                connection,
                transaction,
                sql,
                cancellationToken);
        }
        else if (target.Domain.Name == "NguoiLX_HoSo")
        {
            var sql = $"""
                SELECT {keyProjection}
                FROM {StageTable} AS stage
                INNER JOIN dbo.[NguoiLX_HoSo] AS target ON {exactKey}
                WHERE NULLIF(target.[MaBC1], '') IS NOT NULL
                  AND EXISTS
                  (
                      SELECT 1
                      FROM dbo.[BaoCaoII] AS bcii
                      WHERE CONVERT(varbinary(max), bcii.[MaBCI]) =
                            CONVERT(varbinary(max), target.[MaBC1])
                  );
                """;
            locked = await ReadStageKeySetAsync(
                connection,
                transaction,
                sql,
                cancellationToken);
        }
        else if (target.Domain.Name == "NguoiLXHS_GiayTo")
        {
            var sql = $"""
                SELECT {keyProjection}
                FROM {StageTable} AS stage
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.[NguoiLX_HoSo] AS hs
                    WHERE CONVERT(varbinary(max), hs.[MaDK]) =
                          CONVERT(varbinary(max), stage.[MaDK])
                );
                """;
            missingParents = await ReadStageKeySetAsync(
                connection,
                transaction,
                sql,
                cancellationToken);
        }

        return new CsdtRealtimeForwardPlanningContext(locked, missingParents);
    }

    private static async Task<IReadOnlySet<string>> ReadStageKeySetAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        var rows = (await connection.QueryAsync(new CommandDefinition(
            sql,
            transaction: transaction,
            commandTimeout: 120,
            cancellationToken: cancellationToken))).AsList();
        return rows.Select(row =>
        {
            var values = (IDictionary<string, object?>)row;
            return JsonSerializer.Serialize(
                values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        }).ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<DataTable> ReadLockedTargetRowsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtRealtimeTableMetadata target,
        CancellationToken cancellationToken)
    {
        var exactKey = CsdtRealtimeSourceReader.BuildExactKeyJoin(
            "target",
            "stage",
            target.PrimaryKey);
        var selectedColumns = string.Join(
            ", ",
            target.WritableColumns.Select(column =>
                $"target.{CsdtRealtimeColumnMetadata.Quote(column.Name)}"));
        var orderBy = string.Join(
            ", ",
            target.PrimaryKey.Select(column =>
                $"target.{CsdtRealtimeColumnMetadata.Quote(column.Name)}"));

        await using var command = new SqlCommand(
            $"""
             SELECT {selectedColumns}
             FROM {target.Domain.QualifiedTableName} AS target WITH (UPDLOCK, HOLDLOCK)
             INNER JOIN {StageTable} AS stage ON {exactKey}
             ORDER BY {orderBy};
             """,
            connection,
            transaction)
        {
            CommandTimeout = 120,
        };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new DataTable
        {
            CaseSensitive = true,
            Locale = CultureInfo.InvariantCulture,
        };
        rows.Load(reader);
        return rows;
    }

    private static void ThrowIfTargetChangedOrMissing(
        DataTable currentTargetRows,
        CsdtRealtimeTableMetadata target,
        IReadOnlyDictionary<string, byte[]> expectedTargetHashes,
        int expectedRowCount)
    {
        if (currentTargetRows.Rows.Count != expectedRowCount ||
            expectedTargetHashes.Count != expectedRowCount)
        {
            throw new CsdtRealtimeTargetConflictException(
                $"dbo.{target.Domain.TableName} changed after the reverse plan was generated.");
        }

        foreach (DataRow row in currentTargetRows.Rows)
        {
            var keyJson = BuildKeyJson(row, target);
            if (!expectedTargetHashes.TryGetValue(keyJson, out var expectedHash) ||
                expectedHash.Length != SHA256.HashSizeInBytes)
            {
                throw new CsdtRealtimeTargetConflictException(
                    $"dbo.{target.Domain.TableName} identity changed after the reverse plan.");
            }

            var actualHash = HashRow(row, target.WritableColumns);
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            {
                throw new CsdtRealtimeTargetConflictException(
                    $"dbo.{target.Domain.TableName} changed after the reverse plan was generated.");
            }
        }
    }

    private static async Task VerifyForwardIdentityReadbackAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtRealtimeTableMetadata target,
        CancellationToken cancellationToken)
    {
        var exactKey = CsdtRealtimeSourceReader.BuildExactKeyJoin(
            "target",
            "stage",
            target.PrimaryKey);
        var mismatchCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            $"""
             SELECT COUNT_BIG(1)
             FROM {StageTable} AS stage
             LEFT JOIN {target.Domain.QualifiedTableName} AS target ON {exactKey}
             WHERE target.{CsdtRealtimeColumnMetadata.Quote(target.PrimaryKey[0].Name)} IS NULL;
             """,
            transaction: transaction,
            commandTimeout: 120,
            cancellationToken: cancellationToken));
        if (mismatchCount > 0)
        {
            throw new CsdtRealtimeTargetConflictException(
                $"dbo.{target.Domain.TableName} immutable identity readback failed.");
        }
    }

    private static async Task VerifyCriticalIdentityReadbackAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtRealtimeTableMetadata target,
        CancellationToken cancellationToken)
    {
        var critical = new HashSet<string>(target.PrimaryKey.Select(item => item.Name), StringComparer.Ordinal);
        foreach (var name in new[] { "MaCSDT", "MaKH", "MaDK", "MaKhoaHoc", "SoGiayCNTN" })
        {
            if (target.Columns.Any(column => column.Name == name))
            {
                critical.Add(name);
            }
        }

        var exactKey = CsdtRealtimeSourceReader.BuildExactKeyJoin("target", "stage", target.PrimaryKey);
        var targetByName = target.Columns.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var mismatches = string.Join(
            " OR ",
            critical.Select(name =>
            {
                var column = targetByName[name];
                var quoted = CsdtRealtimeColumnMetadata.Quote(name);
                if (column.IsText)
                {
                    return $"ISNULL(CONVERT(varbinary(max), target.{quoted}), 0x) <> " +
                           $"ISNULL(CONVERT(varbinary(max), stage.{quoted}), 0x) OR " +
                           $"(target.{quoted} IS NULL AND stage.{quoted} IS NOT NULL) OR " +
                           $"(target.{quoted} IS NOT NULL AND stage.{quoted} IS NULL)";
                }

                return $"target.{quoted} <> stage.{quoted} OR " +
                       $"(target.{quoted} IS NULL AND stage.{quoted} IS NOT NULL) OR " +
                       $"(target.{quoted} IS NOT NULL AND stage.{quoted} IS NULL)";
            }));
        var mismatchCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            $"""
             SELECT COUNT_BIG(1)
             FROM {StageTable} AS stage
             LEFT JOIN {target.Domain.QualifiedTableName} AS target ON {exactKey}
             WHERE target.{CsdtRealtimeColumnMetadata.Quote(target.PrimaryKey[0].Name)} IS NULL
                OR ({mismatches});
             """,
            transaction: transaction,
            commandTimeout: 120,
            cancellationToken: cancellationToken));
        if (mismatchCount > 0)
        {
            throw new CsdtRealtimeTargetConflictException(
                $"dbo.{target.Domain.TableName} identity readback did not exactly match V2.");
        }
    }

    private static IReadOnlyList<CsdtRealtimeEntitySnapshot> BuildEntitySnapshots(
        DataTable rows,
        CsdtRealtimeTableMetadata metadata,
        IReadOnlyList<CsdtRealtimeColumnMetadata> hashColumns)
    {
        var result = new List<CsdtRealtimeEntitySnapshot>(rows.Rows.Count);
        foreach (DataRow row in rows.Rows)
        {
            var keyJson = BuildKeyJson(row, metadata);
            var hash = HashRow(row, hashColumns);
            result.Add(new CsdtRealtimeEntitySnapshot(keyJson, hash, hash));
        }

        return result;
    }

    internal static string BuildKeyJson(
        DataRow row,
        CsdtRealtimeTableMetadata metadata)
    {
        var key = metadata.PrimaryKey.ToDictionary(
            column => column.Name,
            column => row[column.Name] is DBNull ? null : row[column.Name],
            StringComparer.Ordinal);
        return JsonSerializer.Serialize(
            key.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    internal static byte[] HashRow(
        DataRow row,
        IReadOnlyList<CsdtRealtimeColumnMetadata> columns)
    {
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var column in columns.OrderBy(item => item.ColumnId))
        {
            Append(incremental, column.Name);
            var value = row[column.Name];
            AppendValue(incremental, value);
        }

        return incremental.GetHashAndReset();
    }

    internal static byte[] HashKey(string keyJson) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(keyJson));

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static void AppendValue(IncrementalHash hash, object value)
    {
        if (value is DBNull)
        {
            Append(hash, "<NULL>");
            return;
        }

        switch (value)
        {
            case byte[] bytes:
                Append(hash, "<BINARY>");
                hash.AppendData(BitConverter.GetBytes(bytes.Length));
                hash.AppendData(bytes);
                return;
            case DateTime dateTime:
                Append(hash, dateTime.ToString("O", CultureInfo.InvariantCulture));
                return;
            case DateTimeOffset dateTimeOffset:
                Append(hash, dateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
                return;
            case float single:
                Append(hash, single.ToString("R", CultureInfo.InvariantCulture));
                return;
            case double doubleValue:
                Append(hash, doubleValue.ToString("R", CultureInfo.InvariantCulture));
                return;
            case decimal decimalValue:
                Append(hash, decimalValue.ToString("G29", CultureInfo.InvariantCulture));
                return;
            case Guid guid:
                Append(hash, guid.ToString("D"));
                return;
            case bool boolean:
                Append(hash, boolean ? "1" : "0");
                return;
            default:
                Append(
                    hash,
                    Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                return;
        }
    }
}

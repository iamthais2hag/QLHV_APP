using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Infrastructure.Sync;

public sealed class MotoSyncRepository : IMotoSyncRepository
{
    private const string BaoCaoICrossCourseConflictBlocker =
        "dbo.BaoCaoI co khoa dong trung voi khoa khac trong target; khong the insert an toan cho MaKhoaHoc dang chon.";

    private static readonly IReadOnlyList<string> SyncTables =
    [
        "NguoiLX",
        "NguoiLX_HoSo",
        "NguoiLXHS_GiayTo",
    ];

    private readonly IConnectionSettingsProvider _connections;
    private readonly AppSyncOptions _options;

    public MotoSyncRepository(
        IConnectionSettingsProvider connections,
        IOptions<AppSyncOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task<MotoSyncPlanDto> BuildPlanAsync(
        MotoSyncPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();

        await using var source = new SqlConnection(await ResolveConnectionStringAsync(
            request.SourceProfileCode,
            cancellationToken));
        await using var target = new SqlConnection(await ResolveConnectionStringAsync(
            request.TargetProfileCode,
            cancellationToken));

        await source.OpenAsync(cancellationToken);
        await target.OpenAsync(cancellationToken);

        foreach (var table in SyncTables.Concat(["KhoaHoc", "BaoCaoI"]))
        {
            if (!await TableExistsAsync(source, table, null, cancellationToken))
            {
                blockers.Add($"Source thieu bang dbo.{table}.");
            }

            if (!await TableExistsAsync(target, table, null, cancellationToken))
            {
                blockers.Add($"Target thieu bang dbo.{table}.");
            }
        }

        if (blockers.Count > 0)
        {
            return WithResult(request, blockers, warnings);
        }

        var sourceRows = await CountHoSoAsync(source, request.MaKhoaHoc, null, cancellationToken);
        var targetRows = await CountHoSoAsync(target, request.MaKhoaHoc, null, cancellationToken);
        var sourceMaDks = await ReadHoSoMaDksAsync(source, request.MaKhoaHoc, null, cancellationToken);
        var targetMaDksForFilter = await ReadHoSoMaDksAsync(target, request.MaKhoaHoc, null, cancellationToken);
        var targetAllHoSoMaDks = await ReadHoSoMaDksAsync(target, null, null, cancellationToken);

        var targetAllSet = targetAllHoSoMaDks.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceSet = sourceMaDks.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceOnlyMaDks = sourceMaDks
            .Where(maDk => !targetAllSet.Contains(maDk))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var exactOverlapMaDks = sourceMaDks
            .Where(maDk => targetAllSet.Contains(maDk))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var targetOnly = targetMaDksForFilter.Count(maDk => !sourceSet.Contains(maDk));
        var exactOverlap = exactOverlapMaDks.Length;

        var duplicateBusinessKeyGroups = await CountDuplicateBusinessKeysAsync(
            source,
            request.MaKhoaHoc,
            null,
            cancellationToken);
        var shortFullMaDkPairs = await CountShortFullMaDkPairsAsync(
            source,
            request.MaKhoaHoc,
            null,
            cancellationToken);

        if (duplicateBusinessKeyGroups > 0)
        {
            blockers.Add($"Nguon co {duplicateBusinessKeyGroups} nhom trung business key.");
        }

        if (shortFullMaDkPairs > 0)
        {
            blockers.Add($"Nguon co {shortFullMaDkPairs} cap MaDK ngan/day du nghi duplicate.");
        }

        if (request.AllowDirtyData && (duplicateBusinessKeyGroups > 0 || shortFullMaDkPairs > 0))
        {
            warnings.Add("AllowDirtyData chi de preview; duplicate blockers van chan execute trong task nay.");
        }

        var sourceKhoaHoc = await ReadKhoaHocKeysAsync(source, null, cancellationToken);
        var targetKhoaHoc = await ReadKhoaHocKeysAsync(target, null, cancellationToken);
        var plannedInsertKhoaHoc = !string.IsNullOrWhiteSpace(request.MaKhoaHoc) &&
                                   sourceKhoaHoc.Contains(request.MaKhoaHoc) &&
                                   !targetKhoaHoc.Contains(request.MaKhoaHoc)
            ? 1
            : 0;
        var sourceOnlyKhoaHoc = await ReadHoSoKhoaHocByMaDksAsync(source, sourceOnlyMaDks, cancellationToken);
        var missingKhoaHocValues = sourceOnlyKhoaHoc
            .Values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(value => !targetKhoaHoc.Contains(value))
            .ToArray();
        var missingKhoaHocDependencies = missingKhoaHocValues.Length;
        var unresolvedMissingKhoaHoc = missingKhoaHocValues
            .Where(value => plannedInsertKhoaHoc == 0 ||
                            !string.Equals(value, request.MaKhoaHoc, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (unresolvedMissingKhoaHoc.Length > 0)
        {
            blockers.Add($"Target thieu {unresolvedMissingKhoaHoc.Length} MaKhoaHoc dependency chua the tu them an toan.");
        }

        if (plannedInsertKhoaHoc > 0)
        {
            warnings.Add($"Target thieu KhoaHoc {request.MaKhoaHoc}; plan se insert dbo.KhoaHoc truoc hoc vien neu execute.");
            await AddKhoaHocInsertBlockersAsync(
                source,
                target,
                request.MaKhoaHoc!,
                blockers,
                warnings,
                null,
                cancellationToken);
        }

        var plannedInsertBaoCaoI = await BuildBaoCaoIInsertPlanAsync(
            source,
            target,
            request.MaKhoaHoc,
            blockers,
            warnings,
            null,
            cancellationToken);

        var targetNguoiLx = await ReadMaDkKeysAsync(target, "NguoiLX", sourceOnlyMaDks, null, cancellationToken);
        var sourceNguoiLx = await ReadMaDkKeysAsync(source, "NguoiLX", sourceOnlyMaDks, null, cancellationToken);
        var plannedInsertNguoiLx = sourceOnlyMaDks
            .Count(maDk => sourceNguoiLx.Contains(maDk) && !targetNguoiLx.Contains(maDk));
        var availableKhoaHocForPlan = targetKhoaHoc.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (plannedInsertKhoaHoc > 0 && !string.IsNullOrWhiteSpace(request.MaKhoaHoc))
        {
            availableKhoaHocForPlan.Add(request.MaKhoaHoc);
        }

        var plannedInsertHoSo = sourceOnlyMaDks
            .Count(maDk => sourceOnlyKhoaHoc.TryGetValue(maDk, out var maKhoaHoc) &&
                            !string.IsNullOrWhiteSpace(maKhoaHoc) &&
                            availableKhoaHocForPlan.Contains(maKhoaHoc));

        var sourceGiayToKeys = await ReadGiayToKeysAsync(source, sourceOnlyMaDks, null, cancellationToken);
        var targetGiayToKeys = await ReadGiayToKeysAsync(target, sourceOnlyMaDks, null, cancellationToken);
        var plannedInsertGiayTo = sourceGiayToKeys.Count(key => !targetGiayToKeys.Contains(key));

        await AddMappingBlockersAsync(
            source,
            target,
            sourceOnlyMaDks,
            plannedInsertNguoiLx,
            plannedInsertHoSo,
            plannedInsertGiayTo,
            blockers,
            warnings,
            null,
            cancellationToken);

        var nguoiLxUpdatePlan = await BuildTableUpdatePlanAsync(
            source,
            target,
            "NguoiLX",
            exactOverlapMaDks,
            request.MaKhoaHoc,
            null,
            cancellationToken);
        var hoSoUpdatePlan = await BuildTableUpdatePlanAsync(
            source,
            target,
            "NguoiLX_HoSo",
            exactOverlapMaDks,
            request.MaKhoaHoc,
            null,
            cancellationToken);

        await AddUpdateWidthBlockersAsync(
            source,
            target,
            "NguoiLX",
            nguoiLxUpdatePlan.Columns,
            nguoiLxUpdatePlan.Detection.UpdatedMaDks,
            request.MaKhoaHoc,
            blockers,
            warnings,
            null,
            cancellationToken);
        await AddUpdateWidthBlockersAsync(
            source,
            target,
            "NguoiLX_HoSo",
            hoSoUpdatePlan.Columns,
            hoSoUpdatePlan.Detection.UpdatedMaDks,
            request.MaKhoaHoc,
            blockers,
            warnings,
            null,
            cancellationToken);

        var updateSamples = nguoiLxUpdatePlan.Detection.Samples
            .Concat(hoSoUpdatePlan.Detection.Samples)
            .Take(20)
            .ToArray();
        var plannedUpdateNguoiLx = nguoiLxUpdatePlan.Detection.UpdatedRowCount;
        var plannedUpdateHoSo = hoSoUpdatePlan.Detection.UpdatedRowCount;

        return new MotoSyncPlanDto
        {
            Direction = request.Direction,
            SourceProfileCode = request.SourceProfileCode,
            TargetProfileCode = request.TargetProfileCode,
            MaKhoaHoc = request.MaKhoaHoc,
            AllowDirtyData = request.AllowDirtyData,
            SourceRows = sourceRows,
            TargetRows = targetRows,
            ExactMaDkOverlap = exactOverlap,
            SourceOnly = sourceOnlyMaDks.Length,
            TargetOnly = targetOnly,
            DuplicateBusinessKeyGroups = duplicateBusinessKeyGroups,
            ShortFullMaDkPairs = shortFullMaDkPairs,
            MissingKhoaHocDependencies = missingKhoaHocDependencies,
            PlannedInsertKhoaHoc = plannedInsertKhoaHoc,
            PlannedInsertBaoCaoI = plannedInsertBaoCaoI,
            PlannedInsertNguoiLX = plannedInsertNguoiLx,
            PlannedInsertNguoiLXHoSo = plannedInsertHoSo,
            PlannedInsertGiayTo = plannedInsertGiayTo,
            PlannedUpdate = plannedUpdateNguoiLx + plannedUpdateHoSo,
            PlannedUpdateNguoiLX = plannedUpdateNguoiLx,
            PlannedUpdateNguoiLXHoSo = plannedUpdateHoSo,
            UpdateSamples = updateSamples,
            Executable = blockers.Count == 0,
            Blockers = blockers,
            Warnings = warnings,
        };
    }

    public async Task<MotoSyncExecuteSummaryDto> ExecuteInsertOnlyAsync(
        MotoSyncPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        await using var source = new SqlConnection(await ResolveConnectionStringAsync(
            request.SourceProfileCode,
            cancellationToken));
        await using var target = new SqlConnection(await ResolveConnectionStringAsync(
            request.TargetProfileCode,
            cancellationToken));

        await source.OpenAsync(cancellationToken);
        await target.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await target.BeginTransactionAsync(cancellationToken);

        try
        {
            var sourceMaDks = await ReadHoSoMaDksAsync(source, request.MaKhoaHoc, null, cancellationToken);
            var targetAllHoSoMaDks = await ReadHoSoMaDksAsync(target, null, transaction, cancellationToken);
            var targetAllSet = targetAllHoSoMaDks.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sourceOnlyMaDks = sourceMaDks
                .Where(maDk => !targetAllSet.Contains(maDk))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var insertedKhoaHoc = await BulkInsertMissingKhoaHocAsync(
                source,
                target,
                transaction,
                request.MaKhoaHoc,
                cancellationToken);
            var insertedBaoCaoI = await BulkInsertMissingBaoCaoIAsync(
                source,
                target,
                transaction,
                request.MaKhoaHoc,
                cancellationToken);
            var insertedNguoiLx = await BulkInsertMissingNguoiLxAsync(
                source,
                target,
                transaction,
                sourceOnlyMaDks,
                cancellationToken);
            var insertedHoSo = await BulkInsertMissingHoSoAsync(
                source,
                target,
                transaction,
                sourceOnlyMaDks,
                cancellationToken);
            var insertedGiayTo = await BulkInsertMissingGiayToAsync(
                source,
                target,
                transaction,
                sourceOnlyMaDks,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            var endedAt = DateTime.UtcNow;
            return new MotoSyncExecuteSummaryDto
            {
                Direction = request.Direction,
                SyncMode = MotoSyncMode.INSERT_ONLY,
                SourceProfileCode = request.SourceProfileCode,
                TargetProfileCode = request.TargetProfileCode,
                MaKhoaHoc = request.MaKhoaHoc,
                InsertedKhoaHoc = insertedKhoaHoc,
                InsertedBaoCaoI = insertedBaoCaoI,
                InsertedNguoiLX = insertedNguoiLx,
                InsertedNguoiLXHoSo = insertedHoSo,
                InsertedGiayTo = insertedGiayTo,
                UpdatedRows = 0,
                DeletedRows = 0,
                StartedAt = startedAt,
                EndedAt = endedAt,
                DurationMs = (long)(endedAt - startedAt).TotalMilliseconds,
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<MotoSyncExecuteSummaryDto> ExecuteInsertAndUpdateAsync(
        MotoSyncPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        await using var source = new SqlConnection(await ResolveConnectionStringAsync(
            request.SourceProfileCode,
            cancellationToken));
        await using var target = new SqlConnection(await ResolveConnectionStringAsync(
            request.TargetProfileCode,
            cancellationToken));

        await source.OpenAsync(cancellationToken);
        await target.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await target.BeginTransactionAsync(cancellationToken);

        try
        {
            var sourceMaDks = await ReadHoSoMaDksAsync(source, request.MaKhoaHoc, null, cancellationToken);
            var targetAllHoSoMaDks = await ReadHoSoMaDksAsync(target, null, transaction, cancellationToken);
            var targetAllSet = targetAllHoSoMaDks.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sourceOnlyMaDks = sourceMaDks
                .Where(maDk => !targetAllSet.Contains(maDk))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var exactOverlapMaDks = sourceMaDks
                .Where(maDk => targetAllSet.Contains(maDk))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var insertedKhoaHoc = await BulkInsertMissingKhoaHocAsync(
                source,
                target,
                transaction,
                request.MaKhoaHoc,
                cancellationToken);
            var insertedBaoCaoI = await BulkInsertMissingBaoCaoIAsync(
                source,
                target,
                transaction,
                request.MaKhoaHoc,
                cancellationToken);
            var insertedNguoiLx = await BulkInsertMissingNguoiLxAsync(
                source,
                target,
                transaction,
                sourceOnlyMaDks,
                cancellationToken);
            var insertedHoSo = await BulkInsertMissingHoSoAsync(
                source,
                target,
                transaction,
                sourceOnlyMaDks,
                cancellationToken);
            var insertedGiayTo = await BulkInsertMissingGiayToAsync(
                source,
                target,
                transaction,
                sourceOnlyMaDks,
                cancellationToken);

            var updatedNguoiLx = await UpdateChangedRowsAsync(
                source,
                target,
                transaction,
                "NguoiLX",
                exactOverlapMaDks,
                request.MaKhoaHoc,
                cancellationToken);
            var updatedHoSo = await UpdateChangedRowsAsync(
                source,
                target,
                transaction,
                "NguoiLX_HoSo",
                exactOverlapMaDks,
                request.MaKhoaHoc,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            var endedAt = DateTime.UtcNow;
            return new MotoSyncExecuteSummaryDto
            {
                Direction = request.Direction,
                SyncMode = MotoSyncMode.INSERT_AND_UPDATE,
                SourceProfileCode = request.SourceProfileCode,
                TargetProfileCode = request.TargetProfileCode,
                MaKhoaHoc = request.MaKhoaHoc,
                InsertedKhoaHoc = insertedKhoaHoc,
                InsertedBaoCaoI = insertedBaoCaoI,
                InsertedNguoiLX = insertedNguoiLx,
                InsertedNguoiLXHoSo = insertedHoSo,
                InsertedGiayTo = insertedGiayTo,
                UpdatedNguoiLX = updatedNguoiLx,
                UpdatedNguoiLXHoSo = updatedHoSo,
                UpdatedRows = updatedNguoiLx + updatedHoSo,
                DeletedRows = 0,
                StartedAt = startedAt,
                EndedAt = endedAt,
                DurationMs = (long)(endedAt - startedAt).TotalMilliseconds,
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<long> BulkInsertMissingKhoaHocAsync(
        SqlConnection source,
        SqlConnection target,
        SqlTransaction transaction,
        string? maKhoaHoc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(maKhoaHoc))
        {
            return 0;
        }

        var targetKhoaHoc = await ReadKhoaHocKeysAsync(target, transaction, cancellationToken);
        if (targetKhoaHoc.Contains(maKhoaHoc))
        {
            return 0;
        }

        var sourceKhoaHoc = await ReadKhoaHocKeysAsync(source, null, cancellationToken);
        if (!sourceKhoaHoc.Contains(maKhoaHoc))
        {
            return 0;
        }

        var columns = await GetCommonSafeInsertColumnsAsync(source, target, "KhoaHoc", transaction, cancellationToken);
        if (!columns.Contains("MaKH", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Khong the insert dbo.KhoaHoc vi thieu cot MaKH chung an toan.");
        }

        var sourceRows = await ReadKhoaHocRowAsync(source, columns, maKhoaHoc, null, cancellationToken);
        if (sourceRows.Rows.Count == 0)
        {
            return 0;
        }

        await BulkCopyAsync(target, transaction, "KhoaHoc", columns, sourceRows, cancellationToken);
        return sourceRows.Rows.Count;
    }

    private async Task<long> BulkInsertMissingBaoCaoIAsync(
        SqlConnection source,
        SqlConnection target,
        SqlTransaction transaction,
        string? maKhoaHoc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(maKhoaHoc))
        {
            return 0;
        }

        var blockers = new List<string>();
        var warnings = new List<string>();
        var shape = await ResolveBaoCaoIInsertShapeAsync(
            source,
            target,
            blockers,
            warnings,
            transaction,
            cancellationToken);
        if (shape is null || blockers.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", blockers));
        }

        var sourceRows = await ReadBaoCaoIRowsAsync(
            source,
            shape.CommonColumns,
            shape.CourseColumn,
            maKhoaHoc,
            null,
            cancellationToken);
        if (sourceRows.Rows.Count == 0)
        {
            return 0;
        }

        var targetKeyRows = await ReadBaoCaoIRowsAsync(
            target,
            shape.KeyColumns,
            shape.CourseColumn,
            maKhoaHoc,
            transaction,
            cancellationToken);
        var rowsToInsert = MotoSyncBaoCaoIPlanner.FilterInsertRowsForSelectedCourse(
            sourceRows,
            targetKeyRows,
            shape.KeyColumns,
            shape.CourseColumn,
            maKhoaHoc);
        if (await HasBaoCaoICrossCourseKeyConflictAsync(
                target,
                shape,
                rowsToInsert,
                maKhoaHoc,
                transaction,
                cancellationToken))
        {
            throw new InvalidOperationException(BaoCaoICrossCourseConflictBlocker);
        }

        await BulkCopyAsync(target, transaction, "BaoCaoI", shape.CommonColumns, rowsToInsert, cancellationToken);
        return rowsToInsert.Rows.Count;
    }

    private async Task<long> BulkInsertMissingNguoiLxAsync(
        SqlConnection source,
        SqlConnection target,
        SqlTransaction transaction,
        IReadOnlyCollection<string> sourceOnlyMaDks,
        CancellationToken cancellationToken)
    {
        if (sourceOnlyMaDks.Count == 0)
        {
            return 0;
        }

        var columns = await GetCommonInsertColumnsAsync(source, target, "NguoiLX", transaction, cancellationToken);
        var sourceRows = await ReadRowsByMaDksAsync(source, "NguoiLX", columns, sourceOnlyMaDks, null, cancellationToken);
        if (sourceRows.Rows.Count == 0)
        {
            return 0;
        }

        var targetExisting = await ReadMaDkKeysAsync(target, "NguoiLX", sourceOnlyMaDks, transaction, cancellationToken);
        var rowsToInsert = FilterRows(sourceRows, row => !targetExisting.Contains(Convert.ToString(row["MaDK"]) ?? string.Empty));
        await BulkCopyAsync(target, transaction, "NguoiLX", columns, rowsToInsert, cancellationToken);
        return rowsToInsert.Rows.Count;
    }

    private async Task<long> BulkInsertMissingHoSoAsync(
        SqlConnection source,
        SqlConnection target,
        SqlTransaction transaction,
        IReadOnlyCollection<string> sourceOnlyMaDks,
        CancellationToken cancellationToken)
    {
        if (sourceOnlyMaDks.Count == 0)
        {
            return 0;
        }

        var columns = await GetCommonInsertColumnsAsync(source, target, "NguoiLX_HoSo", transaction, cancellationToken);
        var sourceRows = await ReadRowsByMaDksAsync(
            source,
            "NguoiLX_HoSo",
            columns,
            sourceOnlyMaDks,
            null,
            cancellationToken);
        if (sourceRows.Rows.Count == 0)
        {
            return 0;
        }

        var targetKhoaHoc = await ReadKhoaHocKeysAsync(target, transaction, cancellationToken);
        var targetExisting = await ReadMaDkKeysAsync(target, "NguoiLX_HoSo", sourceOnlyMaDks, transaction, cancellationToken);
        var rowsToInsert = FilterRows(sourceRows, row =>
        {
            var maDk = Convert.ToString(row["MaDK"]) ?? string.Empty;
            var maKhoaHoc = Convert.ToString(row["MaKhoaHoc"]) ?? string.Empty;
            return !targetExisting.Contains(maDk) && targetKhoaHoc.Contains(maKhoaHoc);
        });

        await BulkCopyAsync(target, transaction, "NguoiLX_HoSo", columns, rowsToInsert, cancellationToken);
        return rowsToInsert.Rows.Count;
    }

    private async Task<long> BulkInsertMissingGiayToAsync(
        SqlConnection source,
        SqlConnection target,
        SqlTransaction transaction,
        IReadOnlyCollection<string> sourceOnlyMaDks,
        CancellationToken cancellationToken)
    {
        if (sourceOnlyMaDks.Count == 0)
        {
            return 0;
        }

        var columns = await GetCommonInsertColumnsAsync(source, target, "NguoiLXHS_GiayTo", transaction, cancellationToken);
        var sourceRows = await ReadRowsByMaDksAsync(
            source,
            "NguoiLXHS_GiayTo",
            columns,
            sourceOnlyMaDks,
            null,
            cancellationToken);
        if (sourceRows.Rows.Count == 0)
        {
            return 0;
        }

        var targetExisting = await ReadGiayToKeysAsync(target, sourceOnlyMaDks, transaction, cancellationToken);
        var rowsToInsert = FilterRows(sourceRows, row =>
        {
            var maDk = Convert.ToString(row["MaDK"]) ?? string.Empty;
            var maGt = Convert.ToString(row["MaGT"]) ?? string.Empty;
            return !targetExisting.Contains($"{maGt}|{maDk}");
        });

        await BulkCopyAsync(target, transaction, "NguoiLXHS_GiayTo", columns, rowsToInsert, cancellationToken);
        return rowsToInsert.Rows.Count;
    }

    private async Task<long> UpdateChangedRowsAsync(
        SqlConnection source,
        SqlConnection target,
        SqlTransaction transaction,
        string tableName,
        IReadOnlyCollection<string> overlapMaDks,
        string? maKhoaHoc,
        CancellationToken cancellationToken)
    {
        if (!MotoSyncUpdateSqlBuilder.IsSupportedUpdateTable(tableName))
        {
            throw new InvalidOperationException($"Bang {tableName} khong nam trong allowlist update Moto sync.");
        }

        if (MotoSyncUpdateSqlBuilder.RequiresMaKhoaHocScope(tableName) && string.IsNullOrWhiteSpace(maKhoaHoc))
        {
            return 0;
        }

        if (overlapMaDks.Count == 0)
        {
            return 0;
        }

        var updateColumns = await GetCommonUpdateColumnsAsync(source, target, tableName, transaction, cancellationToken);
        if (updateColumns.Count == 0)
        {
            return 0;
        }

        var readColumns = new[] { "MaDK" }
            .Concat(updateColumns)
            .ToArray();
        var sourceRows = await ReadRowsByMaDksForUpdateAsync(
            source,
            tableName,
            readColumns,
            overlapMaDks,
            maKhoaHoc,
            null,
            cancellationToken);
        var targetRows = await ReadRowsByMaDksForUpdateAsync(
            target,
            tableName,
            readColumns,
            overlapMaDks,
            maKhoaHoc,
            transaction,
            cancellationToken);
        var targetByMaDk = targetRows.Rows
            .Cast<DataRow>()
            .Select(row => new
            {
                MaDK = Convert.ToString(row["MaDK"])?.Trim() ?? string.Empty,
                Row = row,
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.MaDK))
            .GroupBy(item => item.MaDK, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Row, StringComparer.OrdinalIgnoreCase);

        long updatedRows = 0;
        foreach (DataRow sourceRow in sourceRows.Rows)
        {
            var maDk = Convert.ToString(sourceRow["MaDK"])?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(maDk) || !targetByMaDk.TryGetValue(maDk, out var targetRow))
            {
                continue;
            }

            var changedColumns = updateColumns
                .Where(column => !MotoSyncUpdatePlanner.ValuesEqual(sourceRow[column], targetRow[column]))
                .ToArray();
            if (changedColumns.Length == 0)
            {
                continue;
            }

            updatedRows += await UpdateOneRowAsync(
                target,
                transaction,
                tableName,
                maDk,
                maKhoaHoc,
                changedColumns,
                sourceRow,
                cancellationToken);
        }

        return updatedRows;
    }

    private async Task<int> UpdateOneRowAsync(
        SqlConnection target,
        SqlTransaction transaction,
        string tableName,
        string maDk,
        string? maKhoaHoc,
        IReadOnlyList<string> changedColumns,
        DataRow sourceRow,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            MotoSyncUpdateSqlBuilder.BuildUpdateSql(tableName, changedColumns),
            target,
            transaction);
        command.CommandTimeout = _options.TimeoutSeconds;
        for (var index = 0; index < changedColumns.Count; index++)
        {
            var value = sourceRow[changedColumns[index]];
            command.Parameters.Add(new SqlParameter($"@v{index}", value == DBNull.Value ? DBNull.Value : value));
        }

        command.Parameters.Add(new SqlParameter("@MaDK", maDk));
        if (MotoSyncUpdateSqlBuilder.RequiresMaKhoaHocScope(tableName))
        {
            command.Parameters.Add(new SqlParameter("@MaKhoaHoc", maKhoaHoc ?? string.Empty));
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<string> ResolveConnectionStringAsync(string profileCode, CancellationToken cancellationToken)
    {
        var source = profileCode switch
        {
            "CSDT_V1" => SourceSystem.V1,
            "CSDT_V2" => SourceSystem.V2,
            _ => throw new InvalidOperationException("Profile khong duoc ho tro cho Moto TEST sync."),
        };

        var resolved = await _connections.GetSourceConnectionAsync(source, cancellationToken);
        if (!resolved.IsUsable || string.IsNullOrWhiteSpace(resolved.ConnectionString))
        {
            throw new InvalidOperationException($"{profileCode} chua co cau hinh ket noi TEST dung duoc.");
        }

        return resolved.ConnectionString;
    }

    private static MotoSyncPlanDto WithResult(
        MotoSyncPlanRequest request,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings) => new()
    {
        Direction = request.Direction,
        SourceProfileCode = request.SourceProfileCode,
        TargetProfileCode = request.TargetProfileCode,
        MaKhoaHoc = request.MaKhoaHoc,
        AllowDirtyData = request.AllowDirtyData,
        Executable = false,
        Blockers = blockers,
        Warnings = warnings,
    };

    private async Task AddKhoaHocInsertBlockersAsync(
        SqlConnection source,
        SqlConnection target,
        string maKhoaHoc,
        ICollection<string> blockers,
        ICollection<string> warnings,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var sourceColumns = await ReadColumnsAsync(source, "KhoaHoc", null, cancellationToken);
        var targetColumns = await ReadColumnsAsync(target, "KhoaHoc", transaction, cancellationToken);
        if (sourceColumns.Count == 0 || targetColumns.Count == 0)
        {
            blockers.Add("Khong doc duoc metadata dbo.KhoaHoc.");
            return;
        }

        var commonColumns = MotoSyncInsertColumnPlanner.SelectCommonSafeInsertColumns(
            sourceColumns.Select(ToInsertColumnInfo),
            targetColumns.Select(ToInsertColumnInfo));
        if (!commonColumns.Contains("MaKH", StringComparer.OrdinalIgnoreCase))
        {
            blockers.Add("dbo.KhoaHoc thieu cot MaKH chung an toan de insert.");
        }

        var missingRequired = MotoSyncInsertColumnPlanner.FindMissingRequiredTargetColumns(
            sourceColumns.Select(ToInsertColumnInfo),
            targetColumns.Select(ToInsertColumnInfo));
        if (missingRequired.Count > 0)
        {
            blockers.Add($"dbo.KhoaHoc co cot bat buoc chi co o target: {string.Join(", ", missingRequired)}.");
        }

        var sourceByName = sourceColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var targetByName = targetColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var columnName in commonColumns)
        {
            if (!sourceByName.TryGetValue(columnName, out var sourceColumn) ||
                !targetByName.TryGetValue(columnName, out var targetColumn) ||
                !IsStringType(sourceColumn.DataType) ||
                !IsStringType(targetColumn.DataType))
            {
                continue;
            }

            var sourceLimit = MotoSyncStringWidthGuard.ToCharacterLimit(
                sourceColumn.DataType,
                sourceColumn.MaxLength);
            var targetLimit = MotoSyncStringWidthGuard.ToCharacterLimit(
                targetColumn.DataType,
                targetColumn.MaxLength);
            if (targetLimit is null || (sourceLimit is not null && sourceLimit <= targetLimit.Value))
            {
                continue;
            }

            var actualMaxLength = await GetMaxActualStringLengthByKhoaHocAsync(
                source,
                columnName,
                maKhoaHoc,
                null,
                cancellationToken);
            var widthResult = MotoSyncStringWidthGuard.Evaluate(
                "KhoaHoc",
                columnName,
                sourceColumn.DataType,
                sourceColumn.MaxLength,
                targetColumn.DataType,
                targetColumn.MaxLength,
                actualMaxLength,
                "planned KhoaHoc insert");

            if (widthResult.IsBlocker)
            {
                blockers.Add(widthResult.Message);
            }
            else if (widthResult.IsWarning)
            {
                warnings.Add(widthResult.Message);
            }
        }
    }

    private async Task<long> BuildBaoCaoIInsertPlanAsync(
        SqlConnection source,
        SqlConnection target,
        string? maKhoaHoc,
        ICollection<string> blockers,
        ICollection<string> warnings,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(maKhoaHoc))
        {
            return 0;
        }

        var shape = await ResolveBaoCaoIInsertShapeAsync(
            source,
            target,
            blockers,
            warnings,
            transaction,
            cancellationToken);
        if (shape is null)
        {
            return 0;
        }

        var sourceRows = await ReadBaoCaoIRowsAsync(
            source,
            shape.CommonColumns,
            shape.CourseColumn,
            maKhoaHoc,
            null,
            cancellationToken);
        if (sourceRows.Rows.Count == 0)
        {
            return 0;
        }

        var targetKeyRows = await ReadBaoCaoIRowsAsync(
            target,
            shape.KeyColumns,
            shape.CourseColumn,
            maKhoaHoc,
            transaction,
            cancellationToken);
        var rowsWithMissingKeys = sourceRows.Rows
            .Cast<DataRow>()
            .Count(row => !MotoSyncBaoCaoIPlanner.TryBuildRowKey(row, shape.KeyColumns, out _));
        if (rowsWithMissingKeys > 0)
        {
            blockers.Add($"dbo.BaoCaoI co {rowsWithMissingKeys} dong source thieu gia tri khoa dong an toan: {string.Join(", ", shape.KeyColumns)}.");
        }

        var plannedRows = MotoSyncBaoCaoIPlanner.FilterInsertRowsForSelectedCourse(
            sourceRows,
            targetKeyRows,
            shape.KeyColumns,
            shape.CourseColumn,
            maKhoaHoc);
        if (plannedRows.Rows.Count == 0)
        {
            return 0;
        }

        if (await HasBaoCaoICrossCourseKeyConflictAsync(
                target,
                shape,
                plannedRows,
                maKhoaHoc,
                transaction,
                cancellationToken))
        {
            blockers.Add(BaoCaoICrossCourseConflictBlocker);
        }

        warnings.Add($"Plan se insert {plannedRows.Rows.Count} dong dbo.BaoCaoI cho khoa {maKhoaHoc} neu execute.");
        AddBaoCaoIWidthBlockers(shape, plannedRows, blockers, warnings);
        return plannedRows.Rows.Count;
    }

    private async Task<BaoCaoIInsertShape?> ResolveBaoCaoIInsertShapeAsync(
        SqlConnection source,
        SqlConnection target,
        ICollection<string> blockers,
        ICollection<string> warnings,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        _ = warnings;
        var sourceColumns = await ReadColumnsAsync(source, "BaoCaoI", null, cancellationToken);
        var targetColumns = await ReadColumnsAsync(target, "BaoCaoI", transaction, cancellationToken);
        if (sourceColumns.Count == 0 || targetColumns.Count == 0)
        {
            blockers.Add("Khong doc duoc metadata dbo.BaoCaoI.");
            return null;
        }

        var courseColumn = MotoSyncBaoCaoIPlanner.DetectCourseScopeColumn(
            sourceColumns.Select(column => column.Name),
            targetColumns.Select(column => column.Name));
        if (courseColumn is null)
        {
            blockers.Add("dbo.BaoCaoI khong co cot khoa hoc duoc ho tro de loc theo MaKhoaHoc.");
            return null;
        }

        var commonColumns = MotoSyncInsertColumnPlanner.SelectCommonSafeInsertColumns(
            sourceColumns.Select(ToInsertColumnInfo),
            targetColumns.Select(ToInsertColumnInfo));
        if (!commonColumns.Contains(courseColumn, StringComparer.OrdinalIgnoreCase))
        {
            blockers.Add("dbo.BaoCaoI thieu cot khoa hoc chung an toan de insert.");
            return null;
        }

        var sourcePrimaryKey = await ReadPrimaryKeyColumnsAsync(source, "BaoCaoI", null, cancellationToken);
        var targetPrimaryKey = await ReadPrimaryKeyColumnsAsync(target, "BaoCaoI", transaction, cancellationToken);
        var keyColumns = MotoSyncBaoCaoIPlanner.ResolveRowIdentityColumns(
            sourceColumns.Select(ToInsertColumnInfo),
            targetColumns.Select(ToInsertColumnInfo),
            sourcePrimaryKey,
            targetPrimaryKey);
        if (keyColumns.Count == 0)
        {
            blockers.Add("dbo.BaoCaoI chua xac dinh duoc khoa dong an toan de insert-only.");
            return null;
        }

        var commonSet = commonColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!keyColumns.All(commonSet.Contains))
        {
            blockers.Add($"dbo.BaoCaoI thieu cot khoa dong chung an toan: {string.Join(", ", keyColumns)}.");
            return null;
        }

        var missingRequired = MotoSyncInsertColumnPlanner.FindMissingRequiredTargetColumns(
            sourceColumns.Select(ToInsertColumnInfo),
            targetColumns.Select(ToInsertColumnInfo));
        if (missingRequired.Count > 0)
        {
            blockers.Add($"dbo.BaoCaoI co cot bat buoc chi co o target: {string.Join(", ", missingRequired)}.");
        }

        return new BaoCaoIInsertShape(
            courseColumn,
            keyColumns,
            commonColumns,
            sourceColumns,
            targetColumns);
    }

    private static void AddBaoCaoIWidthBlockers(
        BaoCaoIInsertShape shape,
        DataTable plannedRows,
        ICollection<string> blockers,
        ICollection<string> warnings)
    {
        var sourceByName = shape.SourceColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var targetByName = shape.TargetColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var columnName in shape.CommonColumns)
        {
            if (!sourceByName.TryGetValue(columnName, out var sourceColumn) ||
                !targetByName.TryGetValue(columnName, out var targetColumn) ||
                !IsStringType(sourceColumn.DataType) ||
                !IsStringType(targetColumn.DataType))
            {
                continue;
            }

            var actualMaxLength = GetMaxStringLength(plannedRows, columnName);
            var widthResult = MotoSyncStringWidthGuard.Evaluate(
                "BaoCaoI",
                columnName,
                sourceColumn.DataType,
                sourceColumn.MaxLength,
                targetColumn.DataType,
                targetColumn.MaxLength,
                actualMaxLength,
                "planned BaoCaoI insert");

            if (widthResult.IsBlocker)
            {
                blockers.Add(widthResult.Message);
            }
            else if (widthResult.IsWarning)
            {
                warnings.Add(widthResult.Message);
            }
        }
    }

    private async Task<bool> HasBaoCaoICrossCourseKeyConflictAsync(
        SqlConnection target,
        BaoCaoIInsertShape shape,
        DataTable plannedRows,
        string maKhoaHoc,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (plannedRows.Rows.Count == 0 ||
            MotoSyncBaoCaoIPlanner.IdentityIncludesCourseScope(shape.KeyColumns, shape.CourseColumn))
        {
            return false;
        }

        var targetOutsideCourseKeys = await ReadBaoCaoIKeyRowsOutsideCourseAsync(
            target,
            shape.KeyColumns,
            shape.CourseColumn,
            maKhoaHoc,
            transaction,
            cancellationToken);
        return MotoSyncBaoCaoIPlanner.HasCrossCourseKeyConflict(
            plannedRows,
            targetOutsideCourseKeys,
            shape.KeyColumns,
            shape.CourseColumn);
    }

    private async Task AddMappingBlockersAsync(
        SqlConnection source,
        SqlConnection target,
        IReadOnlyCollection<string> plannedMaDks,
        long plannedInsertNguoiLx,
        long plannedInsertHoSo,
        long plannedInsertGiayTo,
        ICollection<string> blockers,
        ICollection<string> warnings,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (plannedInsertNguoiLx > 0)
        {
            await AddTableMappingBlockersAsync(
                source,
                target,
                "NguoiLX",
                plannedMaDks,
                blockers,
                warnings,
                transaction,
                cancellationToken);
        }

        if (plannedInsertHoSo > 0)
        {
            await AddTableMappingBlockersAsync(
                source,
                target,
                "NguoiLX_HoSo",
                plannedMaDks,
                blockers,
                warnings,
                transaction,
                cancellationToken);
        }

        if (plannedInsertGiayTo > 0)
        {
            await AddTableMappingBlockersAsync(
                source,
                target,
                "NguoiLXHS_GiayTo",
                plannedMaDks,
                blockers,
                warnings,
                transaction,
                cancellationToken);
        }
    }

    private async Task AddTableMappingBlockersAsync(
        SqlConnection source,
        SqlConnection target,
        string tableName,
        IReadOnlyCollection<string> plannedMaDks,
        ICollection<string> blockers,
        ICollection<string> warnings,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var sourceColumns = await ReadColumnsAsync(source, tableName, null, cancellationToken);
        var targetColumns = await ReadColumnsAsync(target, tableName, transaction, cancellationToken);
        if (sourceColumns.Count == 0 || targetColumns.Count == 0)
        {
            blockers.Add($"Khong doc duoc metadata dbo.{tableName}.");
            return;
        }

        var sourceByName = sourceColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var insertableTarget = targetColumns
            .Where(c => !c.IsIdentity && !c.IsComputed && !c.IsRowVersion)
            .ToArray();
        var missingRequired = insertableTarget
            .Where(c => !c.IsNullable && !c.HasDefault && !sourceByName.ContainsKey(c.Name))
            .Select(c => c.Name)
            .ToArray();
        if (missingRequired.Length > 0)
        {
            blockers.Add($"dbo.{tableName} co cot bat buoc chi co o target: {string.Join(", ", missingRequired)}.");
        }

        foreach (var targetColumn in insertableTarget)
        {
            if (!sourceByName.TryGetValue(targetColumn.Name, out var sourceColumn))
            {
                continue;
            }

            if (IsStringType(sourceColumn.DataType) &&
                IsStringType(targetColumn.DataType))
            {
                var sourceLimit = MotoSyncStringWidthGuard.ToCharacterLimit(
                    sourceColumn.DataType,
                    sourceColumn.MaxLength);
                var targetLimit = MotoSyncStringWidthGuard.ToCharacterLimit(
                    targetColumn.DataType,
                    targetColumn.MaxLength);
                if (targetLimit is null || (sourceLimit is not null && sourceLimit <= targetLimit.Value))
                {
                    continue;
                }

                var actualMaxLength = await GetMaxActualStringLengthAsync(
                    source,
                    tableName,
                    targetColumn.Name,
                    plannedMaDks,
                    null,
                    null,
                    cancellationToken);
                var widthResult = MotoSyncStringWidthGuard.Evaluate(
                    tableName,
                    targetColumn.Name,
                    sourceColumn.DataType,
                    sourceColumn.MaxLength,
                    targetColumn.DataType,
                    targetColumn.MaxLength,
                    actualMaxLength,
                    "planned insert");

                if (widthResult.IsBlocker)
                {
                    blockers.Add(widthResult.Message);
                }
                else if (widthResult.IsWarning)
                {
                    warnings.Add(widthResult.Message);
                }
            }
        }
    }

    private async Task<TableUpdatePlan> BuildTableUpdatePlanAsync(
        SqlConnection source,
        SqlConnection target,
        string tableName,
        IReadOnlyCollection<string> overlapMaDks,
        string? maKhoaHoc,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (!MotoSyncUpdateSqlBuilder.IsSupportedUpdateTable(tableName))
        {
            throw new InvalidOperationException($"Bang {tableName} khong nam trong allowlist update Moto sync.");
        }

        if (MotoSyncUpdateSqlBuilder.RequiresMaKhoaHocScope(tableName) && string.IsNullOrWhiteSpace(maKhoaHoc))
        {
            return new TableUpdatePlan(
                Array.Empty<string>(),
                new MotoSyncUpdateDetectionResult(Array.Empty<string>(), Array.Empty<MotoSyncUpdateSampleDto>()));
        }

        if (overlapMaDks.Count == 0)
        {
            return new TableUpdatePlan(
                Array.Empty<string>(),
                new MotoSyncUpdateDetectionResult(Array.Empty<string>(), Array.Empty<MotoSyncUpdateSampleDto>()));
        }

        var updateColumns = await GetCommonUpdateColumnsAsync(source, target, tableName, transaction, cancellationToken);
        if (updateColumns.Count == 0)
        {
            return new TableUpdatePlan(
                updateColumns,
                new MotoSyncUpdateDetectionResult(Array.Empty<string>(), Array.Empty<MotoSyncUpdateSampleDto>()));
        }

        var readColumns = new[] { "MaDK" }
            .Concat(updateColumns)
            .ToArray();
        var sourceRows = await ReadRowsByMaDksForUpdateAsync(
            source,
            tableName,
            readColumns,
            overlapMaDks,
            maKhoaHoc,
            null,
            cancellationToken);
        var targetRows = await ReadRowsByMaDksForUpdateAsync(
            target,
            tableName,
            readColumns,
            overlapMaDks,
            maKhoaHoc,
            transaction,
            cancellationToken);

        return new TableUpdatePlan(
            updateColumns,
            MotoSyncUpdatePlanner.Build(tableName, sourceRows, targetRows, updateColumns));
    }

    private async Task AddUpdateWidthBlockersAsync(
        SqlConnection source,
        SqlConnection target,
        string tableName,
        IReadOnlyList<string> updateColumns,
        IReadOnlyCollection<string> plannedUpdateMaDks,
        string? maKhoaHoc,
        ICollection<string> blockers,
        ICollection<string> warnings,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (MotoSyncUpdateSqlBuilder.RequiresMaKhoaHocScope(tableName) && string.IsNullOrWhiteSpace(maKhoaHoc))
        {
            return;
        }

        if (updateColumns.Count == 0 || plannedUpdateMaDks.Count == 0)
        {
            return;
        }

        var sourceColumns = await ReadColumnsAsync(source, tableName, null, cancellationToken);
        var targetColumns = await ReadColumnsAsync(target, tableName, transaction, cancellationToken);
        var sourceByName = sourceColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var targetByName = targetColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var columnName in updateColumns)
        {
            if (!sourceByName.TryGetValue(columnName, out var sourceColumn) ||
                !targetByName.TryGetValue(columnName, out var targetColumn) ||
                !IsStringType(sourceColumn.DataType) ||
                !IsStringType(targetColumn.DataType))
            {
                continue;
            }

            var sourceLimit = MotoSyncStringWidthGuard.ToCharacterLimit(
                sourceColumn.DataType,
                sourceColumn.MaxLength);
            var targetLimit = MotoSyncStringWidthGuard.ToCharacterLimit(
                targetColumn.DataType,
                targetColumn.MaxLength);
            if (targetLimit is null || (sourceLimit is not null && sourceLimit <= targetLimit.Value))
            {
                continue;
            }

            var actualMaxLength = await GetMaxActualStringLengthAsync(
                source,
                tableName,
                columnName,
                plannedUpdateMaDks,
                maKhoaHoc,
                null,
                cancellationToken);
            var widthResult = MotoSyncStringWidthGuard.Evaluate(
                tableName,
                columnName,
                sourceColumn.DataType,
                sourceColumn.MaxLength,
                targetColumn.DataType,
                targetColumn.MaxLength,
                actualMaxLength,
                "planned update");

            if (widthResult.IsBlocker)
            {
                blockers.Add(widthResult.Message);
            }
            else if (widthResult.IsWarning)
            {
                warnings.Add(widthResult.Message);
            }
        }
    }

    private async Task<IReadOnlyList<string>> GetCommonInsertColumnsAsync(
        SqlConnection source,
        SqlConnection target,
        string tableName,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var sourceColumns = await ReadColumnsAsync(source, tableName, null, cancellationToken);
        var targetColumns = await ReadColumnsAsync(target, tableName, transaction, cancellationToken);
        var sourceNames = sourceColumns
            .Where(c => !c.IsIdentity && !c.IsComputed && !c.IsRowVersion)
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return targetColumns
            .Where(c => !c.IsIdentity && !c.IsComputed && !c.IsRowVersion && sourceNames.Contains(c.Name))
            .Select(c => c.Name)
            .ToArray();
    }

    private async Task<IReadOnlyList<string>> GetCommonSafeInsertColumnsAsync(
        SqlConnection source,
        SqlConnection target,
        string tableName,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var sourceColumns = await ReadColumnsAsync(source, tableName, null, cancellationToken);
        var targetColumns = await ReadColumnsAsync(target, tableName, transaction, cancellationToken);
        return MotoSyncInsertColumnPlanner.SelectCommonSafeInsertColumns(
            sourceColumns.Select(ToInsertColumnInfo),
            targetColumns.Select(ToInsertColumnInfo));
    }

    private async Task<IReadOnlyList<string>> GetCommonUpdateColumnsAsync(
        SqlConnection source,
        SqlConnection target,
        string tableName,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var sourceColumns = await ReadColumnsAsync(source, tableName, null, cancellationToken);
        var targetColumns = await ReadColumnsAsync(target, tableName, transaction, cancellationToken);
        var sourceWritable = sourceColumns
            .Where(IsSafeUpdateColumn)
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return targetColumns
            .Where(c => IsSafeUpdateColumn(c) && sourceWritable.Contains(c.Name))
            .Select(c => c.Name)
            .ToArray();
    }

    private async Task<bool> TableExistsAsync(
        SqlConnection connection,
        string tableName,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT_BIG(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = N'dbo' AND t.name = @TableName;",
            connection,
            transaction);
        command.CommandTimeout = _options.TimeoutSeconds;
        command.Parameters.AddWithValue("@TableName", tableName);
        var result = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        return result > 0;
    }

    private async Task<long> CountHoSoAsync(
        SqlConnection connection,
        string? maKhoaHoc,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT_BIG(*) FROM dbo.NguoiLX_HoSo WHERE (@MaKhoaHoc IS NULL OR MaKhoaHoc = @MaKhoaHoc);",
            connection,
            transaction);
        command.CommandTimeout = _options.TimeoutSeconds;
        command.Parameters.AddWithValue("@MaKhoaHoc", (object?)maKhoaHoc ?? DBNull.Value);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private async Task<IReadOnlyList<string>> ReadHoSoMaDksAsync(
        SqlConnection connection,
        string? maKhoaHoc,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT MaDK FROM dbo.NguoiLX_HoSo WHERE MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N'' AND (@MaKhoaHoc IS NULL OR MaKhoaHoc = @MaKhoaHoc);",
            connection,
            transaction);
        command.CommandTimeout = _options.TimeoutSeconds;
        command.Parameters.AddWithValue("@MaKhoaHoc", (object?)maKhoaHoc ?? DBNull.Value);
        return await ReadStringListAsync(command, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadHoSoKhoaHocByMaDksAsync(
        SqlConnection connection,
        IReadOnlyCollection<string> maDks,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in maDks.Chunk(900))
        {
            var parameters = CreateInParameters(chunk, "@p");
            await using var command = new SqlCommand(
                $"SELECT MaDK, MaKhoaHoc FROM dbo.NguoiLX_HoSo WHERE MaDK IN ({string.Join(", ", parameters.Select(p => p.ParameterName))});",
                connection);
            command.CommandTimeout = _options.TimeoutSeconds;
            command.Parameters.AddRange(parameters.ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var maDk = Convert.ToString(reader["MaDK"]) ?? string.Empty;
                var maKhoaHoc = Convert.ToString(reader["MaKhoaHoc"]) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(maDk))
                {
                    result[maDk] = maKhoaHoc;
                }
            }
        }

        return result;
    }

    private async Task<HashSet<string>> ReadKhoaHocKeysAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT MaKH FROM dbo.KhoaHoc WHERE MaKH IS NOT NULL AND LTRIM(RTRIM(MaKH)) <> N'';",
            connection,
            transaction);
        command.CommandTimeout = _options.TimeoutSeconds;
        var values = await ReadStringListAsync(command, cancellationToken);
        return values.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<DataTable> ReadKhoaHocRowAsync(
        SqlConnection connection,
        IReadOnlyList<string> columns,
        string maKhoaHoc,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var columnList = string.Join(", ", columns.Select(Quote));
        await using var command = new SqlCommand(
            $"SELECT {columnList} FROM dbo.KhoaHoc WHERE MaKH = @MaKhoaHoc;",
            connection,
            transaction);
        command.CommandTimeout = _options.TimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@MaKhoaHoc", maKhoaHoc));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    private async Task<DataTable> ReadBaoCaoIRowsAsync(
        SqlConnection connection,
        IReadOnlyList<string> columns,
        string courseColumn,
        string? maKhoaHoc,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (columns.Count == 0)
        {
            return CreateEmptyTable(columns);
        }

        var columnList = string.Join(", ", columns.Select(Quote));
        await using var command = new SqlCommand(
            $"SELECT {columnList} FROM dbo.BaoCaoI WHERE (@MaKhoaHoc IS NULL OR {Quote(courseColumn)} = @MaKhoaHoc);",
            connection,
            transaction);
        command.CommandTimeout = _options.TimeoutSeconds;
        command.Parameters.AddWithValue("@MaKhoaHoc", (object?)maKhoaHoc ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    private async Task<DataTable> ReadBaoCaoIKeyRowsOutsideCourseAsync(
        SqlConnection connection,
        IReadOnlyList<string> keyColumns,
        string courseColumn,
        string maKhoaHoc,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (keyColumns.Count == 0)
        {
            return CreateEmptyTable(keyColumns);
        }

        var columnList = string.Join(", ", keyColumns.Select(Quote));
        await using var command = new SqlCommand(
            $"SELECT {columnList} FROM dbo.BaoCaoI WHERE {Quote(courseColumn)} IS NULL OR {Quote(courseColumn)} <> @MaKhoaHoc;",
            connection,
            transaction);
        command.CommandTimeout = _options.TimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@MaKhoaHoc", maKhoaHoc));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    private async Task<HashSet<string>> ReadMaDkKeysAsync(
        SqlConnection connection,
        string tableName,
        IReadOnlyCollection<string> maDks,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in maDks.Chunk(900))
        {
            if (chunk.Length == 0)
            {
                continue;
            }

            var parameters = CreateInParameters(chunk, "@p");
            await using var command = new SqlCommand(
                $"SELECT MaDK FROM dbo.{Quote(tableName)} WHERE MaDK IN ({string.Join(", ", parameters.Select(p => p.ParameterName))});",
                connection,
                transaction);
            command.CommandTimeout = _options.TimeoutSeconds;
            command.Parameters.AddRange(parameters.ToArray());
            foreach (var value in await ReadStringListAsync(command, cancellationToken))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private async Task<HashSet<string>> ReadGiayToKeysAsync(
        SqlConnection connection,
        IReadOnlyCollection<string> maDks,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in maDks.Chunk(900))
        {
            if (chunk.Length == 0)
            {
                continue;
            }

            var parameters = CreateInParameters(chunk, "@p");
            await using var command = new SqlCommand(
                $"SELECT MaGT, MaDK FROM dbo.NguoiLXHS_GiayTo WHERE MaDK IN ({string.Join(", ", parameters.Select(p => p.ParameterName))});",
                connection,
                transaction);
            command.CommandTimeout = _options.TimeoutSeconds;
            command.Parameters.AddRange(parameters.ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add($"{reader["MaGT"]}|{reader["MaDK"]}");
            }
        }

        return result;
    }

    private async Task<long> GetMaxActualStringLengthAsync(
        SqlConnection connection,
        string tableName,
        string columnName,
        IReadOnlyCollection<string> maDks,
        string? maKhoaHoc,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        long maxLength = 0;
        foreach (var chunk in maDks.Chunk(900))
        {
            if (chunk.Length == 0)
            {
                continue;
            }

            var parameters = CreateInParameters(chunk, "@p");
            var shouldScopeByMaKhoaHoc = MotoSyncUpdateSqlBuilder.RequiresMaKhoaHocScope(tableName) &&
                !string.IsNullOrWhiteSpace(maKhoaHoc);
            var whereClause = shouldScopeByMaKhoaHoc
                ? $"MaDK IN ({string.Join(", ", parameters.Select(p => p.ParameterName))}) AND MaKhoaHoc = @MaKhoaHoc"
                : $"MaDK IN ({string.Join(", ", parameters.Select(p => p.ParameterName))})";
            await using var command = new SqlCommand(
                $"SELECT ISNULL(MAX(LEN({Quote(columnName)})), 0) FROM dbo.{Quote(tableName)} WHERE {whereClause};",
                connection,
                transaction);
            command.CommandTimeout = _options.TimeoutSeconds;
            command.Parameters.AddRange(parameters.ToArray());
            if (shouldScopeByMaKhoaHoc)
            {
                command.Parameters.Add(new SqlParameter("@MaKhoaHoc", maKhoaHoc ?? string.Empty));
            }

            var chunkMax = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (chunkMax > maxLength)
            {
                maxLength = chunkMax;
            }
        }

        return maxLength;
    }

    private async Task<long> GetMaxActualStringLengthByKhoaHocAsync(
        SqlConnection connection,
        string columnName,
        string maKhoaHoc,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            $"SELECT ISNULL(MAX(LEN({Quote(columnName)})), 0) FROM dbo.KhoaHoc WHERE MaKH = @MaKhoaHoc;",
            connection,
            transaction);
        command.CommandTimeout = _options.TimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@MaKhoaHoc", maKhoaHoc));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private async Task<long> CountDuplicateBusinessKeysAsync(
        SqlConnection connection,
        string? maKhoaHoc,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            @"
;WITH Rows AS (
    SELECT
        UPPER(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(ISNULL(nlx.HoVaTen, N''), CHAR(9), N' '), N'  ', N' '), N'  ', N' ')))) AS NormalizedHoVaTen,
        nlx.NgaySinh,
        NULLIF(LTRIM(RTRIM(ISNULL(nlx.SoCMT, N''))), N'') AS NormalizedSoCMT,
        hs.MaKhoaHoc
    FROM dbo.NguoiLX_HoSo hs
    JOIN dbo.NguoiLX nlx ON nlx.MaDK = hs.MaDK
    WHERE (@MaKhoaHoc IS NULL OR hs.MaKhoaHoc = @MaKhoaHoc)
)
SELECT COUNT_BIG(*)
FROM (
    SELECT NormalizedHoVaTen, NgaySinh, NormalizedSoCMT, MaKhoaHoc
    FROM Rows
    GROUP BY NormalizedHoVaTen, NgaySinh, NormalizedSoCMT, MaKhoaHoc
    HAVING COUNT_BIG(*) > 1
) d;",
            connection,
            transaction);
        command.CommandTimeout = _options.TimeoutSeconds;
        command.Parameters.AddWithValue("@MaKhoaHoc", (object?)maKhoaHoc ?? DBNull.Value);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private async Task<long> CountShortFullMaDkPairsAsync(
        SqlConnection connection,
        string? maKhoaHoc,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            @"
;WITH Rows AS (
    SELECT
        hs.MaDK,
        UPPER(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(ISNULL(nlx.HoVaTen, N''), CHAR(9), N' '), N'  ', N' '), N'  ', N' ')))) AS NormalizedHoVaTen,
        nlx.NgaySinh,
        NULLIF(LTRIM(RTRIM(ISNULL(nlx.SoCMT, N''))), N'') AS NormalizedSoCMT
    FROM dbo.NguoiLX_HoSo hs
    JOIN dbo.NguoiLX nlx ON nlx.MaDK = hs.MaDK
    WHERE (@MaKhoaHoc IS NULL OR hs.MaKhoaHoc = @MaKhoaHoc)
)
SELECT COUNT_BIG(*)
FROM Rows shortRow
JOIN Rows fullRow
    ON fullRow.MaDK LIKE shortRow.MaDK + N'%'
   AND fullRow.MaDK <> shortRow.MaDK
   AND LEN(fullRow.MaDK) > LEN(shortRow.MaDK)
   AND LEN(fullRow.MaDK) - LEN(shortRow.MaDK) = 3
   AND fullRow.NormalizedHoVaTen = shortRow.NormalizedHoVaTen
   AND fullRow.NgaySinh = shortRow.NgaySinh
   AND ISNULL(fullRow.NormalizedSoCMT, N'') = ISNULL(shortRow.NormalizedSoCMT, N'');",
            connection,
            transaction);
        command.CommandTimeout = _options.TimeoutSeconds;
        command.Parameters.AddWithValue("@MaKhoaHoc", (object?)maKhoaHoc ?? DBNull.Value);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private async Task<IReadOnlyList<ColumnMetadata>> ReadColumnsAsync(
        SqlConnection connection,
        string tableName,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            @"
SELECT
    c.name AS Name,
    ty.name AS DataType,
    c.max_length AS MaxLength,
    c.is_nullable AS IsNullable,
    c.is_identity AS IsIdentity,
    c.is_computed AS IsComputed,
    CAST(CASE WHEN dc.object_id IS NULL THEN 0 ELSE 1 END AS bit) AS HasDefault
FROM sys.columns c
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id
   AND dc.parent_column_id = c.column_id
WHERE c.object_id = OBJECT_ID(N'dbo.' + @TableName)
ORDER BY c.column_id;",
            connection,
            transaction);
        command.CommandTimeout = _options.TimeoutSeconds;
        command.Parameters.AddWithValue("@TableName", tableName);
        var columns = new List<ColumnMetadata>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnMetadata(
                Convert.ToString(reader["Name"]) ?? string.Empty,
                Convert.ToString(reader["DataType"]) ?? string.Empty,
                Convert.ToInt16(reader["MaxLength"]),
                Convert.ToBoolean(reader["IsNullable"]),
                Convert.ToBoolean(reader["IsIdentity"]),
                Convert.ToBoolean(reader["IsComputed"]),
                Convert.ToBoolean(reader["HasDefault"])));
        }

        return columns;
    }

    private async Task<IReadOnlyList<string>> ReadPrimaryKeyColumnsAsync(
        SqlConnection connection,
        string tableName,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            @"
SELECT c.name
FROM sys.key_constraints kc
JOIN sys.index_columns ic
    ON ic.object_id = kc.parent_object_id
   AND ic.index_id = kc.unique_index_id
JOIN sys.columns c
    ON c.object_id = ic.object_id
   AND c.column_id = ic.column_id
WHERE kc.parent_object_id = OBJECT_ID(N'dbo.' + @TableName)
  AND kc.type = N'PK'
ORDER BY ic.key_ordinal;",
            connection,
            transaction);
        command.CommandTimeout = _options.TimeoutSeconds;
        command.Parameters.AddWithValue("@TableName", tableName);
        return await ReadStringListAsync(command, cancellationToken);
    }

    private async Task<DataTable> ReadRowsByMaDksAsync(
        SqlConnection connection,
        string tableName,
        IReadOnlyList<string> columns,
        IReadOnlyCollection<string> maDks,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        DataTable? result = null;
        foreach (var chunk in maDks.Chunk(900))
        {
            if (chunk.Length == 0)
            {
                continue;
            }

            var parameters = CreateInParameters(chunk, "@p");
            var columnList = string.Join(", ", columns.Select(Quote));
            await using var command = new SqlCommand(
                $"SELECT {columnList} FROM dbo.{Quote(tableName)} WHERE MaDK IN ({string.Join(", ", parameters.Select(p => p.ParameterName))});",
                connection,
                transaction);
            command.CommandTimeout = _options.TimeoutSeconds;
            command.Parameters.AddRange(parameters.ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var chunkTable = new DataTable();
            chunkTable.Load(reader);
            result ??= chunkTable.Clone();
            result.Merge(chunkTable);
        }

        return result ?? CreateEmptyTable(columns);
    }

    private async Task<DataTable> ReadRowsByMaDksForUpdateAsync(
        SqlConnection connection,
        string tableName,
        IReadOnlyList<string> columns,
        IReadOnlyCollection<string> maDks,
        string? maKhoaHoc,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (!MotoSyncUpdateSqlBuilder.IsSupportedUpdateTable(tableName))
        {
            throw new InvalidOperationException($"Bang {tableName} khong nam trong allowlist update Moto sync.");
        }

        if (MotoSyncUpdateSqlBuilder.RequiresMaKhoaHocScope(tableName) && string.IsNullOrWhiteSpace(maKhoaHoc))
        {
            return CreateEmptyTable(columns);
        }

        DataTable? result = null;
        foreach (var chunk in maDks.Chunk(900))
        {
            if (chunk.Length == 0)
            {
                continue;
            }

            var parameters = CreateInParameters(chunk, "@p");
            var columnList = string.Join(", ", columns.Select(Quote));
            var whereClause = MotoSyncUpdateSqlBuilder.RequiresMaKhoaHocScope(tableName)
                ? $"MaDK IN ({string.Join(", ", parameters.Select(p => p.ParameterName))}) AND MaKhoaHoc = @MaKhoaHoc"
                : $"MaDK IN ({string.Join(", ", parameters.Select(p => p.ParameterName))})";
            await using var command = new SqlCommand(
                $"SELECT {columnList} FROM dbo.{Quote(tableName)} WHERE {whereClause};",
                connection,
                transaction);
            command.CommandTimeout = _options.TimeoutSeconds;
            command.Parameters.AddRange(parameters.ToArray());
            if (MotoSyncUpdateSqlBuilder.RequiresMaKhoaHocScope(tableName))
            {
                command.Parameters.Add(new SqlParameter("@MaKhoaHoc", maKhoaHoc ?? string.Empty));
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var chunkTable = new DataTable();
            chunkTable.Load(reader);
            result ??= chunkTable.Clone();
            result.Merge(chunkTable);
        }

        return result ?? CreateEmptyTable(columns);
    }

    private async Task BulkCopyAsync(
        SqlConnection target,
        SqlTransaction transaction,
        string tableName,
        IReadOnlyList<string> columns,
        DataTable rows,
        CancellationToken cancellationToken)
    {
        if (rows.Rows.Count == 0)
        {
            return;
        }

        using var bulkCopy = new SqlBulkCopy(target, SqlBulkCopyOptions.CheckConstraints, transaction)
        {
            DestinationTableName = $"dbo.{Quote(tableName)}",
            BulkCopyTimeout = _options.TimeoutSeconds,
        };

        foreach (var column in columns)
        {
            bulkCopy.ColumnMappings.Add(column, column);
        }

        await bulkCopy.WriteToServerAsync(rows, cancellationToken);
    }

    private static DataTable FilterRows(DataTable source, Func<DataRow, bool> predicate)
    {
        var result = source.Clone();
        foreach (DataRow row in source.Rows)
        {
            if (predicate(row))
            {
                result.ImportRow(row);
            }
        }

        return result;
    }

    private static long GetMaxStringLength(DataTable rows, string columnName)
    {
        long maxLength = 0;
        foreach (DataRow row in rows.Rows)
        {
            if (row[columnName] == DBNull.Value)
            {
                continue;
            }

            var value = Convert.ToString(row[columnName]) ?? string.Empty;
            if (value.Length > maxLength)
            {
                maxLength = value.Length;
            }
        }

        return maxLength;
    }

    private static DataTable CreateEmptyTable(IReadOnlyList<string> columns)
    {
        var table = new DataTable();
        foreach (var column in columns)
        {
            table.Columns.Add(column);
        }

        return table;
    }

    private static async Task<IReadOnlyList<string>> ReadStringListAsync(
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = Convert.ToString(reader[0]);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        return values;
    }

    private static IReadOnlyList<SqlParameter> CreateInParameters(IReadOnlyList<string> values, string prefix)
        => values
            .Select((value, index) => new SqlParameter($"{prefix}{index}", value))
            .ToArray();

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static bool IsStringType(string dataType)
        => dataType is "varchar" or "nvarchar" or "char" or "nchar";

    private static bool IsSafeUpdateColumn(ColumnMetadata column)
        => !column.IsIdentity &&
           !column.IsComputed &&
           !column.IsRowVersion &&
           !IsBinaryType(column.DataType) &&
           !IsKeyOrIdColumn(column.Name);

    private static bool IsKeyOrIdColumn(string columnName)
        => MotoSyncUpdateSqlBuilder.IsKeyOrScopeColumn(columnName);

    private static bool IsBinaryType(string dataType)
        => dataType is "binary" or "varbinary" or "image" or "timestamp" or "rowversion";

    private static MotoSyncInsertColumnInfo ToInsertColumnInfo(ColumnMetadata column) => new(
        column.Name,
        column.DataType,
        column.IsNullable,
        column.IsIdentity,
        column.IsComputed,
        column.IsRowVersion,
        column.HasDefault);

    private sealed record TableUpdatePlan(
        IReadOnlyList<string> Columns,
        MotoSyncUpdateDetectionResult Detection);

    private sealed record BaoCaoIInsertShape(
        string CourseColumn,
        IReadOnlyList<string> KeyColumns,
        IReadOnlyList<string> CommonColumns,
        IReadOnlyList<ColumnMetadata> SourceColumns,
        IReadOnlyList<ColumnMetadata> TargetColumns);

    private sealed record ColumnMetadata(
        string Name,
        string DataType,
        short MaxLength,
        bool IsNullable,
        bool IsIdentity,
        bool IsComputed,
        bool HasDefault)
    {
        public bool IsRowVersion =>
            string.Equals(DataType, "timestamp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(DataType, "rowversion", StringComparison.OrdinalIgnoreCase);
    }
}

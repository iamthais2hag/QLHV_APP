using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync;

/// <summary>
/// Dựng kế hoạch đồng bộ học viên (dry-run) từ các dòng nguồn đã đọc và tập khóa MaDK đã có ở đích.
/// Hàm THUẦN (pure), không I/O, KHÔNG ghi dữ liệu — an toàn cho dry-run.
/// Phân loại Insert/Update dựa trên tập khóa đích; áp dụng quy tắc dữ liệu qua <see cref="HocVienSyncMapper"/>.
/// </summary>
public static class HocVienSyncPlanner
{
    /// <summary>Số mục preview tối đa đưa vào kế hoạch.</summary>
    public const int MaxPreviewItems = 50;

    public static HocVienSyncPlanDto BuildPlan(
        IReadOnlyList<V2HocVienSourceRow> sourceRows,
        ISet<string> existingTargetKeys,
        HocVienSourceIdentityContext? sourceIdentity = null)
    {
        var existingTargetHashes = existingTargetKeys.ToDictionary(
            key => key,
            _ => string.Empty,
            StringComparer.Ordinal);

        return BuildPlan(sourceRows, existingTargetHashes, sourceIdentity);
    }

    public static HocVienSyncPlanDto BuildPlan(
        IReadOnlyList<V2HocVienSourceRow> sourceRows,
        IReadOnlyDictionary<string, string> existingTargetHashes,
        HocVienSourceIdentityContext? sourceIdentity = null)
    {
        sourceIdentity ??= HocVienSourceIdentityContext.DataV2;
        var warnings = new List<HocVienDataWarningDto>();
        var items = new List<HocVienSyncPlanItemDto>();
        int insert = 0, update = 0, skip = 0;

        foreach (var row in sourceRows)
        {
            var result = HocVienSyncMapper.MapAndValidate(row, sourceIdentity);

            if (result.ShouldSkip || result.Model is null)
            {
                skip++;
                AddPreview(items, new HocVienSyncPlanItemDto
                {
                    MaDK = (row.MaDK ?? string.Empty).Trim(),
                    Action = PlannedSyncAction.Skip,
                    Warnings = result.Warnings,
                });
                warnings.AddRange(result.Warnings);
                continue;
            }

            var sourceKey = HocVienSourceIdentityKey.Create(
                result.Model.SourceProfileCode,
                result.Model.SourceMaDK);
            var action = ResolveAction(result.Model, sourceKey, existingTargetHashes);

            if (action == PlannedSyncAction.Insert)
            {
                insert++;
            }
            else if (action == PlannedSyncAction.Update)
            {
                update++;
            }
            else
            {
                skip++;
            }

            warnings.AddRange(result.Warnings);
            AddPreview(items, new HocVienSyncPlanItemDto
            {
                MaDK = result.Model.MaDK,
                Action = action,
                Warnings = result.Warnings,
            });
        }

        return new HocVienSyncPlanDto
        {
            IsDryRun = true,
            SourceProfileCode = sourceIdentity.SourceProfileCode,
            SourceSystem = sourceIdentity.SourceSystem,
            SourceRowsRead = sourceRows.Count,
            PlannedInsert = insert,
            PlannedUpdate = update,
            PlannedSkip = skip,
            WarningCount = warnings.Count,
            Warnings = warnings,
            Items = items,
        };
    }

    private static PlannedSyncAction ResolveAction(
        HocVienTargetWriteModel model,
        string sourceKey,
        IReadOnlyDictionary<string, string> existingTargetHashes)
    {
        if (!existingTargetHashes.TryGetValue(sourceKey, out var existingHash))
        {
            return PlannedSyncAction.Insert;
        }

        if (string.IsNullOrWhiteSpace(existingHash))
        {
            return string.IsNullOrWhiteSpace(model.V2RowHash)
                ? PlannedSyncAction.Skip
                : PlannedSyncAction.Update;
        }

        return string.Equals(existingHash, model.V2RowHash, StringComparison.OrdinalIgnoreCase)
            ? PlannedSyncAction.Skip
            : PlannedSyncAction.Update;
    }

    private static void AddPreview(List<HocVienSyncPlanItemDto> items, HocVienSyncPlanItemDto item)
    {
        if (items.Count < MaxPreviewItems)
        {
            items.Add(item);
        }
    }
}

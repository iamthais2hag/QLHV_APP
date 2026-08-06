using System.Data;
using System.Globalization;

namespace QLHV.Infrastructure.Sync.Realtime;

internal static class CsdtRealtimeForwardWritePlanner
{
    private static readonly IReadOnlySet<string> LegacyTrainingStates =
        new HashSet<string>(["03", "04", "09"], StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> AtomicTrainingStates =
        new HashSet<string>(
            ["01", "02", "03", "04", "05", "06", "07", "09", "10"],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> LegacyV1LifecycleStates =
        new HashSet<string>(
            ["11", "12", "13", "14", "16", "17", "18", "19"],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> AtomicV1LifecycleStates =
        new HashSet<string>(
            ["00", "11", "12", "13", "14", "16", "17", "18", "19", "90"],
            StringComparer.Ordinal);

    private static readonly string[] V1LifecycleSignalColumns =
    [
        "KetQuaBC2", "SoBD", "LanSH", "SoQDSH", "NgayQDSH",
        "KetQua_LyThuyet", "NhanXet_LyThuyet",
        "KetQuaSHM", "NhanXet_MoPhong", "KetQua_Hinh", "NhanXet_Hinh",
        "KetQua_Duong", "NhanXet_Duong", "SoQDTT", "NgayQDTT", "NguoiKy",
        "SoGPLXTmp", "NgayKTBC2", "NguoiKTBC2", "MaIn", "KetQuaDoiSanhTW",
        "GhiChuKQDSTW", "ChuKy", "CHON_IN_GPLX", "KetQuaPDSo",
        "DAT_QDThucHanh", "DAT_TGThucHanh", "DAT_KQCuc",
        "DAT_ThoiGianLayKQ", "LyDoTuChoiKQDT",
    ];

    internal static CsdtRealtimePlannedSnapshot Plan(
        CsdtRealtimeSnapshot snapshot,
        CsdtRealtimeTableMetadata targetMetadata,
        DataTable targetRows,
        CsdtRealtimeForwardPlanningContext context,
        bool useAtomicMappedTableContract = false)
    {
        var targetByKey = targetRows.Rows
            .Cast<DataRow>()
            .ToDictionary(
                row => CsdtRealtimeTargetWriter.BuildKeyJson(row, targetMetadata),
                StringComparer.Ordinal);
        var planned = snapshot.Rows.Clone();
        var conflicts = new List<CsdtRealtimeConflictRecord>();
        foreach (DataRow source in snapshot.Rows.Rows)
        {
            var keyJson = CsdtRealtimeTargetWriter.BuildKeyJson(
                source,
                snapshot.SourceMetadata);
            targetByKey.TryGetValue(keyJson, out var target);
            var rowPlan = PlanRow(
                snapshot.SourceMetadata.Domain,
                source,
                target,
                context.RelationshipLockedKeys.Contains(keyJson),
                !context.MissingParentKeys.Contains(keyJson),
                useAtomicMappedTableContract);
            if (rowPlan.Include)
            {
                planned.ImportRow(rowPlan.Row);
            }

            if (rowPlan.Conflict is not null)
            {
                conflicts.Add(rowPlan.Conflict);
            }
        }

        return new CsdtRealtimePlannedSnapshot(planned, conflicts);
    }

    internal static CsdtRealtimePlannedRow PlanRow(
        CsdtRealtimeDomainDefinition domain,
        DataRow source,
        DataRow? target,
        bool relationshipLocked = false,
        bool parentExists = true,
        bool useAtomicMappedTableContract = false)
    {
        var policy = CsdtRealtimeColumnOwnershipPolicy.GetRequired(domain.Name);
        var plannedTable = source.Table.Clone();
        plannedTable.ImportRow(source);
        var planned = plannedTable.Rows[0];
        var keyJson = BuildAvailableKeyJson(planned, domain);

        if (!policy.AutomaticWritesEnabled)
        {
            return new CsdtRealtimePlannedRow(
                planned,
                Include: false,
                Conflict(
                    keyJson,
                    target is null ? "GPLX_PROVENANCE_UNCONFIRMED" : "TARGET_GPLX_EXISTS",
                    ["MaDK"],
                    target is null
                        ? "Automatic GPLX insert is disabled until legacy/training provenance is proven."
                        : "Existing V1 GPLX row is authoritative and was preserved."));
        }

        if (target is null)
        {
            if (relationshipLocked && domain.Name is "BaoCaoI" or "KhoaHoc")
            {
                return new CsdtRealtimePlannedRow(
                    planned,
                    Include: false,
                    Conflict(
                        keyJson,
                        "BCI_RELATION_LOCKED",
                        domain.Name == "BaoCaoI" ? ["MaKH", "MaCSDT"] : ["MaKH"],
                        "Insert was blocked because it would attach to an existing V1 BCII lifecycle."));
            }

            if (domain.Name == "NguoiLX_HoSo" &&
                !IsTrainingState(
                    ReadString(source, "TT_XuLy"),
                    useAtomicMappedTableContract))
            {
                return new CsdtRealtimePlannedRow(
                    planned,
                    Include: false,
                    Conflict(
                        keyJson,
                        useAtomicMappedTableContract &&
                        IsKnownDownstreamState(
                            ReadString(source, "TT_XuLy"),
                            useAtomicMappedTableContract)
                            ? "SOURCE_STATE_OUT_OF_SCOPE"
                            : "UNKNOWN_SOURCE_STATE",
                        ["TT_XuLy"],
                        "New dossier was skipped because its source state is not a permitted training state."));
            }

            if (domain.Name == "NguoiLXHS_GiayTo" && !parentExists)
            {
                return new CsdtRealtimePlannedRow(
                    planned,
                    Include: false,
                    Conflict(
                        keyJson,
                        "PARENT_DOSSIER_MISSING",
                        ["MaDK"],
                        "Document row was skipped because its target dossier does not exist."));
            }

            var insertV1Columns = domain.Name == "NguoiLX_HoSo"
                ? FindPopulatedV1Columns(policy, source)
                : [];
            return new CsdtRealtimePlannedRow(
                planned,
                Include: true,
                insertV1Columns.Length == 0
                    ? null
                    : Conflict(
                        keyJson,
                        "V1_OWNED_COLUMN",
                        insertV1Columns,
                        "Source V1-owned lifecycle values were excluded from the new target dossier."));
        }

        var guardMismatch = FindGuardMismatch(domain, source, target);
        if (guardMismatch.Count != 0)
        {
            return new CsdtRealtimePlannedRow(
                planned,
                Include: false,
                Conflict(
                    keyJson,
                    "IDENTITY_COLLISION",
                    guardMismatch,
                    "Target identity exists with different immutable relation fields."));
        }

        if (domain.Name == "KhoaHoc" && relationshipLocked)
        {
            var changed = PreserveChangedColumns(
                planned,
                target,
                policy.Rules.Where(rule => rule.AllowUpdate).Select(rule => rule.Name));
            return new CsdtRealtimePlannedRow(
                planned,
                Include: true,
                changed.Count == 0
                    ? null
                    : Conflict(
                        keyJson,
                        "BCI_RELATION_LOCKED",
                        changed,
                        "Course training fields were preserved because target BCII lifecycle is active."));
        }

        if (domain.Name == "BaoCaoI" && relationshipLocked)
        {
            var changed = PreserveChangedColumns(planned, target, ["MaKH", "MaCSDT"]);
            return new CsdtRealtimePlannedRow(
                planned,
                Include: true,
                changed.Count == 0
                    ? null
                    : Conflict(
                        keyJson,
                        "BCI_RELATION_LOCKED",
                        changed,
                        "BCI relationship columns were preserved because target BCII lifecycle is active."));
        }

        if (domain.Name != "NguoiLX_HoSo")
        {
            return new CsdtRealtimePlannedRow(planned, Include: true, Conflict: null);
        }

        var targetState = ReadString(target, "TT_XuLy");
        if (useAtomicMappedTableContract &&
            !IsTrainingState(targetState, useAtomicMappedTableContract) &&
            !IsKnownDownstreamState(targetState, useAtomicMappedTableContract))
        {
            return new CsdtRealtimePlannedRow(
                planned,
                Include: false,
                Conflict(
                    keyJson,
                    "UNKNOWN_TARGET_STATE",
                    ["TT_XuLy"],
                    "Target dossier state is outside the approved explicit state sets."));
        }

        var targetLifecycleActive = IsV1LifecycleActive(
            target,
            useAtomicMappedTableContract);
        var preserved = new List<string>();
        foreach (var rule in policy.Rules.Where(rule => rule.AllowUpdate))
        {
            if (!planned.Table.Columns.Contains(rule.Name) ||
                !target.Table.Columns.Contains(rule.Name))
            {
                continue;
            }

            var shouldPreserve =
                useAtomicMappedTableContract &&
                rule.Name is "TrangThai" or "MaKhoaHoc" or "MaBC1"
                    ? false
                    : rule.MergeRule switch
            {
                CsdtRealtimeMergeRule.PreserveWhenV1LifecycleActive =>
                    targetLifecycleActive,
                CsdtRealtimeMergeRule.TrainingState =>
                    targetLifecycleActive ||
                    !IsTrainingState(
                        ReadString(source, rule.Name),
                        useAtomicMappedTableContract),
                _ => false,
            };
            shouldPreserve |= !useAtomicMappedTableContract &&
                              relationshipLocked &&
                              rule.Name is "MaBC1" or "MaKhoaHoc";
            if (shouldPreserve && !ValuesEqual(planned[rule.Name], target[rule.Name]))
            {
                planned[rule.Name] = target[rule.Name];
                preserved.Add(rule.Name);
            }
        }

        var populatedV1Columns = FindPopulatedV1Columns(policy, source);
        if (targetLifecycleActive && preserved.Count != 0)
        {
            return new CsdtRealtimePlannedRow(
                planned,
                Include: true,
                Conflict(
                    keyJson,
                    "V1_BCII_LIFECYCLE_ACTIVE",
                    preserved.Concat(populatedV1Columns).Distinct(StringComparer.Ordinal).ToArray(),
                    "V1-owned lifecycle and shared target values were preserved."));
        }

        if (relationshipLocked && preserved.Count != 0)
        {
            return new CsdtRealtimePlannedRow(
                planned,
                Include: true,
                Conflict(
                    keyJson,
                    "BCI_RELATION_LOCKED",
                    preserved,
                    "Dossier BCI/course relationship was preserved because target BCII is linked."));
        }

        var sourceState = ReadString(source, "TT_XuLy");
        if ((!IsTrainingState(sourceState, useAtomicMappedTableContract) &&
             sourceState is not null) ||
            populatedV1Columns.Length != 0)
        {
            var columns = populatedV1Columns
                .Concat(!IsTrainingState(sourceState, useAtomicMappedTableContract) &&
                        sourceState is not null
                    ? ["TT_XuLy"]
                    : [])
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return new CsdtRealtimePlannedRow(
                planned,
                Include: true,
                Conflict(
                    keyJson,
                    useAtomicMappedTableContract &&
                    !IsTrainingState(sourceState, useAtomicMappedTableContract) &&
                    IsKnownDownstreamState(sourceState, useAtomicMappedTableContract)
                        ? "SOURCE_STATE_OUT_OF_SCOPE"
                        : "V1_OWNED_COLUMN",
                    columns,
                    "Source V1-owned lifecycle values were ignored by the forward merge policy."));
        }

        return new CsdtRealtimePlannedRow(planned, Include: true, Conflict: null);
    }

    internal static IReadOnlyDictionary<string, object?> ProjectInsertValues(
        CsdtRealtimeDomainDefinition domain,
        DataRow source)
    {
        var policy = CsdtRealtimeColumnOwnershipPolicy.GetRequired(domain.Name);
        return policy.Rules
            .Where(rule => rule.AllowInsert && source.Table.Columns.Contains(rule.Name))
            .OrderBy(rule => source.Table.Columns[rule.Name]!.Ordinal)
            .ToDictionary(
                rule => rule.Name,
                rule => source[rule.Name] is DBNull ? null : source[rule.Name],
                StringComparer.Ordinal);
    }

    internal static IReadOnlyDictionary<string, object?> ProjectUpdateValues(
        CsdtRealtimeDomainDefinition domain,
        DataRow planned)
    {
        var policy = CsdtRealtimeColumnOwnershipPolicy.GetRequired(domain.Name);
        return policy.Rules
            .Where(rule => rule.AllowUpdate && planned.Table.Columns.Contains(rule.Name))
            .OrderBy(rule => planned.Table.Columns[rule.Name]!.Ordinal)
            .ToDictionary(
                rule => rule.Name,
                rule => planned[rule.Name] is DBNull ? null : planned[rule.Name],
                StringComparer.Ordinal);
    }

    internal static bool IsTrainingState(string? state)
        => IsTrainingState(state, useAtomicMappedTableContract: false);

    internal static bool IsKnownDownstreamState(string? state)
        => IsKnownDownstreamState(state, useAtomicMappedTableContract: false);

    internal static bool IsAtomicTrainingState(string? state)
        => IsTrainingState(state, useAtomicMappedTableContract: true);

    internal static bool IsAtomicKnownDownstreamState(string? state)
        => IsKnownDownstreamState(state, useAtomicMappedTableContract: true);

    internal static bool IsV1LifecycleActive(
        DataRow row,
        bool useAtomicMappedTableContract = false)
    {
        if (!string.IsNullOrWhiteSpace(ReadString(row, "MaBC2")) ||
            !string.IsNullOrWhiteSpace(ReadString(row, "MaKySH")) ||
            !string.IsNullOrWhiteSpace(ReadString(row, "KetQuaSH")) ||
            (useAtomicMappedTableContract
                ? AtomicV1LifecycleStates
                : LegacyV1LifecycleStates)
            .Contains(ReadString(row, "TT_XuLy") ?? string.Empty))
        {
            return true;
        }

        return V1LifecycleSignalColumns.Any(column =>
            row.Table.Columns.Contains(column) && row[column] is not DBNull);
    }

    internal static string BuildV1LifecycleSql(
        string alias,
        bool useAtomicMappedTableContract = false)
    {
        var quotedSignals = V1LifecycleSignalColumns
            .Distinct(StringComparer.Ordinal)
            .Select(column => $"{alias}.{CsdtRealtimeColumnMetadata.Quote(column)} IS NOT NULL");
        var states = useAtomicMappedTableContract
            ? "'00','11','12','13','14','16','17','18','19','90'"
            : "'11','12','13','14','16','17','18','19'";
        return $"""
            (
                NULLIF({alias}.[MaBC2], '') IS NOT NULL
                OR NULLIF({alias}.[MaKySH], '') IS NOT NULL
                OR NULLIF({alias}.[KetQuaSH], '') IS NOT NULL
                OR {alias}.[TT_XuLy] IN ({states})
                OR {string.Join("\n                OR ", quotedSignals)}
            )
            """;
    }

    private static bool IsTrainingState(
        string? state,
        bool useAtomicMappedTableContract)
        => state is not null &&
           (useAtomicMappedTableContract
               ? AtomicTrainingStates
               : LegacyTrainingStates).Contains(state);

    private static bool IsKnownDownstreamState(
        string? state,
        bool useAtomicMappedTableContract)
        => state is not null &&
           (useAtomicMappedTableContract
               ? AtomicV1LifecycleStates
               : LegacyV1LifecycleStates).Contains(state);

    private static IReadOnlyList<string> FindGuardMismatch(
        CsdtRealtimeDomainDefinition domain,
        DataRow source,
        DataRow target)
    {
        if (domain.IdentityCollisionGuardColumns is null)
        {
            return [];
        }

        return domain.IdentityCollisionGuardColumns
            .Where(column =>
                source.Table.Columns.Contains(column) &&
                target.Table.Columns.Contains(column) &&
                !ValuesEqual(source[column], target[column]))
            .ToArray();
    }

    private static IReadOnlyList<string> PreserveChangedColumns(
        DataRow planned,
        DataRow target,
        IEnumerable<string> columns)
    {
        var changed = new List<string>();
        foreach (var column in columns.Distinct(StringComparer.Ordinal))
        {
            if (!planned.Table.Columns.Contains(column) ||
                !target.Table.Columns.Contains(column) ||
                ValuesEqual(planned[column], target[column]))
            {
                continue;
            }

            planned[column] = target[column];
            changed.Add(column);
        }

        return changed;
    }

    private static CsdtRealtimeConflictRecord Conflict(
        string keyJson,
        string code,
        IReadOnlyList<string> columns,
        string message)
        => new(
            keyJson,
            code,
            message,
            columns.Order(StringComparer.Ordinal).ToArray());

    private static string BuildAvailableKeyJson(
        DataRow row,
        CsdtRealtimeDomainDefinition domain)
    {
        var key = domain.KeyColumns.ToDictionary(
            column => column,
            column =>
                row.Table.Columns.Contains(column) && row[column] is not DBNull
                    ? row[column]
                    : null,
            StringComparer.Ordinal);
        return System.Text.Json.JsonSerializer.Serialize(
            key.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    private static string? ReadString(DataRow row, string column)
        => row.Table.Columns.Contains(column) && row[column] is not DBNull
            ? Convert.ToString(row[column], CultureInfo.InvariantCulture)
            : null;

    private static string[] FindPopulatedV1Columns(
        CsdtRealtimeDomainColumnPolicy policy,
        DataRow source)
        => policy.Rules
            .Where(rule =>
                rule.Owner == CsdtRealtimeColumnOwner.V1 &&
                source.Table.Columns.Contains(rule.Name) &&
                source[rule.Name] is not DBNull)
            .Select(rule => rule.Name)
            .ToArray();

    private static bool ValuesEqual(object left, object right)
    {
        if (left is DBNull || right is DBNull)
        {
            return left is DBNull && right is DBNull;
        }

        if (left is byte[] leftBytes && right is byte[] rightBytes)
        {
            return leftBytes.AsSpan().SequenceEqual(rightBytes);
        }

        return Equals(left, right);
    }
}

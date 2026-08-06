using System.Security.Cryptography;
using System.Text.Json;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IQlhvFreshnessSourceRepository
{
    Task<QlhvImportSourceSnapshot> ReadLiveSourceAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken = default);

    Task<QlhvImportSourceSnapshot> ReadBackupSourceAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken = default);
}

public interface IQlhvPartitionSyncStateRepository
{
    Task<QlhvPartitionSyncState?> GetAsync(
        string sourceType,
        CancellationToken cancellationToken = default);
}

public interface IQlhvSyncFreshnessService
{
    Task<QlhvSyncFreshnessResult> EvaluateAsync(
        CancellationToken cancellationToken = default);
}

public sealed class QlhvPartitionSyncState
{
    public string SourceType { get; init; } = string.Empty;

    public string SourceProfileCode { get; init; } = string.Empty;

    public string AppliedBackupSnapshotToken { get; init; } = string.Empty;

    public int HocVienRows { get; init; }

    public int KhoaHocRows { get; init; }

    public int GiaoVienRows { get; init; }

    public int KhoaHocGiaoVienRows { get; init; }

    public DateTime AppliedAtUtc { get; init; }
}

public sealed class QlhvSyncFreshnessResult
{
    public bool NeedSync { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<QlhvSyncSnapshotDto> LiveSnapshots { get; init; } =
        Array.Empty<QlhvSyncSnapshotDto>();

    public IReadOnlyList<QlhvSyncSnapshotDto> BackupSnapshots { get; init; } =
        Array.Empty<QlhvSyncSnapshotDto>();

    public IReadOnlyList<QlhvPartitionFreshnessDto> Partitions { get; init; } =
        Array.Empty<QlhvPartitionFreshnessDto>();
}

public sealed class QlhvSyncFreshnessService : IQlhvSyncFreshnessService
{
    private readonly IQlhvFreshnessSourceRepository _sources;
    private readonly IQlhvPartitionSyncStateRepository _partitionState;
    private readonly IQlhvImportService _import;

    public QlhvSyncFreshnessService(
        IQlhvFreshnessSourceRepository sources,
        IQlhvPartitionSyncStateRepository partitionState,
        IQlhvImportService import)
    {
        _sources = sources;
        _partitionState = partitionState;
        _import = import;
    }

    public async Task<QlhvSyncFreshnessResult> EvaluateAsync(
        CancellationToken cancellationToken = default)
    {
        var reasons = new List<string>();
        var blockers = new List<string>();
        var liveSnapshots = new List<QlhvSyncSnapshotDto>();
        var backupSnapshots = new List<QlhvSyncSnapshotDto>();
        var partitions = new List<QlhvPartitionFreshnessDto>();

        foreach (var source in QlhvOperationSourceCatalog.All.OrderBy(
                     item => string.Equals(item.SourceType, "OTO", StringComparison.Ordinal) ? 0 : 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var request = new QlhvImportRequest
                {
                    SourceProfileCode = source.SourceProfileCode,
                    MaCSDT = source.MaCsdt,
                };
                var liveSource = await _sources.ReadLiveSourceAsync(request, cancellationToken);
                var backupSource = await _sources.ReadBackupSourceAsync(request, cancellationToken);
                var live = CreateSnapshot(source.SourceType, liveSource, backup: false);
                var backup = CreateSnapshot(source.SourceType, backupSource, backup: true);
                liveSnapshots.Add(live);
                backupSnapshots.Add(backup);

                var state = await _partitionState.GetAsync(source.SourceType, cancellationToken);
                var plan = await _import.GetPlanAsync(request, cancellationToken);
                var partitionReasons = EvaluatePartition(live, backup, state, plan);
                reasons.AddRange(partitionReasons);
                if (plan.Blockers.Count > 0)
                {
                    blockers.AddRange(plan.Blockers.Select(
                        blocker => $"{source.SourceType}: {blocker}"));
                }

                partitions.Add(new QlhvPartitionFreshnessDto
                {
                    SourceType = source.SourceType,
                    SourceProfileCode = source.SourceProfileCode,
                    IsConsistent = partitionReasons.Count == 0 && plan.Blockers.Count == 0,
                    AppliedBackupSnapshotToken = state?.AppliedBackupSnapshotToken,
                    AppliedRows = state is null
                        ? new QlhvSyncEntityCountsDto()
                        : new QlhvSyncEntityCountsDto
                        {
                            HocVien = state.HocVienRows,
                            KhoaHoc = state.KhoaHocRows,
                            GiaoVien = state.GiaoVienRows,
                            KhoaHocGiaoVien = state.KhoaHocGiaoVienRows,
                        },
                    AppliedAtUtc = state?.AppliedAtUtc,
                    Reasons = partitionReasons,
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var safeReason =
                    $"{source.SourceType}:KHONG_DOC_DUOC_SNAPSHOT_{ex.GetType().Name.ToUpperInvariant()}";
                reasons.Add(safeReason);
                blockers.Add(
                    $"{source.SourceType}: khong doc duoc snapshot Live/BAK/QLHV_APP an toan.");
                partitions.Add(new QlhvPartitionFreshnessDto
                {
                    SourceType = source.SourceType,
                    SourceProfileCode = source.SourceProfileCode,
                    Reasons = [safeReason],
                });
            }
        }

        var normalizedReasons = reasons.Distinct(StringComparer.Ordinal).ToArray();
        return new QlhvSyncFreshnessResult
        {
            NeedSync = normalizedReasons.Length > 0,
            Reasons = normalizedReasons,
            Blockers = blockers.Distinct(StringComparer.Ordinal).ToArray(),
            LiveSnapshots = liveSnapshots,
            BackupSnapshots = backupSnapshots,
            Partitions = partitions,
        };
    }

    private static IReadOnlyList<string> EvaluatePartition(
        QlhvSyncSnapshotDto live,
        QlhvSyncSnapshotDto backup,
        QlhvPartitionSyncState? state,
        QlhvImportPlanDto plan)
    {
        var sourceType = live.SourceType;
        var reasons = new List<string>();
        if (!string.Equals(live.ContentToken, backup.ContentToken, StringComparison.Ordinal))
        {
            reasons.Add($"{sourceType}:LIVE_KHAC_BAK");
        }

        if (!CountsEqual(live.Rows, backup.Rows))
        {
            reasons.Add($"{sourceType}:SO_LUONG_LIVE_KHAC_BAK");
        }

        if (string.IsNullOrWhiteSpace(backup.BackupSnapshotToken))
        {
            reasons.Add($"{sourceType}:BAK_CHUA_CO_SNAPSHOT_TOKEN");
        }

        if (state is null)
        {
            reasons.Add($"{sourceType}:CHUA_TUNG_DONG_BO_THANH_CONG");
        }
        else
        {
            if (!string.Equals(
                    state.AppliedBackupSnapshotToken,
                    backup.BackupSnapshotToken,
                    StringComparison.Ordinal))
            {
                reasons.Add($"{sourceType}:BAK_KHAC_TOKEN_DA_AP_DUNG");
            }

            var applied = new QlhvSyncEntityCountsDto
            {
                HocVien = state.HocVienRows,
                KhoaHoc = state.KhoaHocRows,
                GiaoVien = state.GiaoVienRows,
                KhoaHocGiaoVien = state.KhoaHocGiaoVienRows,
            };
            if (!CountsEqual(applied, backup.Rows, plan.ExecutableDomains))
            {
                reasons.Add($"{sourceType}:SO_LUONG_DA_AP_DUNG_KHONG_KHOP_BAK");
            }
        }

        if (plan.Blockers.Count > 0 || plan.HocVienBlockers.Count > 0)
        {
            reasons.Add($"{sourceType}:PLAN_CO_BLOCKER");
        }
        else if (HasPlannedChanges(plan))
        {
            reasons.Add($"{sourceType}:QLHV_APP_CHUA_KHOP_BAK");
        }

        return reasons.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool HasPlannedChanges(QlhvImportPlanDto plan)
        => HasChanges(plan.HocVien) ||
           (plan.ExecutableDomains.Contains(QlhvImportDomains.KhoaHoc, StringComparer.Ordinal) &&
            HasChanges(plan.KhoaHoc)) ||
           (plan.ExecutableDomains.Contains(QlhvImportDomains.GiaoVien, StringComparer.Ordinal) &&
            HasChanges(plan.GiaoVien)) ||
           (plan.ExecutableDomains.Contains(QlhvImportDomains.Relation, StringComparer.Ordinal) &&
            HasChanges(plan.KhoaHocGiaoVien));

    private static bool HasChanges(QlhvEntitySyncCountsDto counts)
        => counts.Insert > 0 ||
           counts.Update > 0 ||
           counts.Reactivate > 0 ||
           counts.SoftDelete > 0;

    private static bool CountsEqual(
        QlhvSyncEntityCountsDto left,
        QlhvSyncEntityCountsDto right)
        => CountsEqual(left, right, QlhvImportDomains.Ordered);

    private static bool CountsEqual(
        QlhvSyncEntityCountsDto left,
        QlhvSyncEntityCountsDto right,
        IReadOnlyCollection<string> executableDomains)
        => left.HocVien == right.HocVien &&
           (!executableDomains.Contains(QlhvImportDomains.KhoaHoc, StringComparer.Ordinal) ||
            left.KhoaHoc == right.KhoaHoc) &&
           (!executableDomains.Contains(QlhvImportDomains.GiaoVien, StringComparer.Ordinal) ||
            left.GiaoVien == right.GiaoVien) &&
           (!executableDomains.Contains(QlhvImportDomains.Relation, StringComparer.Ordinal) ||
            left.KhoaHocGiaoVien == right.KhoaHocGiaoVien);

    private static QlhvSyncSnapshotDto CreateSnapshot(
        string sourceType,
        QlhvImportSourceSnapshot source,
        bool backup)
    {
        var canonical = new
        {
            HocVien = Canonicalize(source.HocVienRows),
            KhoaHoc = Canonicalize(source.KhoaHocSourceRows),
            GiaoVien = Canonicalize(source.GiaoVienRows),
            KhoaHocGiaoVien = Canonicalize(source.KhoaHocGiaoVienRows),
        };
        var contentToken = Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical)));

        return new QlhvSyncSnapshotDto
        {
            SourceType = sourceType,
            DatabaseName = source.SourceDatabaseName,
            GeneratedAtUtc = source.GeneratedAtUtc,
            ContentToken = contentToken,
            BackupSnapshotToken = backup ? source.BackupSnapshotToken : null,
            Rows = new QlhvSyncEntityCountsDto
            {
                HocVien = source.HocVienRows.Count,
                KhoaHoc = source.KhoaHocSourceRows.Count,
                GiaoVien = source.GiaoVienRows.Count,
                KhoaHocGiaoVien = source.KhoaHocGiaoVienRows.Count,
            },
        };
    }

    private static IReadOnlyList<string> Canonicalize<T>(IEnumerable<T> rows)
        => rows
            .Select(row => JsonSerializer.Serialize(row))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
}

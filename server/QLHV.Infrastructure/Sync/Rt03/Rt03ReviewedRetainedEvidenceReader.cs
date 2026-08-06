using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Mapping;
using QLHV.Application.Sync.Rt01;
using QLHV.Application.Sync.Rt03;

namespace QLHV.Infrastructure.Sync.Rt03;

public sealed class Rt03ReviewedRetainedEvidenceReader
{
    private readonly SyncOptions _syncOptions;

    public Rt03ReviewedRetainedEvidenceReader(IOptions<SyncOptions> syncOptions)
    {
        _syncOptions = syncOptions.Value;
    }

    public async Task<Rt03ReviewedRetainedSummary> EvaluateNoChangeAsync(
        SqlConnection targetConnection,
        string sourceConnectionString,
        string sourceProfileCode,
        Rt01aRawProbe raw,
        Rt01aProbeEvidence drift,
        long checkpointVersion,
        long sourceCurrentVersion,
        long sourceMinimumValidVersion,
        CancellationToken cancellationToken)
        => await EvaluateAsync(
            targetConnection,
            sourceConnectionString,
            sourceProfileCode,
            raw,
            drift,
            checkpointVersion,
            sourceCurrentVersion,
            sourceMinimumValidVersion,
            Rt03ReviewedRetainedContext.NoChangeCycle,
            cancellationToken);

    public async Task<Rt03ReviewedRetainedSummary> EvaluateAsync(
        SqlConnection targetConnection,
        string sourceConnectionString,
        string sourceProfileCode,
        Rt01aRawProbe raw,
        Rt01aProbeEvidence drift,
        long checkpointVersion,
        long sourceCurrentVersion,
        long sourceMinimumValidVersion,
        Rt03ReviewedRetainedContext context,
        CancellationToken cancellationToken)
    {
        var reviews = (await targetConnection.QueryAsync<ReviewRow>(new CommandDefinition(
            ActiveReviewSql,
            new
            {
                SourceProfileCode = sourceProfileCode,
                ContractVersion = Rt03ReviewedRetainedContract.Version,
                ActiveState = Rt03ReviewedRetainedContract.ActiveState,
            },
            commandTimeout: _syncOptions.TimeoutSeconds,
            cancellationToken: cancellationToken))).ToArray();

        var evaluations = new List<Rt03ReviewedRetainedEvaluation>();
        var safeBusinessIdentities = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var reviewGroup in reviews.GroupBy(
                     row => row.SourceBusinessIdentityHash,
                     StringComparer.OrdinalIgnoreCase))
        {
            var group = reviewGroup.ToArray();
            var review = group.OrderByDescending(item => item.CreatedAtUtc).First();
            var sourceRows = raw.MappedSourceRows.Where(row =>
                string.Equals(
                    Rt03ReviewedRetainedFingerprints.SourceBusinessIdentity(
                        sourceProfileCode, row.SourceMaDK),
                    review.SourceBusinessIdentityHash,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            var targetRows = raw.TargetRows.Where(row =>
                !row.IsDeleted &&
                string.Equals(row.SourceProfileCode, sourceProfileCode, StringComparison.Ordinal) &&
                string.Equals(
                    Rt03ReviewedRetainedFingerprints.SourceBusinessIdentity(
                        sourceProfileCode, row.SourceMaDK ?? string.Empty),
                    review.SourceBusinessIdentityHash,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            var source = sourceRows.SingleOrDefault();
            var target = targetRows.SingleOrDefault();
            var currentFieldSet = source is null || target is null
                ? string.Empty
                : Rt03ReviewedRetainedFingerprints.CurrentFieldSet(source, target);
            var hasNewSourceEvent = await HasNewSourceEventAsync(
                sourceConnectionString,
                source?.SourceMaDK,
                review.EvidenceAnchorVersion,
                sourceCurrentVersion,
                sourceMinimumValidVersion,
                cancellationToken);
            var exactReviewedDrift = source is not null && target is not null &&
                !string.IsNullOrWhiteSpace(currentFieldSet) &&
                string.Equals(
                    currentFieldSet,
                    Rt03ReviewedRetainedPolicy.NormalizeFieldSet(review.ReviewedFieldSet),
                    StringComparison.Ordinal);
            var input = new Rt03ReviewedRetainedInput
            {
                SourceProfileCode = sourceProfileCode,
                DomainCode = review.DomainCode,
                SourceBusinessIdentityHash = review.SourceBusinessIdentityHash,
                TargetIdentity = target?.HocVienId,
                DriftClassification = review.Classification,
                ReviewedFieldSet = review.ReviewedFieldSet,
                CurrentFieldSet = currentFieldSet,
                SourceVersion = sourceCurrentVersion,
                ReviewVersion = review.ReviewedEventVersion,
                CheckpointVersion = checkpointVersion,
                SourceIdentityCount = sourceRows.Length,
                LiveTargetIdentityCount = targetRows.Length,
                ActiveReviewCount = group.Length,
                MarkerCheckpointAtomic = review.MarkerCheckpointAtomic,
                TargetRetainedActive = review.TargetRetainedActive,
                TargetMutated = review.TargetMutated,
                ReviewIsActive = string.Equals(
                    review.ReviewState,
                    Rt03ReviewedRetainedContract.ActiveState,
                    StringComparison.Ordinal),
                HasNewSourceEvent = hasNewSourceEvent,
                HasNewDriftOutsideReviewedFields = !exactReviewedDrift,
                ReviewedSourceFingerprint = review.SourceFingerprint,
                CurrentSourceFingerprint = source is null
                    ? string.Empty
                    : Rt03ReviewedRetainedFingerprints.Source(source),
                ReviewedTargetFingerprint = review.TargetFingerprint,
                CurrentTargetFingerprint = target is null
                    ? string.Empty
                    : Rt03ReviewedRetainedFingerprints.Target(target),
                ReviewedOwnershipFingerprint = review.QlhvOwnedFingerprint,
                CurrentOwnershipFingerprint = target is null
                    ? string.Empty
                    : Rt03ReviewedRetainedFingerprints.Ownership(target),
                EvidenceContractVersion = review.EvidenceContractVersion,
            };
            var evaluation = Rt03ReviewedRetainedPolicy.Evaluate(input, context);
            evaluations.Add(evaluation);
            if (evaluation.IsSafeSteadyState)
            {
                safeBusinessIdentities.Add(review.SourceBusinessIdentityHash);
            }
        }

        var safe = evaluations.Where(item => item.IsSafeSteadyState).ToArray();
        var newDrift = Math.Max(0, drift.WouldUpdateRows - safe.Length);
        DateTime? oldest = reviews.Length == 0
            ? null
            : reviews.Min(item => item.CreatedAtUtc);
        DateTime? newest = reviews.Length == 0
            ? null
            : reviews.Max(item => item.CreatedAtUtc);
        var outcome = safe.Length > 0 && newDrift == 0 &&
                      safe.Length == drift.WouldUpdateRows
            ? Rt03CycleStatuses.HealthyReviewedRetained
            : Rt03CycleStatuses.HealthyNoChange;
        return new Rt03ReviewedRetainedSummary(
            safe.Length,
            safe.Length == 0 ? [] : [Rt03ReviewedRetainedContract.DomainLearner],
            reviews.Length,
            evaluations.Count(item => !item.IsSafeSteadyState),
            newDrift,
            oldest,
            newest,
            outcome,
            evaluations)
        {
            SafeSourceBusinessIdentityHashes = safeBusinessIdentities,
        };
    }

    private async Task<bool> HasNewSourceEventAsync(
        string sourceConnectionString,
        string? sourceMaDk,
        long evidenceAnchorVersion,
        long sourceCurrentVersion,
        long sourceMinimumValidVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceMaDk))
        {
            return true;
        }

        if (sourceCurrentVersion <= evidenceAnchorVersion)
        {
            return false;
        }

        if (evidenceAnchorVersion < sourceMinimumValidVersion)
        {
            return true;
        }

        await using var connection = new SqlConnection(sourceConnectionString);
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            NewSourceEventSql,
            new
            {
                EvidenceAnchorVersion = evidenceAnchorVersion,
                SourceCurrentVersion = sourceCurrentVersion,
                SourceMaDk = sourceMaDk,
            },
            commandTimeout: _syncOptions.TimeoutSeconds,
            cancellationToken: cancellationToken)) != 0;
    }

    private sealed class ReviewRow
    {
        public long ManualReviewId { get; init; }
        public string EvidenceContractVersion { get; init; } = string.Empty;
        public string DomainCode { get; init; } = string.Empty;
        public string SourceBusinessIdentityHash { get; init; } = string.Empty;
        public long TargetIdentity { get; init; }
        public string Classification { get; init; } = string.Empty;
        public string ReviewedFieldSet { get; init; } = string.Empty;
        public long ReviewedEventVersion { get; init; }
        public long EvidenceAnchorVersion { get; init; }
        public string SourceFingerprint { get; init; } = string.Empty;
        public string TargetFingerprint { get; init; } = string.Empty;
        public string QlhvOwnedFingerprint { get; init; } = string.Empty;
        public string ReviewState { get; init; } = string.Empty;
        public bool TargetRetainedActive { get; init; }
        public bool TargetMutated { get; init; }
        public bool MarkerCheckpointAtomic { get; init; }
        public DateTime CreatedAtUtc { get; init; }
    }

    internal const string ActiveReviewSql = """
        SELECT review.ManualReviewId, review.EvidenceContractVersion,
               review.DomainCode, review.SourceBusinessIdentityHash,
               review.TargetIdentity, review.Classification,
               review.ReviewedFieldSet, review.ReviewedEventVersion,
               review.EvidenceAnchorVersion, review.SourceFingerprint,
               review.TargetFingerprint, review.QlhvOwnedFingerprint,
               review.ReviewState, review.TargetRetainedActive,
               review.TargetMutated, review.CreatedAtUtc,
               CONVERT(bit, CASE WHEN marker.CycleId IS NOT NULL THEN 1 ELSE 0 END)
                   AS MarkerCheckpointAtomic
        FROM dbo.App_QlhvDirectRealtimeManualReview review
        LEFT JOIN dbo.App_QlhvDirectRealtimeApplyMarker marker
          ON marker.CycleId=review.CycleId
         AND marker.SourceProfileCode=review.SourceProfileCode
         AND marker.PlanHash=review.PlanHash
         AND marker.SourceChangeTrackingVersion=review.ReviewedEventVersion
        WHERE review.SourceProfileCode=@SourceProfileCode
          AND review.EvidenceContractVersion=@ContractVersion
          AND review.ReviewState=@ActiveState
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.App_QlhvDirectRealtimeManualReview successor
              WHERE successor.EvidenceContractVersion=@ContractVersion
                AND successor.SupersedesManualReviewId=review.ManualReviewId
          )
        ORDER BY review.ManualReviewId;
        """;

    private const string NewSourceEventSql = """
        SELECT CONVERT(int, CASE WHEN EXISTS
        (
            SELECT 1
            FROM CHANGETABLE(CHANGES dbo.NguoiLX, @EvidenceAnchorVersion) changeRow
            WHERE changeRow.SYS_CHANGE_VERSION<=@SourceCurrentVersion
              AND changeRow.MaDK=@SourceMaDk
            UNION ALL
            SELECT 1
            FROM CHANGETABLE(CHANGES dbo.NguoiLX_HoSo, @EvidenceAnchorVersion) changeRow
            WHERE changeRow.SYS_CHANGE_VERSION<=@SourceCurrentVersion
              AND changeRow.MaDK=@SourceMaDk
        ) THEN 1 ELSE 0 END);
        """;
}

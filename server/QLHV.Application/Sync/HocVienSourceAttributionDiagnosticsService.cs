using QLHV.Application.CsdtConnections;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public sealed class HocVienSourceAttributionDiagnosticsService : IHocVienSourceAttributionDiagnosticsService
{
    private const string RecommendationDataV1 = "DATA_V1";
    private const string RecommendationDataV2 = "DATA_V2";
    private const string RecommendationAmbiguous = "Ambiguous";
    private const string RecommendationCannotDetermine = "CannotDetermine";

    private readonly IHocVienSourceAttributionDiagnosticsRepository _repository;

    public HocVienSourceAttributionDiagnosticsService(
        IHocVienSourceAttributionDiagnosticsRepository repository)
    {
        _repository = repository;
    }

    public async Task<HocVienSourceAttributionDiagnosticsResultDto> GetDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var targetRows = await _repository.ReadTargetKeysAsync(cancellationToken);
            var dataV1 = await _repository.ReadSourceKeysAsync(
                CsdtConnectionProfileCodes.DataV1,
                cancellationToken);
            var dataV2 = await _repository.ReadSourceKeysAsync(
                CsdtConnectionProfileCodes.DataV2,
                cancellationToken);

            var issues = new List<string>();
            var errors = new List<SyncErrorDto>();
            AddSourceReadIssue(dataV1, issues, errors);
            AddSourceReadIssue(dataV2, issues, errors);

            var dataV1Keys = dataV1.CanRead
                ? new HashSet<string>(dataV1.MaDks, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dataV2Keys = dataV2.CanRead
                ? new HashSet<string>(dataV2.MaDks, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var matchedDataV1 = 0;
            var matchedDataV2 = 0;
            var matchedBoth = 0;
            var matchedNeither = 0;

            foreach (var target in targetRows)
            {
                var inDataV1 = dataV1Keys.Contains(target.MaDK);
                var inDataV2 = dataV2Keys.Contains(target.MaDK);

                if (inDataV1)
                {
                    matchedDataV1++;
                }

                if (inDataV2)
                {
                    matchedDataV2++;
                }

                if (inDataV1 && inDataV2)
                {
                    matchedBoth++;
                }
                else if (!inDataV1 && !inDataV2)
                {
                    matchedNeither++;
                }
            }

            var canRead = dataV1.CanRead && dataV2.CanRead;
            var recommendation = canRead
                ? Recommend(targetRows.Count, matchedDataV1, matchedDataV2, matchedBoth, matchedNeither)
                : RecommendationCannotDetermine;

            if (targetRows.Count == 0)
            {
                issues.Add("Khong co dong App_HocVien dang hoat dong de xac dinh nguon.");
            }

            if (matchedBoth > 0)
            {
                issues.Add("Co MaDK khop ca DATA_V1 va DATA_V2; can xac nhan thu cong truoc khi backfill.");
            }

            if (matchedNeither > 0)
            {
                issues.Add("Co App_HocVien khong khop DATA_V1/DATA_V2 theo MaDK; khong the tu dong ket luan toan bo.");
            }

            return new HocVienSourceAttributionDiagnosticsResultDto
            {
                CanRead = canRead,
                Status = canRead ? "SanSang" : "LoiDocNguon",
                Issues = issues,
                Errors = errors,
                Diagnostics = new HocVienSourceAttributionDiagnosticsDto
                {
                    CheckedAtUtc = DateTime.UtcNow,
                    TargetRows = targetRows.Count,
                    TargetRowsWithSourceProfileCode = targetRows.Count(HasSourceProfile),
                    TargetRowsWithoutSourceProfileCode = targetRows.Count(row => !HasSourceProfile(row)),
                    MatchedWithDataV1ByMaDk = matchedDataV1,
                    MatchedWithDataV2ByMaDk = matchedDataV2,
                    MatchedBoth = matchedBoth,
                    MatchedNeither = matchedNeither,
                    Recommendation = recommendation,
                    SourceProfiles = new[]
                    {
                        ToSourceProfileAttribution(dataV1, matchedDataV1),
                        ToSourceProfileAttribution(dataV2, matchedDataV2),
                    },
                    TargetSourceProfileDistribution = BuildTargetDistribution(targetRows),
                },
            };
        }
        catch (Exception ex)
        {
            const string issue = "Khong doc duoc thong ke gan nguon App_HocVien.";
            return new HocVienSourceAttributionDiagnosticsResultDto
            {
                CanRead = false,
                Status = "LoiDocDich",
                Issues = new[] { issue },
                Errors = new[]
                {
                    new SyncErrorDto
                    {
                        Code = "SOURCE_ATTRIBUTION_DIAGNOSTICS_FAILED",
                        Message = $"{issue} Chi tiet: {ex.GetType().Name}.",
                    },
                },
            };
        }
    }

    private static bool HasSourceProfile(HocVienTargetAttributionKeyDto row)
        => !string.IsNullOrWhiteSpace(row.SourceProfileCode);

    private static HocVienSourceProfileAttributionDto ToSourceProfileAttribution(
        HocVienSourceMaDkReadResultDto source,
        int matchedTargetRows) => new()
    {
        SourceProfileCode = source.SourceProfileCode,
        CanRead = source.CanRead,
        SourceRows = source.MaDks.Count,
        MatchedTargetRowsByMaDk = matchedTargetRows,
        Issue = source.Issue,
    };

    private static IReadOnlyList<HocVienTargetSourceProfileDistributionDto> BuildTargetDistribution(
        IReadOnlyList<HocVienTargetAttributionKeyDto> targetRows)
        => targetRows
            .GroupBy(
                row => string.IsNullOrWhiteSpace(row.SourceProfileCode)
                    ? "null-empty"
                    : row.SourceProfileCode.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new HocVienTargetSourceProfileDistributionDto
            {
                SourceProfileCode = group.Key,
                Total = group.Count(),
            })
            .ToList();

    private static void AddSourceReadIssue(
        HocVienSourceMaDkReadResultDto source,
        ICollection<string> issues,
        ICollection<SyncErrorDto> errors)
    {
        if (source.CanRead)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(source.Issue))
        {
            issues.Add(source.Issue);
        }

        if (source.Error is not null)
        {
            errors.Add(source.Error);
        }
    }

    private static string Recommend(
        int targetRows,
        int matchedDataV1,
        int matchedDataV2,
        int matchedBoth,
        int matchedNeither)
    {
        if (targetRows <= 0 || matchedNeither > 0)
        {
            return RecommendationCannotDetermine;
        }

        if (matchedBoth > 0)
        {
            return RecommendationAmbiguous;
        }

        if (matchedDataV1 == targetRows && matchedDataV2 == 0)
        {
            return RecommendationDataV1;
        }

        if (matchedDataV2 == targetRows && matchedDataV1 == 0)
        {
            return RecommendationDataV2;
        }

        return RecommendationAmbiguous;
    }
}

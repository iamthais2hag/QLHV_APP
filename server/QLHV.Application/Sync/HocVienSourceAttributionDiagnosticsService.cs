using QLHV.Application.CsdtConnections;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public sealed class HocVienSourceAttributionDiagnosticsService : IHocVienSourceAttributionDiagnosticsService
{
    private const string RecommendationDataV1 = "DATA_V1";
    private const string RecommendationDataV2 = "DATA_V2";
    private const string RecommendationAmbiguous = "Ambiguous";
    private const string RecommendationCannotDetermine = "CannotDetermine";
    private const string ConfidenceLow = "Low";
    private const string ConfidenceMedium = "Medium";
    private const string ConfidenceHigh = "High";

    private static readonly string[] ComparedFields =
    {
        "HoTen",
        "NgaySinh",
        "GioiTinh",
        "MaKhoa",
        "TenKhoa",
        "MaHangDT",
        "HangGPLXHoc",
    };

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
            var targetRows = await _repository.ReadTargetRowsAsync(cancellationToken);
            var dataV1 = await _repository.ReadSourceRowsAsync(
                CsdtConnectionProfileCodes.DataV1,
                cancellationToken);
            var dataV2 = await _repository.ReadSourceRowsAsync(
                CsdtConnectionProfileCodes.DataV2,
                cancellationToken);

            var issues = new List<string>();
            var errors = new List<SyncErrorDto>();
            AddSourceReadIssue(dataV1, issues, errors);
            AddSourceReadIssue(dataV2, issues, errors);

            var dataV1Keys = dataV1.CanRead
                ? new HashSet<string>(dataV1.Rows.Select(row => row.MaDK), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dataV2Keys = dataV2.CanRead
                ? new HashSet<string>(dataV2.Rows.Select(row => row.MaDK), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dataV1Map = BuildMap(dataV1.Rows);
            var dataV2Map = BuildMap(dataV2.Rows);

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

            var dataV1Metrics = BuildSourceMetrics(targetRows, dataV1Map);
            var dataV2Metrics = BuildSourceMetrics(targetRows, dataV2Map);
            var canRead = dataV1.CanRead && dataV2.CanRead;
            var recommendation = canRead
                ? Recommend(
                    targetRows.Count,
                    dataV1.DistinctSourceMaDk,
                    dataV2.DistinctSourceMaDk,
                    dataV1Metrics.StrongerMatch,
                    dataV2Metrics.StrongerMatch)
                : new RecommendationResult(RecommendationCannotDetermine, ConfidenceLow);

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

            if (recommendation.Recommendation != RecommendationCannotDetermine &&
                recommendation.Confidence != ConfidenceHigh)
            {
                issues.Add("Recommendation chua dat confidence High; khong du dieu kien de backfill tu dong.");
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
                    DataV1SourceRows = dataV1.SourceRows,
                    DataV2SourceRows = dataV2.SourceRows,
                    DataV1DistinctSourceMaDk = dataV1.DistinctSourceMaDk,
                    DataV2DistinctSourceMaDk = dataV2.DistinctSourceMaDk,
                    DataV1DuplicateSourceMaDkCount = dataV1.DuplicateSourceMaDkCount,
                    DataV2DuplicateSourceMaDkCount = dataV2.DuplicateSourceMaDkCount,
                    DataV1InvalidNgaySinhCount = dataV1.InvalidNgaySinhCount,
                    DataV2InvalidNgaySinhCount = dataV2.InvalidNgaySinhCount,
                    MatchedByMaDkDataV1 = matchedDataV1,
                    MatchedByMaDkDataV2 = matchedDataV2,
                    ExactFieldMatchDataV1 = dataV1Metrics.ExactFieldMatch,
                    ExactFieldMatchDataV2 = dataV2Metrics.ExactFieldMatch,
                    V2RowHashMatchDataV1 = dataV1Metrics.V2RowHashMatch,
                    V2RowHashMatchDataV2 = dataV2Metrics.V2RowHashMatch,
                    StrongerMatchDataV1 = dataV1Metrics.StrongerMatch,
                    StrongerMatchDataV2 = dataV2Metrics.StrongerMatch,
                    DataV2OnlyMaDkCount = dataV2Keys.Except(dataV1Keys, StringComparer.OrdinalIgnoreCase).Count(),
                    DataV1OnlyMaDkCount = dataV1Keys.Except(dataV2Keys, StringComparer.OrdinalIgnoreCase).Count(),
                    OverlappingMaDkCount = dataV1Keys.Intersect(dataV2Keys, StringComparer.OrdinalIgnoreCase).Count(),
                    MatchedBoth = matchedBoth,
                    MatchedNeither = matchedNeither,
                    Recommendation = recommendation.Recommendation,
                    Confidence = recommendation.Confidence,
                    ChangedFieldSummary = BuildChangedFieldSummary(dataV1Metrics, dataV2Metrics),
                    SourceProfiles = new[]
                    {
                        ToSourceProfileAttribution(dataV1, dataV1Metrics),
                        ToSourceProfileAttribution(dataV2, dataV2Metrics),
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

    private static bool HasSourceProfile(HocVienComparableAttributionRowDto row)
        => !string.IsNullOrWhiteSpace(row.SourceProfileCode);

    private static HocVienSourceProfileAttributionDto ToSourceProfileAttribution(
        HocVienSourceComparableReadResultDto source,
        SourceMatchMetrics metrics) => new()
    {
        SourceProfileCode = source.SourceProfileCode,
        CanRead = source.CanRead,
        SourceRows = source.SourceRows,
        DistinctSourceMaDk = source.DistinctSourceMaDk,
        DuplicateSourceMaDkCount = source.DuplicateSourceMaDkCount,
        InvalidNgaySinhCount = source.InvalidNgaySinhCount,
        MatchedTargetRowsByMaDk = metrics.MatchedByMaDk,
        ExactFieldMatchTargetRows = metrics.ExactFieldMatch,
        V2RowHashMatchTargetRows = metrics.V2RowHashMatch,
        StrongerMatchTargetRows = metrics.StrongerMatch,
        Issue = source.Issue,
    };

    private static IReadOnlyList<HocVienTargetSourceProfileDistributionDto> BuildTargetDistribution(
        IReadOnlyList<HocVienComparableAttributionRowDto> targetRows)
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
        HocVienSourceComparableReadResultDto source,
        ICollection<string> issues,
        ICollection<SyncErrorDto> errors)
    {
        if (!string.IsNullOrWhiteSpace(source.Issue))
        {
            issues.Add(source.Issue);
        }

        if (source.CanRead)
        {
            return;
        }

        if (source.Error is not null)
        {
            errors.Add(source.Error);
        }
    }

    private static IReadOnlyDictionary<string, HocVienComparableAttributionRowDto> BuildMap(
        IReadOnlyCollection<HocVienComparableAttributionRowDto> rows)
        => rows
            .Where(row => !string.IsNullOrWhiteSpace(row.MaDK))
            .GroupBy(row => row.MaDK, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    private static SourceMatchMetrics BuildSourceMetrics(
        IReadOnlyList<HocVienComparableAttributionRowDto> targetRows,
        IReadOnlyDictionary<string, HocVienComparableAttributionRowDto> sourceRowsByMaDk)
    {
        var metrics = new SourceMatchMetrics();
        foreach (var target in targetRows)
        {
            if (!sourceRowsByMaDk.TryGetValue(target.MaDK, out var source))
            {
                continue;
            }

            metrics.MatchedByMaDk++;
            var differentFields = GetDifferentFields(target, source);
            foreach (var field in differentFields)
            {
                metrics.DifferentByField[field] = metrics.DifferentByField.GetValueOrDefault(field) + 1;
            }

            var exactFieldMatch = differentFields.Count == 0;
            var hashMatch = ValuesEqual(target.V2RowHash, source.V2RowHash) &&
                            !string.IsNullOrWhiteSpace(target.V2RowHash) &&
                            !string.IsNullOrWhiteSpace(source.V2RowHash);
            if (exactFieldMatch)
            {
                metrics.ExactFieldMatch++;
            }

            if (hashMatch)
            {
                metrics.V2RowHashMatch++;
            }

            if (exactFieldMatch || hashMatch)
            {
                metrics.StrongerMatch++;
            }
        }

        return metrics;
    }

    private static IReadOnlyList<string> GetDifferentFields(
        HocVienComparableAttributionRowDto target,
        HocVienComparableAttributionRowDto source)
    {
        var fields = new List<string>();
        AddIfDifferent(fields, "HoTen", target.HoTenNormalized, source.HoTenNormalized);
        AddIfDifferent(fields, "NgaySinh", target.NgaySinh?.ToString("yyyy-MM-dd"), source.NgaySinh?.ToString("yyyy-MM-dd"));
        AddIfDifferent(fields, "GioiTinh", target.GioiTinh, source.GioiTinh);
        AddIfDifferent(fields, "MaKhoa", target.MaKhoa, source.MaKhoa);
        AddIfDifferent(fields, "TenKhoa", target.TenKhoa, source.TenKhoa);
        AddIfDifferent(fields, "MaHangDT", target.MaHangDT, source.MaHangDT);
        AddIfDifferent(fields, "HangGPLXHoc", target.HangGPLXHoc, source.HangGPLXHoc);
        return fields;
    }

    private static void AddIfDifferent(
        ICollection<string> fields,
        string fieldName,
        string? targetValue,
        string? sourceValue)
    {
        if (!ValuesEqual(targetValue, sourceValue))
        {
            fields.Add(fieldName);
        }
    }

    private static bool ValuesEqual(string? left, string? right)
        => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<HocVienAttributionFieldDifferenceSummaryDto> BuildChangedFieldSummary(
        SourceMatchMetrics dataV1Metrics,
        SourceMatchMetrics dataV2Metrics)
        => ComparedFields
            .Select(field => new HocVienAttributionFieldDifferenceSummaryDto
            {
                FieldName = field,
                DataV1DifferentCount = dataV1Metrics.DifferentByField.GetValueOrDefault(field),
                DataV2DifferentCount = dataV2Metrics.DifferentByField.GetValueOrDefault(field),
            })
            .ToList();

    private static RecommendationResult Recommend(
        int targetRows,
        int dataV1SourceRows,
        int dataV2SourceRows,
        int strongerMatchDataV1,
        int strongerMatchDataV2)
    {
        if (targetRows <= 0)
        {
            return new RecommendationResult(RecommendationCannotDetermine, ConfidenceLow);
        }

        if (strongerMatchDataV1 == strongerMatchDataV2)
        {
            return new RecommendationResult(RecommendationAmbiguous, ConfidenceLow);
        }

        var dataV1Ratio = (double)strongerMatchDataV1 / targetRows;
        var dataV2Ratio = (double)strongerMatchDataV2 / targetRows;
        var dataV1Leads = dataV1Ratio > dataV2Ratio;
        var topRatio = dataV1Leads ? dataV1Ratio : dataV2Ratio;
        var diffRatio = Math.Abs(dataV1Ratio - dataV2Ratio);
        var topSourceRows = dataV1Leads ? dataV1SourceRows : dataV2SourceRows;
        var topRecommendation = dataV1Leads ? RecommendationDataV1 : RecommendationDataV2;

        if (topRatio >= 0.98 && diffRatio >= 0.10 && topSourceRows >= targetRows)
        {
            return new RecommendationResult(topRecommendation, ConfidenceHigh);
        }

        if (topRatio >= 0.90 && diffRatio >= 0.10)
        {
            return new RecommendationResult(topRecommendation, ConfidenceMedium);
        }

        return new RecommendationResult(RecommendationAmbiguous, ConfidenceLow);
    }

    private sealed class SourceMatchMetrics
    {
        public int MatchedByMaDk { get; set; }
        public int ExactFieldMatch { get; set; }
        public int V2RowHashMatch { get; set; }
        public int StrongerMatch { get; set; }
        public Dictionary<string, int> DifferentByField { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record RecommendationResult(string Recommendation, string Confidence);
}

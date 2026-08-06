using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync.Rt01;

/// <summary>
/// Privacy-safe, read-only evidence contract for RT-01A. Raw source/target values are
/// accepted only in memory by the classifier and are never included in its result.
/// </summary>
public static class Rt01aProofContract
{
    public const string ConsistencyLevel = "BEST_EFFORT_READ_ONLY_STABLE_SAMPLE";
    public const string IdentityNormalizationVersion = "RT01-IDENTITY-TRIM-ORDINAL-IGNORE-CASE-v1";
    public const string HmacVersion = "RT01A-HMAC-SHA256-v1";
    public const string MappingContractVersion = "QLHV-IMPORT-HOCVIEN-v1";
    public const string MappingContractStatus = "PASS";

    public static IReadOnlyList<string> MappedFields { get; } =
    [
        "MaDK",
        "MaKhoa",
        "TenKhoa",
        "MaHangDT",
        "HangGPLXHoc",
        "HoTen",
        "NgaySinh",
        "GioiTinh",
        "SoCCCD",
        "DiaChiThuongTru",
        "SoGPLXDaCo",
        "HangGPLXDaCo",
        "NguoiNhanHoSo",
        "AnhRelativePath",
        "SourcePhotoPathInvalid",
        "ChatLuongAnh",
        "NgayThuNhanAnh",
        "NguoiThuNhanAnh",
        "SourceOfTruth",
    ];

    public static string MappingFingerprint =>
        Sha256(string.Join(
            "\n",
            MappedFields.Select((field, index) => $"{index:D2}|{field}|TRIM_PRESERVE|SOURCE_OWNED")));

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}

/// <summary>
/// A target row held only inside the read-only proof process. Callers must never
/// serialize this type or write it to logs because it contains imported learner data.
/// </summary>
public sealed class Rt01aTargetHocVienRow
{
    public long HocVienId { get; init; }
    public string? SourceProfileCode { get; init; }
    public string? SourceMaDK { get; init; }
    public string? SourceSystem { get; init; }
    public string? SourceVersion { get; init; }
    public string? MaDK { get; init; }
    public string? MaKhoa { get; init; }
    public string? TenKhoa { get; init; }
    public string? MaHangDT { get; init; }
    public string? HangGPLXHoc { get; init; }
    public string? HoTen { get; init; }
    public DateTime? NgaySinh { get; init; }
    public string? GioiTinh { get; init; }
    public string? SoCCCD { get; init; }
    public string? DiaChiThuongTru { get; init; }
    public string? SoGPLXDaCo { get; init; }
    public string? HangGPLXDaCo { get; init; }
    public string? NguoiNhanHoSo { get; init; }
    public string? AnhRelativePath { get; init; }
    public int? ChatLuongAnh { get; init; }
    public DateTime? NgayThuNhanAnh { get; init; }
    public string? NguoiThuNhanAnh { get; init; }
    public string? SourceOfTruth { get; init; }
    public string? V2RowHash { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? LastSyncFromV2At { get; init; }
    public string? CreatedBy { get; init; }
    public string? UpdatedBy { get; init; }
    public string? DeletedBy { get; init; }
    public string? DeleteReason { get; init; }
    public string? GhiChuNoiBo { get; init; }
    public bool DaDoiChieuCccd { get; init; }
    public bool DaInThe { get; init; }
    public bool DaTaoXml { get; init; }
}

public sealed record Rt01aSqlIdentityMatch(int SourceOrdinal, long TargetHocVienId);

public sealed record Rt01aSourcePresenceEvidence(
    long TargetHocVienId,
    bool NguoiLxExists,
    bool NguoiLxHoSoExists,
    bool WouldPassCurrentSourceScope);

public sealed record Rt01aReadWindow(
    DateTime SourceReadStartedAtUtc,
    DateTime SourceReadCompletedAtUtc,
    DateTime TargetReadStartedAtUtc,
    DateTime TargetReadCompletedAtUtc);

public sealed record Rt01aRawProbe(
    IReadOnlyList<QlhvImportHocVienWriteModel> MappedSourceRows,
    IReadOnlyList<Rt01aTargetHocVienRow> TargetRows,
    IReadOnlyList<Rt01aSqlIdentityMatch> SqlIdentityMatches,
    IReadOnlyList<Rt01aSourcePresenceEvidence> TargetOnlySourcePresence,
    Rt01aReadWindow ReadWindow,
    string SourceSchemaFingerprint,
    string TargetSchemaFingerprint,
    string TargetIdentityCollation);

public sealed record Rt01aFieldDifference
{
    public string FieldCategory { get; init; } = string.Empty;
    public string Ownership { get; init; } = "V2_SOURCE_OWNED_MAPPED_FIELD";
    public string Transform { get; init; } = "TRIM_PRESERVE";
    public string SourceValueHmac { get; init; } = string.Empty;
    public string TargetValueHmac { get; init; } = string.Empty;
    public bool NormalizedEqual { get; init; }
    public bool SafeUpdateEligible { get; init; }
    public bool ContributesToUpdate { get; init; }
    public string DifferenceClass { get; init; } = string.Empty;
}

public sealed record Rt01aCandidateEvidence
{
    public string CandidateType { get; init; } = string.Empty;
    public string IdentityHmac { get; init; } = string.Empty;
    public string Classification { get; init; } = string.Empty;
    public string SafeDisposition { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;
    public bool SqlCollationEqualCounterpart { get; init; }
    public bool CaseDifference { get; init; }
    public bool AccentDifference { get; init; }
    public bool LeadingOrTrailingSpaceDifference { get; init; }
    public bool UnicodeNormalizationDifference { get; init; }
    public bool NullOrBlankDifference { get; init; }
    public bool AlternateImportedKeyEvidence { get; init; }
    public bool SoftDeletedCounterpart { get; init; }
    public bool OtherProfileCounterpart { get; init; }
    public bool ExistingAutoSyncAttribution { get; init; }
    public bool RawSourceRepresentationExists { get; init; }
    public bool WouldPassCurrentSourceScope { get; init; }
    public DateTime? TargetCreatedAtUtc { get; init; }
    public DateTime? TargetUpdatedAtUtc { get; init; }
    public DateTime? TargetLastSyncFromV2AtUtc { get; init; }
    public bool ManualReviewRequired { get; init; }
    public IReadOnlyList<Rt01aFieldDifference> FieldDifferences { get; init; } =
        Array.Empty<Rt01aFieldDifference>();
}

public sealed record Rt01aProbeEvidence
{
    public string ConsistencyLevel { get; init; } = Rt01aProofContract.ConsistencyLevel;
    public string HmacVersion { get; init; } = Rt01aProofContract.HmacVersion;
    public string IdentityNormalizationVersion { get; init; } =
        Rt01aProofContract.IdentityNormalizationVersion;
    public string MappingContractVersion { get; init; } =
        Rt01aProofContract.MappingContractVersion;
    public string MappingContractStatus { get; init; } =
        Rt01aProofContract.MappingContractStatus;
    public string MappingFingerprint { get; init; } = string.Empty;
    public string SourceSchemaFingerprint { get; init; } = string.Empty;
    public string TargetSchemaFingerprint { get; init; } = string.Empty;
    public string TargetIdentityCollation { get; init; } = string.Empty;
    public Rt01aReadWindow ReadWindow { get; init; } = new(default, default, default, default);
    public int SourceActiveRows { get; init; }
    public int TargetActiveRows { get; init; }
    public int TargetSoftDeletedRows { get; init; }
    public int IntersectionRows { get; init; }
    public int NoChangeRows { get; init; }
    public int WouldInsertRows { get; init; }
    public int WouldUpdateRows { get; init; }
    public int WouldReactivateRows { get; init; }
    public int TargetOnlyActiveRows { get; init; }
    public int ConflictRows { get; init; }
    public int ManualReviewRows { get; init; }
    public string SourceKeySetHash { get; init; } = string.Empty;
    public string TargetKeySetHash { get; init; } = string.Empty;
    public string IntersectionHash { get; init; } = string.Empty;
    public string SourceOnlyHash { get; init; } = string.Empty;
    public string TargetOnlyHash { get; init; } = string.Empty;
    public string UpdateCandidateHash { get; init; } = string.Empty;
    public string StageHash { get; init; } = string.Empty;
    public string TargetComparisonHash { get; init; } = string.Empty;
    public IReadOnlyList<Rt01aCandidateEvidence> Candidates { get; init; } =
        Array.Empty<Rt01aCandidateEvidence>();
    public int BusinessDataWrites { get; init; }
    public bool ApplyCheckpointPublished { get; init; }
    public bool ExistingAutoSyncTouched { get; init; }
}

/// <summary>
/// Pure classifier for a single OTO probe. It follows the same trim +
/// OrdinalIgnoreCase identity behavior as QlhvFullSyncPlanner and separately
/// consumes SQL Server collation matches so matcher aliases cannot be hidden.
/// </summary>
public static class Rt01aDriftClassifier
{
    private static readonly StringComparer IdentityComparer = StringComparer.OrdinalIgnoreCase;

    public static Rt01aProbeEvidence Classify(
        Rt01aRawProbe probe,
        byte[] hmacKey,
        string sourceProfileCode = "CSDT_OTO")
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(hmacKey);
        if (hmacKey.Length < 32)
        {
            throw new ArgumentException("RT-01A HMAC key must contain at least 256 bits.", nameof(hmacKey));
        }

        var sourceRows = probe.MappedSourceRows
            .Select((row, ordinal) => new SourceWithOrdinal(row, ordinal))
            .OrderBy(item => NormalizeIdentity(item.Row.SourceMaDK), StringComparer.Ordinal)
            .ToArray();
        var targetPartition = probe.TargetRows
            .Where(row => string.Equals(
                NormalizeOptional(row.SourceProfileCode),
                sourceProfileCode,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => NormalizeIdentity(row.SourceMaDK), StringComparer.Ordinal)
            .ThenBy(row => row.HocVienId)
            .ToArray();

        var sourceByIdentity = sourceRows.ToDictionary(
            item => NormalizeIdentity(item.Row.SourceMaDK),
            item => item,
            IdentityComparer);
        var targetByIdentity = targetPartition.ToDictionary(
            row => NormalizeIdentity(row.SourceMaDK),
            row => row,
            IdentityComparer);
        var sqlMatchesBySource = probe.SqlIdentityMatches
            .GroupBy(match => match.SourceOrdinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(match => match.TargetHocVienId).ToHashSet());
        var targetById = probe.TargetRows.ToDictionary(row => row.HocVienId);

        var intersection = sourceByIdentity.Keys
            .Where(targetByIdentity.ContainsKey)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var sourceOnly = sourceByIdentity.Keys
            .Where(key => !targetByIdentity.ContainsKey(key))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var targetOnlyActive = targetByIdentity
            .Where(pair => !pair.Value.IsDeleted && !sourceByIdentity.ContainsKey(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .ToArray();
        var updateKeys = intersection
            .Where(key =>
                !targetByIdentity[key].IsDeleted &&
                !string.Equals(
                    sourceByIdentity[key].Row.V2RowHash,
                    targetByIdentity[key].V2RowHash,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var reactivateKeys = intersection
            .Where(key => targetByIdentity[key].IsDeleted)
            .ToArray();
        var noChangeKeys = intersection
            .Where(key =>
                !targetByIdentity[key].IsDeleted &&
                string.Equals(
                    sourceByIdentity[key].Row.V2RowHash,
                    targetByIdentity[key].V2RowHash,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var candidates = new List<Rt01aCandidateEvidence>();
        foreach (var key in sourceOnly)
        {
            var source = sourceByIdentity[key];
            var sqlCounterparts = sqlMatchesBySource.TryGetValue(source.Ordinal, out var ids)
                ? ids.Select(id => targetById[id]).ToArray()
                : Array.Empty<Rt01aTargetHocVienRow>();
            var sameProfileSqlCounterpart = sqlCounterparts.FirstOrDefault(row =>
                string.Equals(
                    NormalizeOptional(row.SourceProfileCode),
                    sourceProfileCode,
                    StringComparison.OrdinalIgnoreCase));
            var otherProfile = probe.TargetRows.FirstOrDefault(row =>
                !string.Equals(
                    NormalizeOptional(row.SourceProfileCode),
                    sourceProfileCode,
                    StringComparison.OrdinalIgnoreCase) &&
                IdentityComparer.Equals(
                    NormalizeIdentity(row.SourceMaDK),
                    NormalizeIdentity(source.Row.SourceMaDK)));
            var softCounterpart = targetPartition.FirstOrDefault(row =>
                row.IsDeleted &&
                (IdentityComparer.Equals(
                     NormalizeIdentity(row.SourceMaDK),
                     NormalizeIdentity(source.Row.SourceMaDK)) ||
                 sqlCounterparts.Any(match => match.HocVienId == row.HocVienId)));
            var rekeyCounterpart = FindStrongRekeyCounterpart(source.Row, targetOnlyActive);
            var alias = sameProfileSqlCounterpart is not null &&
                        !IdentityComparer.Equals(
                            NormalizeIdentity(sameProfileSqlCounterpart.SourceMaDK),
                            NormalizeIdentity(source.Row.SourceMaDK));

            var classification = softCounterpart is not null
                ? "TARGET_SOFT_DELETED_COUNTERPART"
                : otherProfile is not null
                    ? "PROFILE_ATTRIBUTION_MISMATCH"
                    : alias
                        ? "IDENTITY_COLLATION_ALIAS"
                        : rekeyCounterpart is not null
                            ? "TARGET_PRESENT_UNDER_ALIAS_KEY"
                            : "SOURCE_ONLY_NEW_ROW";
            var disposition = softCounterpart is not null
                ? "WOULD_REACTIVATE_SAFE_AFTER_APPROVAL"
                : otherProfile is not null
                    ? "OWNERSHIP_CONFLICT"
                    : alias
                        ? "SHADOW_IMPLEMENTATION_FIX_REQUIRED"
                        : rekeyCounterpart is not null
                            ? "REKEY_REQUIRES_MIGRATION"
                            : "WOULD_INSERT_SAFE_AFTER_APPROVAL";
            var counterpart = softCounterpart ?? sameProfileSqlCounterpart ?? otherProfile ?? rekeyCounterpart;

            candidates.Add(BuildIdentityCandidate(
                "WOULD_INSERT",
                source.Row.SourceProfileCode,
                source.Row.SourceMaDK,
                counterpart?.SourceMaDK,
                classification,
                disposition,
                alias ? "SQL_COLLATION_MATCH_NOT_MATCHED_BY_RT01" : classification,
                sameProfileSqlCounterpart is not null,
                rekeyCounterpart is not null,
                softCounterpart is not null,
                otherProfile is not null,
                IsExistingAutoSyncAttributed(counterpart),
                disposition is "OWNERSHIP_CONFLICT" or "REKEY_REQUIRES_MIGRATION",
                hmacKey));
        }

        foreach (var key in updateKeys)
        {
            var source = sourceByIdentity[key].Row;
            var target = targetByIdentity[key];
            var differences = CompareMappedFields(source, target, hmacKey);
            var photoManualReview = differences.Count != 0 && differences.All(difference =>
                difference.FieldCategory is "AnhRelativePath" or "ChatLuongAnh" or
                    "NgayThuNhanAnh");
            var classification = differences.Count == 0
                ? "SHADOW_HASH_BUG"
                : differences.All(difference => difference.NormalizedEqual)
                    ? "NORMALIZATION_ONLY_DIFFERENCE"
                    : photoManualReview
                        ? "MULTI_FIELD_PHOTO_DRIFT"
                        : "STALE_IMPORTED_VALUE";
            var disposition = differences.Count == 0
                ? "SHADOW_IMPLEMENTATION_FIX_REQUIRED"
                : differences.All(difference => difference.NormalizedEqual)
                    ? "NO_UPDATE_NORMALIZED_EQUAL"
                    : photoManualReview
                        ? "MANUAL_REVIEW_REQUIRED"
                    : "WOULD_UPDATE_SOURCE_OWNED_FIELDS_AFTER_APPROVAL";

            candidates.Add(new Rt01aCandidateEvidence
            {
                CandidateType = "WOULD_UPDATE",
                IdentityHmac = IdentityHmac(hmacKey, source.SourceProfileCode, source.SourceMaDK),
                Classification = classification,
                SafeDisposition = disposition,
                ReasonCode = classification,
                ExistingAutoSyncAttribution = IsExistingAutoSyncAttributed(target),
                TargetCreatedAtUtc = AsUtc(target.CreatedAt),
                TargetUpdatedAtUtc = AsUtc(target.UpdatedAt),
                TargetLastSyncFromV2AtUtc = AsUtc(target.LastSyncFromV2At),
                ManualReviewRequired = disposition is
                    "SHADOW_IMPLEMENTATION_FIX_REQUIRED" or "MANUAL_REVIEW_REQUIRED",
                FieldDifferences = differences,
            });
        }

        foreach (var key in reactivateKeys)
        {
            var source = sourceByIdentity[key].Row;
            var target = targetByIdentity[key];
            candidates.Add(new Rt01aCandidateEvidence
            {
                CandidateType = "WOULD_REACTIVATE",
                IdentityHmac = IdentityHmac(hmacKey, source.SourceProfileCode, source.SourceMaDK),
                Classification = "TARGET_SOFT_DELETED_COUNTERPART",
                SafeDisposition = "WOULD_REACTIVATE_SAFE_AFTER_APPROVAL",
                ReasonCode = "TARGET_SOFT_DELETED_COUNTERPART",
                SoftDeletedCounterpart = true,
                ExistingAutoSyncAttribution = IsExistingAutoSyncAttributed(target),
                TargetCreatedAtUtc = AsUtc(target.CreatedAt),
                TargetUpdatedAtUtc = AsUtc(target.UpdatedAt),
                TargetLastSyncFromV2AtUtc = AsUtc(target.LastSyncFromV2At),
            });
        }

        foreach (var target in targetOnlyActive)
        {
            var rekeySource = sourceOnly
                .Select(key => sourceByIdentity[key].Row)
                .FirstOrDefault(source => StrongRekeyEvidence(source, target));
            var imported = IsExistingAutoSyncAttributed(target);
            var sourcePresence = probe.TargetOnlySourcePresence.SingleOrDefault(
                evidence => evidence.TargetHocVienId == target.HocVienId);
            var hasRawSourceRepresentation =
                sourcePresence is not null &&
                (sourcePresence.NguoiLxExists || sourcePresence.NguoiLxHoSoExists);
            var native = !imported &&
                         string.IsNullOrWhiteSpace(target.SourceSystem) &&
                         !string.IsNullOrWhiteSpace(target.CreatedBy);
            var legacy = !imported &&
                         string.Equals(
                             target.SourceSystem?.Trim(),
                             "LEGACY",
                             StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrWhiteSpace(target.SourceProfileCode);
            var classification = rekeySource is not null
                ? "SOURCE_ROW_REKEYED"
                : native
                    ? "QLHV_NATIVE_ROW"
                    : legacy
                        ? "LEGACY_IMPORTED_ROW"
                    : imported && hasRawSourceRepresentation &&
                      sourcePresence is not null &&
                      !sourcePresence.WouldPassCurrentSourceScope
                        ? "SOURCE_ROW_FILTERED_OUT"
                        : imported && !hasRawSourceRepresentation
                            ? "SOURCE_ROW_REMOVED"
                        : "ORPHAN_IMPORT_ATTRIBUTION";
            var disposition = rekeySource is not null
                ? "REKEY_REQUIRES_MIGRATION"
                : native
                    ? "TARGET_NATIVE_RETAIN"
                    : legacy
                        ? "LEGACY_RETAIN"
                    : imported
                        ? "MANUAL_REVIEW_REQUIRED"
                        : "OWNERSHIP_CONFLICT";

            var candidate = BuildIdentityCandidate(
                "TARGET_ONLY_ACTIVE",
                target.SourceProfileCode,
                target.SourceMaDK,
                rekeySource?.SourceMaDK,
                classification,
                disposition,
                classification,
                sqlCollationEqual: false,
                alternateImportedKey: rekeySource is not null,
                softDeletedCounterpart: false,
                otherProfileCounterpart: false,
                existingAutoSyncAttribution: imported,
                manualReview: disposition is "REKEY_REQUIRES_MIGRATION" or "MANUAL_REVIEW_REQUIRED" or "OWNERSHIP_CONFLICT",
                hmacKey);
            candidates.Add(candidate with
            {
                RawSourceRepresentationExists = hasRawSourceRepresentation,
                WouldPassCurrentSourceScope =
                    sourcePresence?.WouldPassCurrentSourceScope ?? false,
                TargetCreatedAtUtc = AsUtc(target.CreatedAt),
                TargetUpdatedAtUtc = AsUtc(target.UpdatedAt),
                TargetLastSyncFromV2AtUtc = AsUtc(target.LastSyncFromV2At),
            });
        }

        var sourceActiveKeys = sourceRows.Select(row => row.Row.SourceMaDK).ToArray();
        var targetActiveKeys = targetPartition.Where(row => !row.IsDeleted).Select(row => row.SourceMaDK).ToArray();
        var intersectionKeys = intersection.Select(key => sourceByIdentity[key].Row.SourceMaDK).ToArray();
        var sourceOnlyKeys = sourceOnly.Select(key => sourceByIdentity[key].Row.SourceMaDK).ToArray();
        var targetOnlyKeys = targetOnlyActive.Select(row => row.SourceMaDK).ToArray();
        var updateCandidateKeys = updateKeys.Select(key => sourceByIdentity[key].Row.SourceMaDK).ToArray();
        var conflictRows = candidates.Count(candidate =>
            candidate.SafeDisposition is "OWNERSHIP_CONFLICT" or "SHADOW_IMPLEMENTATION_FIX_REQUIRED");
        var manualReviewRows = candidates.Count(candidate => candidate.ManualReviewRequired);

        return new Rt01aProbeEvidence
        {
            MappingFingerprint = Rt01aProofContract.MappingFingerprint,
            SourceSchemaFingerprint = probe.SourceSchemaFingerprint,
            TargetSchemaFingerprint = probe.TargetSchemaFingerprint,
            TargetIdentityCollation = probe.TargetIdentityCollation,
            ReadWindow = probe.ReadWindow,
            SourceActiveRows = sourceRows.Length,
            TargetActiveRows = targetPartition.Count(row => !row.IsDeleted),
            TargetSoftDeletedRows = targetPartition.Count(row => row.IsDeleted),
            IntersectionRows = intersection.Length,
            NoChangeRows = noChangeKeys.Length,
            WouldInsertRows = sourceOnly.Length,
            WouldUpdateRows = updateKeys.Length,
            WouldReactivateRows = reactivateKeys.Length,
            TargetOnlyActiveRows = targetOnlyActive.Length,
            ConflictRows = conflictRows,
            ManualReviewRows = manualReviewRows,
            SourceKeySetHash = KeySetHmac(
                hmacKey, sourceProfileCode, "source-key-set", sourceActiveKeys),
            TargetKeySetHash = KeySetHmac(
                hmacKey, sourceProfileCode, "target-key-set", targetActiveKeys),
            IntersectionHash = KeySetHmac(
                hmacKey, sourceProfileCode, "intersection", intersectionKeys),
            SourceOnlyHash = KeySetHmac(
                hmacKey, sourceProfileCode, "source-only", sourceOnlyKeys),
            TargetOnlyHash = KeySetHmac(
                hmacKey, sourceProfileCode, "target-only", targetOnlyKeys),
            UpdateCandidateHash = KeySetHmac(
                hmacKey, sourceProfileCode, "update-candidate", updateCandidateKeys),
            StageHash = RowSetHmac(
                hmacKey,
                "mapped-stage",
                sourceRows.Select(row =>
                    $"{NormalizeIdentity(row.Row.SourceMaDK)}|{row.Row.V2RowHash}")),
            TargetComparisonHash = RowSetHmac(
                hmacKey,
                "target-comparison",
                targetPartition.Select(row =>
                    $"{NormalizeIdentity(row.SourceMaDK)}|{row.V2RowHash}|{(row.IsDeleted ? 1 : 0)}")),
            Candidates = candidates
                .OrderBy(candidate => candidate.CandidateType, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.IdentityHmac, StringComparer.Ordinal)
                .ToArray(),
            BusinessDataWrites = 0,
            ApplyCheckpointPublished = false,
            ExistingAutoSyncTouched = false,
        };
    }

    private static Rt01aCandidateEvidence BuildIdentityCandidate(
        string candidateType,
        string? profile,
        string? key,
        string? counterpartKey,
        string classification,
        string disposition,
        string reasonCode,
        bool sqlCollationEqual,
        bool alternateImportedKey,
        bool softDeletedCounterpart,
        bool otherProfileCounterpart,
        bool existingAutoSyncAttribution,
        bool manualReview,
        byte[] hmacKey)
    {
        var relation = counterpartKey is null
            ? default
            : CompareIdentityForms(key, counterpartKey);
        return new Rt01aCandidateEvidence
        {
            CandidateType = candidateType,
            IdentityHmac = IdentityHmac(hmacKey, profile, key),
            Classification = classification,
            SafeDisposition = disposition,
            ReasonCode = reasonCode,
            SqlCollationEqualCounterpart = sqlCollationEqual,
            CaseDifference = relation.CaseDifference,
            AccentDifference = relation.AccentDifference,
            LeadingOrTrailingSpaceDifference = relation.SpaceDifference,
            UnicodeNormalizationDifference = relation.UnicodeNormalizationDifference,
            NullOrBlankDifference = relation.NullOrBlankDifference,
            AlternateImportedKeyEvidence = alternateImportedKey,
            SoftDeletedCounterpart = softDeletedCounterpart,
            OtherProfileCounterpart = otherProfileCounterpart,
            ExistingAutoSyncAttribution = existingAutoSyncAttribution,
            ManualReviewRequired = manualReview,
        };
    }

    private static IReadOnlyList<Rt01aFieldDifference> CompareMappedFields(
        QlhvImportHocVienWriteModel source,
        Rt01aTargetHocVienRow target,
        byte[] hmacKey)
    {
        var fields = new[]
        {
            Field("MaDK", source.MaDK, target.MaDK),
            Field("MaKhoa", source.MaKhoa, target.MaKhoa),
            Field("TenKhoa", source.TenKhoa, target.TenKhoa),
            Field("MaHangDT", source.MaHangDT, target.MaHangDT),
            Field("HangGPLXHoc", source.HangGPLXHoc, target.HangGPLXHoc),
            Field("HoTen", source.HoTen, target.HoTen),
            Field("NgaySinh", Date(source.NgaySinh), Date(target.NgaySinh)),
            Field("GioiTinh", source.GioiTinh, target.GioiTinh),
            Field("SoCCCD", source.SoCCCD, target.SoCCCD),
            Field("DiaChiThuongTru", source.DiaChiThuongTru, target.DiaChiThuongTru),
            Field("SoGPLXDaCo", source.SoGPLXDaCo, target.SoGPLXDaCo),
            Field("HangGPLXDaCo", source.HangGPLXDaCo, target.HangGPLXDaCo),
            Field("NguoiNhanHoSo", source.NguoiNhanHoSo, target.NguoiNhanHoSo),
            Field("AnhRelativePath", source.AnhRelativePath, target.AnhRelativePath),
            Field("ChatLuongAnh", Number(source.ChatLuongAnh), Number(target.ChatLuongAnh)),
            Field("NgayThuNhanAnh", Timestamp(source.NgayThuNhanAnh), Timestamp(target.NgayThuNhanAnh)),
            Field("NguoiThuNhanAnh", source.NguoiThuNhanAnh, target.NguoiThuNhanAnh),
            Field("SourceOfTruth", source.SourceOfTruth, target.SourceOfTruth),
        };

        return fields
            .Where(field => !string.Equals(field.Source, field.Target, StringComparison.Ordinal))
            .Select(field =>
            {
                var normalizedEqual = string.Equals(
                    NormalizeField(field.Source),
                    NormalizeField(field.Target),
                    StringComparison.OrdinalIgnoreCase);
                return new Rt01aFieldDifference
                {
                    FieldCategory = field.Name,
                    Transform = field.Name switch
                    {
                        "NgaySinh" => "DATE_YYYY_MM_DD",
                        "NgayThuNhanAnh" => "DATETIME2_ROUNDTRIP",
                        "AnhRelativePath" => "SAFE_RELATIVE_JP2_PATH",
                        _ => "TRIM_PRESERVE",
                    },
                    SourceValueHmac = ValueHmac(hmacKey, field.Name, field.Source),
                    TargetValueHmac = ValueHmac(hmacKey, field.Name, field.Target),
                    NormalizedEqual = normalizedEqual,
                    SafeUpdateEligible = !normalizedEqual && field.Name is not
                        ("AnhRelativePath" or "ChatLuongAnh" or "NgayThuNhanAnh"),
                    ContributesToUpdate = field.Name is not
                        ("AnhRelativePath" or "ChatLuongAnh" or "NgayThuNhanAnh"),
                    DifferenceClass = normalizedEqual
                        ? "NORMALIZATION_ONLY_DIFFERENCE"
                        : NullBlankDifferent(field.Source, field.Target)
                            ? "NULL_BLANK_DIFFERENCE"
                            : field.Name is "AnhRelativePath" or "ChatLuongAnh" or
                                "NgayThuNhanAnh"
                                ? "PHOTO_MANUAL_REVIEW"
                                : "V2_SOURCE_OWNED_MAPPED_FIELD",
                };
            })
            .ToArray();
    }

    private static Rt01aTargetHocVienRow? FindStrongRekeyCounterpart(
        QlhvImportHocVienWriteModel source,
        IReadOnlyList<Rt01aTargetHocVienRow> targets)
        => targets.SingleOrDefault(target => StrongRekeyEvidence(source, target));

    private static bool StrongRekeyEvidence(
        QlhvImportHocVienWriteModel source,
        Rt01aTargetHocVienRow target)
    {
        var cccdMatch = NonBlankEqual(source.SoCCCD, target.SoCCCD);
        var identityAttributesMatch =
            NonBlankEqual(source.HoTen, target.HoTen) &&
            source.NgaySinh.HasValue &&
            target.NgaySinh.HasValue &&
            source.NgaySinh.Value.Date == target.NgaySinh.Value.Date;
        var courseMatch = NonBlankEqual(source.MaKhoa, target.MaKhoa);
        return cccdMatch && (identityAttributesMatch || courseMatch);
    }

    private static bool IsExistingAutoSyncAttributed(Rt01aTargetHocVienRow? row)
        => row is not null &&
           (string.Equals(row.CreatedBy?.Trim(), "QlhvBakFullSync", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(row.UpdatedBy?.Trim(), "QlhvBakFullSync", StringComparison.OrdinalIgnoreCase));

    private static (bool CaseDifference, bool AccentDifference, bool SpaceDifference,
        bool UnicodeNormalizationDifference, bool NullOrBlankDifference) CompareIdentityForms(
        string? left,
        string? right)
    {
        var leftValue = left ?? string.Empty;
        var rightValue = right ?? string.Empty;
        var leftTrimmed = leftValue.Trim();
        var rightTrimmed = rightValue.Trim();
        var removeAccentsLeft = RemoveAccents(leftTrimmed);
        var removeAccentsRight = RemoveAccents(rightTrimmed);
        return (
            !string.Equals(leftTrimmed, rightTrimmed, StringComparison.Ordinal) &&
            string.Equals(leftTrimmed, rightTrimmed, StringComparison.OrdinalIgnoreCase),
            !string.Equals(leftTrimmed, rightTrimmed, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(removeAccentsLeft, removeAccentsRight, StringComparison.OrdinalIgnoreCase),
            !string.Equals(leftValue, leftTrimmed, StringComparison.Ordinal) ||
            !string.Equals(rightValue, rightTrimmed, StringComparison.Ordinal),
            !string.Equals(
                leftTrimmed.Normalize(NormalizationForm.FormC),
                rightTrimmed.Normalize(NormalizationForm.FormC),
                StringComparison.Ordinal) &&
            string.Equals(
                leftTrimmed.Normalize(NormalizationForm.FormD),
                rightTrimmed.Normalize(NormalizationForm.FormD),
                StringComparison.Ordinal),
            string.IsNullOrEmpty(leftValue) != string.IsNullOrEmpty(rightValue) ||
            string.IsNullOrWhiteSpace(leftValue) != string.IsNullOrWhiteSpace(rightValue));
    }

    private static string RemoveAccents(string value)
        => new(
            value.Normalize(NormalizationForm.FormD)
                .Where(character =>
                    CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                .ToArray());

    private static string KeySetHmac(
        byte[] key,
        string sourceProfileCode,
        string domain,
        IEnumerable<string?> identities)
        => RowSetHmac(
            key,
            domain,
            identities.Select(identity => IdentityHmac(key, sourceProfileCode, identity)));

    private static string RowSetHmac(byte[] key, string domain, IEnumerable<string> rows)
    {
        var canonical = string.Join(
            "\n",
            rows.Order(StringComparer.Ordinal).Select(value => $"{value.Length}:{value}"));
        return Hmac(key, domain, canonical);
    }

    private static string IdentityHmac(byte[] key, string? profile, string? identity)
        => Hmac(
            key,
            "identity",
            $"{NormalizeOptional(profile).ToUpperInvariant()}|{NormalizeIdentity(identity)}");

    private static string ValueHmac(byte[] key, string field, string? value)
        => Hmac(key, $"field:{field}", value ?? "<NULL>");

    private static string Hmac(byte[] key, string domain, string value)
    {
        using var hmac = new HMACSHA256(key);
        var bytes = Encoding.UTF8.GetBytes($"{Rt01aProofContract.HmacVersion}|{domain}|{value}");
        return $"{Rt01aProofContract.HmacVersion}:{Convert.ToHexString(hmac.ComputeHash(bytes)).ToLowerInvariant()}";
    }

    private static string NormalizeIdentity(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeField(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Normalize(NormalizationForm.FormC);

    private static bool NonBlankEqual(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool NullBlankDifferent(string? left, string? right)
        => string.IsNullOrWhiteSpace(left) != string.IsNullOrWhiteSpace(right);

    private static string? Date(DateTime? value)
        => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Timestamp(DateTime? value)
        => value?.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    private static string? Number(int? value)
        => value?.ToString(CultureInfo.InvariantCulture);

    private static DateTime? AsUtc(DateTime? value)
        => value.HasValue
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : null;

    private static (string Name, string? Source, string? Target) Field(
        string name,
        string? source,
        string? target)
        => (name, source, target);

    private sealed record SourceWithOrdinal(QlhvImportHocVienWriteModel Row, int Ordinal);
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using QLHV.Application.Sync.Mapping;
using QLHV.Application.Sync.Rt01;

namespace QLHV.Application.Sync.Rt03;

public static class Rt03ReviewedRetainedContract
{
    public const string Version = "RT03-REVIEWED-RETAINED-1.0";
    public const string DomainLearner = "LEARNER";
    public const string ActiveState = "REVIEWED_AND_RETAINED";
    public const string SupersededState = "SUPERSEDED";
    public const string HealthyState = "REVIEWED_AND_RETAINED";
    public const string BlockedState = "BLOCKED";
    public const string PhotoClassification = "MULTI_FIELD_PHOTO_DRIFT";
}

public static class Rt03ReviewedRetainedReasonCodes
{
    public const string ReviewedAndRetained = "REVIEWED_AND_RETAINED";
    public const string ReviewStale = "REVIEW_STALE";
    public const string ReviewSourceChanged = "REVIEW_SOURCE_CHANGED";
    public const string ReviewTargetChanged = "REVIEW_TARGET_CHANGED";
    public const string ReviewFieldSetChanged = "REVIEW_FIELDSET_CHANGED";
    public const string ReviewIdentityAmbiguous = "REVIEW_IDENTITY_AMBIGUOUS";
    public const string ReviewEvidenceIncomplete = "REVIEW_EVIDENCE_INCOMPLETE";
    public const string NewTargetDrift = "NEW_TARGET_DRIFT";
}

public enum Rt03ReviewedRetainedContext
{
    IncrementalWorker,
    NoChangeCycle,
    FullConvergence,
    RecoveryVerification,
    ProductionPreflight,
    RuntimeDiagnostics,
}

public sealed record Rt03ReviewedRetainedInput
{
    public string SourceProfileCode { get; init; } = string.Empty;
    public string DomainCode { get; init; } = string.Empty;
    public string SourceBusinessIdentityHash { get; init; } = string.Empty;
    public long? TargetIdentity { get; init; }
    public string DriftClassification { get; init; } = string.Empty;
    public string ReviewedFieldSet { get; init; } = string.Empty;
    public string CurrentFieldSet { get; init; } = string.Empty;
    public long SourceVersion { get; init; }
    public long ReviewVersion { get; init; }
    public long CheckpointVersion { get; init; }
    public int SourceIdentityCount { get; init; }
    public int LiveTargetIdentityCount { get; init; }
    public int ActiveReviewCount { get; init; }
    public bool MarkerCheckpointAtomic { get; init; }
    public bool TargetRetainedActive { get; init; }
    public bool TargetMutated { get; init; }
    public bool ReviewIsActive { get; init; }
    public bool HasNewSourceEvent { get; init; }
    public bool HasNewDriftOutsideReviewedFields { get; init; }
    public string ReviewedSourceFingerprint { get; init; } = string.Empty;
    public string CurrentSourceFingerprint { get; init; } = string.Empty;
    public string ReviewedTargetFingerprint { get; init; } = string.Empty;
    public string CurrentTargetFingerprint { get; init; } = string.Empty;
    public string ReviewedOwnershipFingerprint { get; init; } = string.Empty;
    public string CurrentOwnershipFingerprint { get; init; } = string.Empty;
    public string EvidenceContractVersion { get; init; } = string.Empty;
}

public sealed record Rt03ReviewedRetainedEvaluation(
    string State,
    string ReasonCode,
    long SourceVersion,
    long ReviewVersion,
    bool SourceFingerprintMatch,
    bool TargetFingerprintMatch,
    bool OwnershipFingerprintMatch,
    bool HasNewSourceEvent,
    bool IsReviewedRetained,
    bool IsSafeSteadyState,
    bool WritesAllowed,
    string DiagnosticId);

/// <summary>
/// The single fail-closed interpretation of an immutable reviewed-retained decision.
/// Every runtime context supplies evidence to this policy; no context may weaken it.
/// </summary>
public static class Rt03ReviewedRetainedPolicy
{
    public static Rt03ReviewedRetainedEvaluation Evaluate(
        Rt03ReviewedRetainedInput input,
        Rt03ReviewedRetainedContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        var sourceMatch = ExactHashMatch(
            input.ReviewedSourceFingerprint, input.CurrentSourceFingerprint);
        var targetMatch = ExactHashMatch(
            input.ReviewedTargetFingerprint, input.CurrentTargetFingerprint);
        var ownershipMatch = ExactHashMatch(
            input.ReviewedOwnershipFingerprint, input.CurrentOwnershipFingerprint);

        var reason = DecideReason(input, sourceMatch, targetMatch, ownershipMatch);
        var safe = reason == Rt03ReviewedRetainedReasonCodes.ReviewedAndRetained;
        return new Rt03ReviewedRetainedEvaluation(
            safe
                ? Rt03ReviewedRetainedContract.HealthyState
                : Rt03ReviewedRetainedContract.BlockedState,
            reason,
            input.SourceVersion,
            input.ReviewVersion,
            sourceMatch,
            targetMatch,
            ownershipMatch,
            input.HasNewSourceEvent,
            safe,
            safe,
            safe,
            DiagnosticId(input, context));
    }

    private static string DecideReason(
        Rt03ReviewedRetainedInput input,
        bool sourceMatch,
        bool targetMatch,
        bool ownershipMatch)
    {
        if (!EvidenceComplete(input))
        {
            return Rt03ReviewedRetainedReasonCodes.ReviewEvidenceIncomplete;
        }

        if (input.SourceIdentityCount != 1 ||
            input.LiveTargetIdentityCount != 1 ||
            input.ActiveReviewCount != 1 ||
            input.TargetIdentity is null)
        {
            return Rt03ReviewedRetainedReasonCodes.ReviewIdentityAmbiguous;
        }

        if (!input.ReviewIsActive ||
            input.CheckpointVersion < input.ReviewVersion ||
            !input.MarkerCheckpointAtomic ||
            !input.TargetRetainedActive ||
            input.TargetMutated)
        {
            return Rt03ReviewedRetainedReasonCodes.ReviewStale;
        }

        if (input.HasNewSourceEvent)
        {
            return Rt03ReviewedRetainedReasonCodes.ReviewStale;
        }

        if (!sourceMatch)
        {
            return Rt03ReviewedRetainedReasonCodes.ReviewSourceChanged;
        }

        if (!targetMatch || !ownershipMatch)
        {
            return Rt03ReviewedRetainedReasonCodes.ReviewTargetChanged;
        }

        if (!string.Equals(
                NormalizeFieldSet(input.ReviewedFieldSet),
                NormalizeFieldSet(input.CurrentFieldSet),
                StringComparison.Ordinal) ||
            input.HasNewDriftOutsideReviewedFields)
        {
            return Rt03ReviewedRetainedReasonCodes.ReviewFieldSetChanged;
        }

        return Rt03ReviewedRetainedReasonCodes.ReviewedAndRetained;
    }

    private static bool EvidenceComplete(Rt03ReviewedRetainedInput input)
        => string.Equals(
               input.EvidenceContractVersion,
               Rt03ReviewedRetainedContract.Version,
               StringComparison.Ordinal) &&
           input.SourceVersion >= 0 &&
           input.ReviewVersion >= 0 &&
           input.CheckpointVersion >= 0 &&
           !string.IsNullOrWhiteSpace(input.SourceProfileCode) &&
           !string.IsNullOrWhiteSpace(input.DomainCode) &&
           IsHash(input.SourceBusinessIdentityHash) &&
           string.Equals(
               input.DriftClassification,
               Rt03ReviewedRetainedContract.PhotoClassification,
               StringComparison.Ordinal) &&
           !string.IsNullOrWhiteSpace(input.ReviewedFieldSet) &&
           IsHash(input.ReviewedSourceFingerprint) &&
           IsHash(input.CurrentSourceFingerprint) &&
           IsHash(input.ReviewedTargetFingerprint) &&
           IsHash(input.CurrentTargetFingerprint) &&
           IsHash(input.ReviewedOwnershipFingerprint) &&
           IsHash(input.CurrentOwnershipFingerprint);

    private static bool ExactHashMatch(string expected, string actual)
        => IsHash(expected) && IsHash(actual) &&
           string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    private static bool IsHash(string? value)
        => value?.Length == 64 && value.All(Uri.IsHexDigit);

    public static string NormalizeFieldSet(string? value)
        => string.Join(",", (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));

    private static string DiagnosticId(
        Rt03ReviewedRetainedInput input,
        Rt03ReviewedRetainedContext context)
        => "RT03-V9-" + Rt03Hash.Sha256(string.Join("|",
            Rt03ReviewedRetainedContract.Version,
            context,
            input.SourceProfileCode,
            input.DomainCode,
            input.SourceBusinessIdentityHash,
            input.TargetIdentity?.ToString(CultureInfo.InvariantCulture) ?? "<NULL>",
            input.ReviewVersion.ToString(CultureInfo.InvariantCulture)))[..16];
}

public static class Rt03ReviewedRetainedFingerprints
{
    public static string SourceBusinessIdentity(string sourceProfileCode, string sourceMaDk)
        => Rt03Hash.Sha256(string.Join("|",
            "RT03-V9-BUSINESS-IDENTITY-v1",
            sourceProfileCode.Trim().ToUpperInvariant(),
            sourceMaDk.Trim().ToUpperInvariant()));

    public static string Source(QlhvImportHocVienWriteModel row)
        => row.V2RowHash;

    public static string Target(Rt01aTargetHocVienRow row)
        => HashFields(
            row.MaDK,
            row.MaKhoa,
            row.TenKhoa,
            row.MaHangDT,
            row.HangGPLXHoc,
            row.HoTen,
            row.NgaySinh?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            row.GioiTinh,
            row.SoCCCD,
            row.DiaChiThuongTru,
            row.SoGPLXDaCo,
            row.HangGPLXDaCo,
            row.NguoiNhanHoSo,
            row.AnhRelativePath,
            "0",
            row.ChatLuongAnh?.ToString(CultureInfo.InvariantCulture),
            row.NgayThuNhanAnh?.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
            row.NguoiThuNhanAnh,
            row.SourceOfTruth);

    public static string Ownership(Rt01aTargetHocVienRow row)
        => HashFields(
            row.GhiChuNoiBo,
            row.DaDoiChieuCccd ? "1" : "0",
            row.DaInThe ? "1" : "0",
            row.DaTaoXml ? "1" : "0",
            row.IsDeleted ? "1" : "0",
            row.CreatedBy,
            row.UpdatedBy,
            row.DeletedBy,
            row.DeleteReason);

    public static string CurrentFieldSet(
        QlhvImportHocVienWriteModel source,
        Rt01aTargetHocVienRow target)
    {
        var fields = new (string Name, string Source, string Target)[]
        {
            ("MaDK", N(source.MaDK), N(target.MaDK)),
            ("MaKhoa", N(source.MaKhoa), N(target.MaKhoa)),
            ("TenKhoa", N(source.TenKhoa), N(target.TenKhoa)),
            ("MaHangDT", N(source.MaHangDT), N(target.MaHangDT)),
            ("HangGPLXHoc", N(source.HangGPLXHoc), N(target.HangGPLXHoc)),
            ("HoTen", N(source.HoTen), N(target.HoTen)),
            ("NgaySinh", Date(source.NgaySinh), Date(target.NgaySinh)),
            ("GioiTinh", N(source.GioiTinh), N(target.GioiTinh)),
            ("SoCCCD", N(source.SoCCCD), N(target.SoCCCD)),
            ("DiaChiThuongTru", N(source.DiaChiThuongTru), N(target.DiaChiThuongTru)),
            ("SoGPLXDaCo", N(source.SoGPLXDaCo), N(target.SoGPLXDaCo)),
            ("HangGPLXDaCo", N(source.HangGPLXDaCo), N(target.HangGPLXDaCo)),
            ("NguoiNhanHoSo", N(source.NguoiNhanHoSo), N(target.NguoiNhanHoSo)),
            ("AnhRelativePath", N(source.AnhRelativePath), N(target.AnhRelativePath)),
            ("ChatLuongAnh", Number(source.ChatLuongAnh), Number(target.ChatLuongAnh)),
            ("NgayThuNhanAnh", Timestamp(source.NgayThuNhanAnh), Timestamp(target.NgayThuNhanAnh)),
            ("NguoiThuNhanAnh", N(source.NguoiThuNhanAnh), N(target.NguoiThuNhanAnh)),
            ("SourceOfTruth", N(source.SourceOfTruth), N(target.SourceOfTruth)),
        };
        return Rt03ReviewedRetainedPolicy.NormalizeFieldSet(string.Join(",",
            fields.Where(field =>
                    !string.Equals(field.Source, field.Target, StringComparison.Ordinal))
                .Select(field => field.Name)));
    }

    public static string Evidence(
        string sourceProfileCode,
        string businessIdentityHash,
        long targetIdentity,
        string fieldSet,
        long reviewedEventVersion,
        long evidenceAnchorVersion,
        string sourceFingerprint,
        string targetFingerprint,
        string ownershipFingerprint)
        => Rt03Hash.Sha256(string.Join("|",
            Rt03ReviewedRetainedContract.Version,
            sourceProfileCode,
            Rt03ReviewedRetainedContract.DomainLearner,
            businessIdentityHash,
            targetIdentity.ToString(CultureInfo.InvariantCulture),
            Rt03ReviewedRetainedContract.PhotoClassification,
            Rt03ReviewedRetainedPolicy.NormalizeFieldSet(fieldSet),
            reviewedEventVersion.ToString(CultureInfo.InvariantCulture),
            evidenceAnchorVersion.ToString(CultureInfo.InvariantCulture),
            sourceFingerprint,
            targetFingerprint,
            ownershipFingerprint));

    private static string HashFields(params string?[] values)
    {
        var canonical = string.Join("|", values.Select(value =>
        {
            var normalized = value ?? string.Empty;
            return $"{normalized.Length}:{normalized}";
        }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static string N(string? value) => value ?? string.Empty;

    private static string Date(DateTime? value)
        => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Number(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Timestamp(DateTime? value)
        => value?.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture) ?? string.Empty;
}

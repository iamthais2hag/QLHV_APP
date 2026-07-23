using System.Globalization;
using System.Text.RegularExpressions;

namespace QLHV.Application.Common;

/// <summary>
/// Recognizes the CSDT identifiers introduced on 2026-07-01 without rewriting
/// or rejecting identifiers produced by the legacy systems.
/// </summary>
public static partial class CsdtCodeFormatRules
{
    public const int MaximumStoredCodeLength = 50;

    public static CsdtCodeFormatKind ClassifyMaCsdt(string? value)
    {
        var code = Normalize(value);
        if (code is null)
        {
            return CsdtCodeFormatKind.Invalid;
        }

        return CurrentMaCsdtRegex().IsMatch(code)
            ? CsdtCodeFormatKind.Current
            : IsStorableLegacyCode(code)
                ? CsdtCodeFormatKind.Legacy
                : CsdtCodeFormatKind.Invalid;
    }

    public static CsdtCodeValidationResult ValidateCourseCode(
        string? value,
        string? expectedMaCsdt = null,
        DateOnly? ngayKhaiGiang = null)
    {
        var code = Normalize(value);
        if (code is null)
        {
            return CsdtCodeValidationResult.Invalid("Ma khoa hoc la bat buoc.");
        }

        var currentMatch = CurrentCourseCodeRegex().Match(code);
        if (!currentMatch.Success)
        {
            return LooksLikeCurrentCourseCode(code)
                ? CsdtCodeValidationResult.Invalid("Ma khoa hoc theo cau truc moi khong hop le.")
                : IsStorableLegacyCode(code)
                    ? CsdtCodeValidationResult.Legacy()
                    : CsdtCodeValidationResult.Invalid("Ma khoa hoc khong the luu an toan.");
        }

        if (currentMatch.Groups["sequence"].Value == "0000")
        {
            return CsdtCodeValidationResult.Invalid("So thu tu khoa hoc khong duoc la 0000.");
        }

        var centerCode = currentMatch.Groups["center"].Value;
        var normalizedExpectedCenter = Normalize(expectedMaCsdt);
        if (normalizedExpectedCenter is not null &&
            !string.Equals(centerCode, normalizedExpectedCenter, StringComparison.Ordinal))
        {
            return CsdtCodeValidationResult.Invalid("Ma khoa hoc khong khop MaCSDT.");
        }

        if (ngayKhaiGiang is not null)
        {
            var expectedYear = (ngayKhaiGiang.Value.Year % 100)
                .ToString("00", CultureInfo.InvariantCulture);
            if (!string.Equals(
                    currentMatch.Groups["year"].Value,
                    expectedYear,
                    StringComparison.Ordinal))
            {
                return CsdtCodeValidationResult.Invalid(
                    "Nam trong ma khoa hoc khong khop ngay khai giang.");
            }
        }

        return CsdtCodeValidationResult.Current();
    }

    public static CsdtCodeValidationResult ValidateStudentCode(
        string? value,
        string? expectedMaCsdt = null)
    {
        var code = Normalize(value);
        if (code is null)
        {
            return CsdtCodeValidationResult.Invalid("Ma hoc vien la bat buoc.");
        }

        var currentMatch = CurrentStudentCodeRegex().Match(code);
        if (!currentMatch.Success)
        {
            return LooksLikeCurrentStudentCode(code)
                ? CsdtCodeValidationResult.Invalid("Ma hoc vien theo cau truc moi khong hop le.")
                : IsStorableLegacyCode(code)
                    ? CsdtCodeValidationResult.Legacy()
                    : CsdtCodeValidationResult.Invalid("Ma hoc vien khong the luu an toan.");
        }

        var centerCode = currentMatch.Groups["center"].Value;
        var normalizedExpectedCenter = Normalize(expectedMaCsdt);
        if (normalizedExpectedCenter is not null &&
            !string.Equals(centerCode, normalizedExpectedCenter, StringComparison.Ordinal))
        {
            return CsdtCodeValidationResult.Invalid("Ma hoc vien khong khop MaCSDT.");
        }

        if (!DateOnly.TryParseExact(
                currentMatch.Groups["date"].Value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            return CsdtCodeValidationResult.Invalid("Ngay trong ma hoc vien khong hop le.");
        }

        if (currentMatch.Groups["sequence"].Value == "000000")
        {
            return CsdtCodeValidationResult.Invalid("So thu tu hoc vien khong duoc la 000000.");
        }

        return CsdtCodeValidationResult.Current();
    }

    public static CsdtCodeValidationResult ValidateCompletionCertificateNumber(
        string? value,
        string? studentCode = null)
    {
        var code = Normalize(value);
        if (code is null)
        {
            return CsdtCodeValidationResult.Invalid("So giay xac nhan la bat buoc.");
        }

        var normalizedStudentCode = Normalize(studentCode);
        if (normalizedStudentCode is null)
        {
            var separator = code.LastIndexOf('-');
            if (separator <= 0)
            {
                return IsStorableLegacyCode(code)
                    ? CsdtCodeValidationResult.Legacy()
                    : CsdtCodeValidationResult.Invalid("So giay xac nhan khong hop le.");
            }

            normalizedStudentCode = code[..separator];
        }

        var studentValidation = ValidateStudentCode(normalizedStudentCode);
        if (!studentValidation.IsValid)
        {
            return LooksLikeCurrentStudentCode(normalizedStudentCode)
                ? CsdtCodeValidationResult.Invalid(
                    "Ma hoc vien trong so giay xac nhan khong hop le.")
                : IsStorableLegacyCode(code)
                    ? CsdtCodeValidationResult.Legacy()
                    : CsdtCodeValidationResult.Invalid("So giay xac nhan khong hop le.");
        }

        var prefix = normalizedStudentCode + "-";
        if (!code.StartsWith(prefix, StringComparison.Ordinal))
        {
            return studentValidation.Format == CsdtCodeFormatKind.Legacy &&
                   IsStorableLegacyCode(code)
                ? CsdtCodeValidationResult.Legacy()
                : CsdtCodeValidationResult.Invalid("So giay xac nhan khong khop ma hoc vien.");
        }

        var trainingClass = code[prefix.Length..];
        if (!TrainingClassRegex().IsMatch(trainingClass))
        {
            return CsdtCodeValidationResult.Invalid(
                "Hang dao tao phai co tu 1 den 3 ky tu.");
        }

        return studentValidation.Format == CsdtCodeFormatKind.Current
            ? CsdtCodeValidationResult.Current()
            : CsdtCodeValidationResult.Legacy();
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static bool IsStorableLegacyCode(string code)
        => code.Length <= MaximumStoredCodeLength &&
           code.IndexOfAny(['\0', '\r', '\n']) < 0;

    private static bool LooksLikeCurrentCourseCode(string code)
        => CurrentCoursePrefixRegex().IsMatch(code);

    private static bool LooksLikeCurrentStudentCode(string code)
        => CurrentStudentPrefixRegex().IsMatch(code);

    [GeneratedRegex(@"^\d{5}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrentMaCsdtRegex();

    [GeneratedRegex(
        @"^(?<center>\d{5})K(?<year>\d{2})(?<sequence>\d{4})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CurrentCourseCodeRegex();

    [GeneratedRegex(
        @"^(?<center>\d{5})-(?<date>\d{8})-(?<sequence>\d{6})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CurrentStudentCodeRegex();

    [GeneratedRegex(@"^\d{5}K", RegexOptions.CultureInvariant)]
    private static partial Regex CurrentCoursePrefixRegex();

    [GeneratedRegex(@"^\d{5}-", RegexOptions.CultureInvariant)]
    private static partial Regex CurrentStudentPrefixRegex();

    [GeneratedRegex(@"^[\p{L}\p{N}]{1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex TrainingClassRegex();
}

public enum CsdtCodeFormatKind
{
    Invalid = 0,
    Legacy = 1,
    Current = 2,
}

public sealed record CsdtCodeValidationResult(
    bool IsValid,
    CsdtCodeFormatKind Format,
    string? Error)
{
    public static CsdtCodeValidationResult Current()
        => new(true, CsdtCodeFormatKind.Current, null);

    public static CsdtCodeValidationResult Legacy()
        => new(true, CsdtCodeFormatKind.Legacy, null);

    public static CsdtCodeValidationResult Invalid(string error)
        => new(false, CsdtCodeFormatKind.Invalid, error);
}

using System.Text.RegularExpressions;

namespace QLHV.Application.Sync.Realtime;

/// <summary>
/// Validates raw CSDT identities. These methods never trim, normalize casing,
/// remove separators, or generate replacement values.
/// </summary>
public static partial class CsdtRealtimeIdentityRules
{
    public const int MaximumLegacyCodeLength = 50;
    public const int MaximumCompletionCertificateLength = 30;

    public static bool IsCurrentMaCsdt(string? value)
        => value is not null && CurrentMaCsdtRegex().IsMatch(value);

    public static bool IsCurrentCourseCode(string? value, string? expectedMaCsdt = null)
    {
        if (value is null)
        {
            return false;
        }

        var match = CurrentCourseCodeRegex().Match(value);
        if (!match.Success)
        {
            return false;
        }

        return expectedMaCsdt is null ||
               string.Equals(match.Groups["center"].Value, expectedMaCsdt, StringComparison.Ordinal);
    }

    public static bool IsCurrentStudentCode(string? value, string? expectedMaCsdt = null)
    {
        if (value is null)
        {
            return false;
        }

        var match = CurrentStudentCodeRegex().Match(value);
        if (!match.Success)
        {
            return false;
        }

        return expectedMaCsdt is null ||
               string.Equals(match.Groups["center"].Value, expectedMaCsdt, StringComparison.Ordinal);
    }

    public static bool IsExactCompletionCertificate(
        string? value,
        string? maDk,
        string? hangDaoTao)
    {
        if (value is null ||
            maDk is null ||
            hangDaoTao is null ||
            !IsCurrentStudentCode(maDk) ||
            !TrainingClassRegex().IsMatch(hangDaoTao) ||
            value.Length > MaximumCompletionCertificateLength)
        {
            return false;
        }

        return string.Equals(value, string.Concat(maDk, "-", hangDaoTao), StringComparison.Ordinal);
    }

    public static bool IsRawCourseCodeOrStorableLegacy(
        string? value,
        string? expectedMaCsdt = null)
    {
        if (value is null || value.Length == 0)
        {
            return false;
        }

        if (IsCurrentCourseCode(value, expectedMaCsdt))
        {
            return true;
        }

        // A value that looks like the current format but fails its exact rules is
        // malformed current data, not a legacy identifier.
        return !CurrentCoursePrefixRegex().IsMatch(value) && IsStorableLegacy(value);
    }

    public static bool IsRawStudentCodeOrStorableLegacy(
        string? value,
        string? expectedMaCsdt = null)
    {
        if (value is null || value.Length == 0)
        {
            return false;
        }

        if (IsCurrentStudentCode(value, expectedMaCsdt))
        {
            return true;
        }

        return !CurrentStudentPrefixRegex().IsMatch(value) && IsStorableLegacy(value);
    }

    public static void RequireStateToken(string? stateToken, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(stateToken) ||
            stateToken.Length > 512 ||
            char.IsWhiteSpace(stateToken[0]) ||
            char.IsWhiteSpace(stateToken[^1]) ||
            stateToken.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new ArgumentException("ExpectedStateToken khong hop le.", parameterName);
        }
    }

    public static void RequirePlanToken(string? planToken, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(planToken) ||
            planToken.Length > 512 ||
            char.IsWhiteSpace(planToken[0]) ||
            char.IsWhiteSpace(planToken[^1]) ||
            planToken.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new ArgumentException("ExpectedPlanToken khong hop le.", parameterName);
        }
    }

    private static bool IsStorableLegacy(string value)
        => value.Length <= MaximumLegacyCodeLength &&
           value.IndexOfAny(['\0', '\r', '\n']) < 0 &&
           !char.IsWhiteSpace(value[0]) &&
           !char.IsWhiteSpace(value[^1]);

    [GeneratedRegex(@"^[0-9]{5}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrentMaCsdtRegex();

    [GeneratedRegex(
        @"^(?<center>[0-9]{5})K(?<year>[0-9]{2})(?<sequence>[0-9]{4})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CurrentCourseCodeRegex();

    [GeneratedRegex(
        @"^(?<center>[0-9]{5})-(?<date>[0-9]{8})-(?<sequence>[0-9]{6})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CurrentStudentCodeRegex();

    [GeneratedRegex(@"^[0-9]{5}K", RegexOptions.CultureInvariant)]
    private static partial Regex CurrentCoursePrefixRegex();

    [GeneratedRegex(@"^[0-9]{5}-", RegexOptions.CultureInvariant)]
    private static partial Regex CurrentStudentPrefixRegex();

    [GeneratedRegex(@"^[\p{L}\p{N}]{1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex TrainingClassRegex();
}

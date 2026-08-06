using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using QLHV.Application.CsdtConnections;

namespace QLHV.Application.Assignments;

public static partial class AssignmentRules
{
    public const int MaxImportRows = 5_000;
    public const long MaxImportBytes = 10 * 1024 * 1024;
    public static readonly TimeSpan PreviewTtl = TimeSpan.FromMinutes(15);

    public static string NormalizeRequired(string? value, int maxLength, string field)
    {
        var normalized = NormalizeOptional(value, maxLength);
        if (normalized is null)
        {
            throw new AssignmentDomainException("INVALID", $"{field} là bắt buộc.");
        }

        return normalized;
    }

    public static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = WhitespaceRegex().Replace(value.Normalize(NormalizationForm.FormC).Trim(), " ");
        if (normalized.Length > maxLength)
        {
            throw new AssignmentDomainException("INVALID", $"Giá trị vượt quá {maxLength} ký tự.");
        }

        return normalized;
    }

    public static string? NormalizeProfile(string? value, bool required)
    {
        var normalized = NormalizeOptional(value, 50)?.ToUpperInvariant();
        if (required && normalized is null)
        {
            throw new AssignmentDomainException("INVALID", "SourceProfileCode là bắt buộc.");
        }

        if (normalized is not null &&
            normalized is not (CsdtConnectionProfileCodes.CsdtOto or CsdtConnectionProfileCodes.CsdtMoto))
        {
            throw new AssignmentDomainException(
                "INVALID",
                "SourceProfileCode chỉ hỗ trợ CSDT_OTO hoặc CSDT_MOTO.");
        }

        return normalized;
    }

    public static bool RequiresBulkPermission(AssignmentPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return string.Equals(request.Selection.Mode,"FILTER",StringComparison.OrdinalIgnoreCase) ||
            request.Selection.HocVienIds.Count>1 ||
            string.Equals(request.Operation,AssignmentOperation.BulkAssign,StringComparison.OrdinalIgnoreCase) ||
            string.Equals(request.Operation,AssignmentOperation.PutInGroup,StringComparison.OrdinalIgnoreCase);
    }

    public static bool RequiresBulkGroupPermission(string? mode)=>
        !string.Equals(mode?.Trim(),GroupPropagationMode.NoCurrentChange,StringComparison.OrdinalIgnoreCase);

    public static string NormalizeVehiclePlate(string? value) =>
        VehicleSeparatorRegex().Replace(
            NormalizeRequired(value, 20, "Biển số xe").ToUpperInvariant(),
            string.Empty);

    public static string NormalizeReason(string? value) =>
        NormalizeRequired(value, 500, "Lý do");

    public static string NormalizeSearchName(string value)
    {
        var decomposed = NormalizeRequired(value, 255, "Họ tên")
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character is 'đ' ? 'd' : character is 'Đ' ? 'D' : character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }

    public static byte[] ParseRowVersion(string? value, bool allowEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (allowEmpty)
            {
                return [];
            }

            throw new AssignmentDomainException("CONFLICT", "Thiếu RowVersion.", 409);
        }

        try
        {
            var bytes = Convert.FromBase64String(value.Trim());
            if (bytes.Length != 8)
            {
                throw new FormatException();
            }

            return bytes;
        }
        catch (FormatException)
        {
            throw new AssignmentDomainException("CONFLICT", "RowVersion không hợp lệ.", 409);
        }
    }

    public static string RowVersionToString(byte[]? value) =>
        value is { Length: > 0 } ? Convert.ToBase64String(value) : string.Empty;

    public static string ComputeFingerprint(IEnumerable<string> components)
    {
        var canonical = string.Join("\n", components);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string NeutralizeFormula(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var trimmedStart = value.TrimStart();
        return trimmedStart.Length > 0 && trimmedStart[0] is '=' or '+' or '-' or '@'
            ? "'" + value
            : value;
    }

    public static bool IsFormula(string? value)
    {
        var trimmed = value?.TrimStart();
        return !string.IsNullOrEmpty(trimmed) && trimmed[0] is '=' or '+' or '-' or '@';
    }

    public static DateOnly? ToDateOnly(DateTime? value) =>
        value.HasValue ? DateOnly.FromDateTime(value.Value) : null;

    public static string ToInvariant(long value) => value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[\s.\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex VehicleSeparatorRegex();
}

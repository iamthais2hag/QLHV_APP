namespace QLHV.Application.HocVien;

public static class HocVienGender
{
    public const string MaleSourceValue = "M";
    public const string FemaleSourceValue = "F";
    public const string MaleDisplayValue = "Nam";
    public const string FemaleDisplayValue = "Nữ";

    public static string? NormalizeFilterValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (IsAllValue(trimmed))
        {
            return null;
        }

        if (string.Equals(trimmed, MaleDisplayValue, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, MaleSourceValue, StringComparison.OrdinalIgnoreCase))
        {
            return MaleSourceValue;
        }

        if (string.Equals(trimmed, FemaleDisplayValue, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Nu", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, FemaleSourceValue, StringComparison.OrdinalIgnoreCase))
        {
            return FemaleSourceValue;
        }

        return trimmed;
    }

    public static string? ToDisplayValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, MaleSourceValue, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, MaleDisplayValue, StringComparison.OrdinalIgnoreCase))
        {
            return MaleDisplayValue;
        }

        if (string.Equals(trimmed, FemaleSourceValue, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, FemaleDisplayValue, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Nu", StringComparison.OrdinalIgnoreCase))
        {
            return FemaleDisplayValue;
        }

        return trimmed;
    }

    private static bool IsAllValue(string value) =>
        string.Equals(value, "Tất cả", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "Tat ca", StringComparison.OrdinalIgnoreCase);
}

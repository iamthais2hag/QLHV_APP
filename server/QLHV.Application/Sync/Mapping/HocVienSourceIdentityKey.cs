namespace QLHV.Application.Sync.Mapping;

public static class HocVienSourceIdentityKey
{
    public static string Create(string sourceProfileCode, string sourceMaDK)
    {
        var profile = Require(sourceProfileCode, nameof(sourceProfileCode)).ToUpperInvariant();
        var key = Require(sourceMaDK, nameof(sourceMaDK));
        return $"{profile}::{key}";
    }

    private static string Require(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", name)
            : value.Trim();
}


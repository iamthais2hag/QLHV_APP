namespace QLHV.Application.Auth;

public static class AppRoles
{
    public const string Admin = "Admin";

    public const string Viewer = "Viewer";

    public static bool IsKnown(string? role) =>
        string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, Viewer, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string role) =>
        string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase)
            ? Admin
            : string.Equals(role, Viewer, StringComparison.OrdinalIgnoreCase)
                ? Viewer
                : throw new ArgumentException("Role is not supported.", nameof(role));

    public static string? SelectPrimary(IEnumerable<string> roles)
    {
        var normalized = roles
            .Where(IsKnown)
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return normalized.Contains(Admin, StringComparer.Ordinal)
            ? Admin
            : normalized.Contains(Viewer, StringComparer.Ordinal)
                ? Viewer
                : null;
    }
}

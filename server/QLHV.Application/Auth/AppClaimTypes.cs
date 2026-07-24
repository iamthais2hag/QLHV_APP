namespace QLHV.Application.Auth;

public static class AppClaimTypes
{
    public const string MustChangePassword = "qlhv:must-change-password";

    public const string SecurityStamp = "qlhv:security-stamp";

    public const string TrueValue = "true";

    public const string FalseValue = "false";

    public static string ToClaimValue(bool value) => value ? TrueValue : FalseValue;

    public static string ToClaimValue(Guid value) => value.ToString("D");
}

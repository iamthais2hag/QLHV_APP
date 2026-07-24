namespace QLHV.Application.Auth;

public static class AuthPolicies
{
    public const string RequireAdmin = "RequireAdmin";

    public const string CanManageUsers = "CanManageUsers";

    public const string CanSynchronizeCSDT = "CanSynchronizeCSDT";

    public const string CanImportData = "CanImportData";

    public const string CanEditBusinessData = "CanEditBusinessData";

    public const string CanViewBusinessData = "CanViewBusinessData";

    // Compatibility aliases for code and extensions that have not yet moved to
    // the capability-based policy names.
    public const string Read = CanViewBusinessData;

    public const string Admin = RequireAdmin;
}

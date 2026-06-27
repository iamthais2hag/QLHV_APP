namespace QLHV.Application.CsdtConnections;

public static class CsdtConnectionProfileCodes
{
    public const string CsdtMoto = "CSDT_MOTO";
    public const string CsdtOto = "CSDT_OTO";
    public const string CsdtMotoGplx = "CSDT_MOTO_GPLX";
    public const string CsdtOtoGplx = "CSDT_OTO_GPLX";
    public const string DataV1 = "DATA_V1";
    public const string DataV2 = "DATA_V2";
    public const string QlhvApp = "QLHV_APP";

    public static readonly IReadOnlySet<string> FixedProfiles = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        CsdtMoto,
        CsdtOto,
        CsdtMotoGplx,
        CsdtOtoGplx,
        DataV1,
        DataV2,
        QlhvApp,
    };

    public static bool IsFixedProfile(string? profileCode)
        => !string.IsNullOrWhiteSpace(profileCode) && FixedProfiles.Contains(profileCode.Trim());

    public static string Normalize(string profileCode) => profileCode.Trim().ToUpperInvariant();
}

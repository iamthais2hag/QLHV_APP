namespace QLHV.Application.Sync.Mapping;

/// <summary>
/// Source ownership context used when writing imported HocVien rows.
/// DATA_V1/DATA_V2 are technical import profiles, not Moto/Oto business groups.
/// </summary>
public sealed class HocVienSourceIdentityContext
{
    public const string DataV1ProfileCode = "DATA_V1";
    public const string DataV2ProfileCode = "DATA_V2";

    public static readonly HocVienSourceIdentityContext DataV1 = new(
        DataV1ProfileCode,
        QLHV.Application.Sync.Connections.SourceSystem.V1.ToString());

    public static readonly HocVienSourceIdentityContext DataV2 = new(
        DataV2ProfileCode,
        QLHV.Application.Sync.Connections.SourceSystem.V2.ToString());

    public HocVienSourceIdentityContext(
        string sourceProfileCode,
        string sourceSystem,
        string? sourceVersion = null)
    {
        SourceProfileCode = Require(sourceProfileCode, nameof(sourceProfileCode)).ToUpperInvariant();
        SourceSystem = Require(sourceSystem, nameof(sourceSystem)).ToUpperInvariant();
        SourceVersion = string.IsNullOrWhiteSpace(sourceVersion) ? null : sourceVersion.Trim();
    }

    public string SourceProfileCode { get; }

    public string SourceSystem { get; }

    public string? SourceVersion { get; }

    public string CreateKey(string sourceMaDK)
        => HocVienSourceIdentityKey.Create(SourceProfileCode, sourceMaDK);

    private static string Require(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", name)
            : value.Trim();
}

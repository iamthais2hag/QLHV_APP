using QLHV.Application.CsdtConnections;

namespace QLHV.Application.Sync;

public sealed record QlhvOperationSourceDefinition(
    string SourceType,
    string LiveDatabaseName,
    string BackupDatabaseName,
    string MaCsdt,
    string SourceProfileCode,
    string LiveProfileCode,
    string BackupReadProfileCode,
    string LockResource);

public static class QlhvOperationSourceCatalog
{
    private static readonly IReadOnlyDictionary<string, QlhvOperationSourceDefinition> Sources =
        new Dictionary<string, QlhvOperationSourceDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["OTO"] = new(
                "OTO",
                "CSDL_OTO",
                "CSDL_OTO_BAK",
                "66029",
                CsdtConnectionProfileCodes.CsdtOto,
                CsdtConnectionProfileCodes.CsdtOto,
                CsdtConnectionProfileCodes.CsdtOtoBak,
                "QLHV:CSDT_OPERATIONS:OTO"),
            ["MOTO"] = new(
                "MOTO",
                "CSDL_MOTO",
                "CSDL_MOTO_BAK",
                "66030",
                CsdtConnectionProfileCodes.CsdtMoto,
                CsdtConnectionProfileCodes.CsdtMoto,
                CsdtConnectionProfileCodes.CsdtMotoBak,
                "QLHV:CSDT_OPERATIONS:MOTO"),
        };

    private static readonly IReadOnlyCollection<QlhvOperationSourceDefinition> AllSources =
        Sources.Values.ToArray();

    public static IReadOnlyCollection<QlhvOperationSourceDefinition> All => AllSources;

    public static bool TryGet(string? sourceType, out QlhvOperationSourceDefinition definition)
    {
        definition = default!;
        return !string.IsNullOrWhiteSpace(sourceType) &&
               Sources.TryGetValue(sourceType.Trim(), out definition!);
    }

    public static QlhvOperationSourceDefinition GetRequired(string? sourceType)
        => TryGet(sourceType, out var definition)
            ? definition
            : throw new ArgumentException("SourceType chi ho tro OTO hoac MOTO.", nameof(sourceType));

    public static string ResolveSourceTypeFromProfile(string? sourceProfileCode)
    {
        var normalized = sourceProfileCode?.Trim().ToUpperInvariant();
        return normalized switch
        {
            CsdtConnectionProfileCodes.CsdtOto => "OTO",
            CsdtConnectionProfileCodes.CsdtMoto => "MOTO",
            _ => throw new ArgumentException(
                "SourceProfileCode chi ho tro CSDT_OTO hoac CSDT_MOTO.",
                nameof(sourceProfileCode)),
        };
    }
}

public static class QlhvOperationTypes
{
    public const string RefreshBackup = "REFRESH_BACKUP";
    public const string FullSync = "FULL_SYNC";

    public const string Queued = "QUEUED";
    public const string Running = "RUNNING";
    public const string Succeeded = "SUCCEEDED";
    public const string Failed = "FAILED";
}

public static class QlhvOperationActors
{
    public const string ManualAdmin = "MANUAL_ADMIN";
    public const string SystemAutoSync = "SYSTEM_AUTO_SYNC";
    public const string SystemSessionStart = "SYSTEM_SESSION_START";

    public static string NormalizeInternal(string? actor)
        => actor?.Trim().ToUpperInvariant() switch
        {
            SystemAutoSync => SystemAutoSync,
            SystemSessionStart => SystemSessionStart,
            _ => ManualAdmin,
        };
}

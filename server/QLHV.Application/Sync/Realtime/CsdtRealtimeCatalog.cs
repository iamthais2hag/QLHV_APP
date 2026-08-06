namespace QLHV.Application.Sync.Realtime;

public static class CsdtRealtimeVehicleTypes
{
    public const string Oto = "OTO";
    public const string Moto = "MOTO";
}

public static class CsdtRealtimeStreamCodes
{
    public const string OtoV2ToV1 = "OTO_V2_TO_V1";
    public const string MotoV2ToV1 = "MOTO_V2_TO_V1";
}

public static class CsdtRealtimeProfileCodes
{
    public const string OtoV2 = "OTO_V2";
    public const string OtoV1 = "OTO_V1";
    public const string MotoV2 = "MOTO_V2";
    public const string MotoV1 = "MOTO_V1";
    public const string OtoV2Bak = "OTO_V2_BAK";
    public const string OtoV1Bak = "OTO_V1_BAK";
    public const string MotoV2Bak = "MOTO_V2_BAK";
    public const string MotoV1Bak = "MOTO_V1_BAK";
}

public static class CsdtRealtimeDatabaseNames
{
    public const string OtoV2 = "CSDL_OTO";
    public const string OtoV1 = "CSDL_OTO_V1";
    public const string MotoV2 = "CSDL_MOTO";
    public const string MotoV1 = "CSDL_MOTO_V1";
    public const string OtoV2Bak = "CSDL_OTO_BAK";
    public const string OtoV1Bak = "CSDL_OTO_V1_BAK";
    public const string MotoV2Bak = "CSDL_MOTO_BAK";
    public const string MotoV1Bak = "CSDL_MOTO_V1_BAK";
}

public sealed record CsdtRealtimeRouteDefinition(
    string StreamCode,
    string VehicleType,
    string SourceProfileCode,
    string TargetProfileCode,
    string SourceDatabaseName,
    string TargetDatabaseName,
    string MaCSDT,
    bool IsBackup,
    string Direction = "V2_TO_V1")
{
    public CsdtRealtimeRouteDefinition Reverse()
        => this with
        {
            SourceProfileCode = TargetProfileCode,
            TargetProfileCode = SourceProfileCode,
            SourceDatabaseName = TargetDatabaseName,
            TargetDatabaseName = SourceDatabaseName,
            Direction = CsdtRealtimeDirections.V1ToV2,
        };
}

/// <summary>
/// Complete server-owned route allowlist. No API request is allowed to supply a
/// server or database name.
/// </summary>
public static class CsdtRealtimeStreamCatalog
{
    private static readonly CsdtRealtimeRouteDefinition OtoLive = new(
        CsdtRealtimeStreamCodes.OtoV2ToV1,
        CsdtRealtimeVehicleTypes.Oto,
        CsdtRealtimeProfileCodes.OtoV2,
        CsdtRealtimeProfileCodes.OtoV1,
        CsdtRealtimeDatabaseNames.OtoV2,
        CsdtRealtimeDatabaseNames.OtoV1,
        "66029",
        false);

    private static readonly CsdtRealtimeRouteDefinition MotoLive = new(
        CsdtRealtimeStreamCodes.MotoV2ToV1,
        CsdtRealtimeVehicleTypes.Moto,
        CsdtRealtimeProfileCodes.MotoV2,
        CsdtRealtimeProfileCodes.MotoV1,
        CsdtRealtimeDatabaseNames.MotoV2,
        CsdtRealtimeDatabaseNames.MotoV1,
        "66030",
        false);

    private static readonly CsdtRealtimeRouteDefinition OtoBackup = new(
        CsdtRealtimeStreamCodes.OtoV2ToV1,
        CsdtRealtimeVehicleTypes.Oto,
        CsdtRealtimeProfileCodes.OtoV2Bak,
        CsdtRealtimeProfileCodes.OtoV1Bak,
        CsdtRealtimeDatabaseNames.OtoV2Bak,
        CsdtRealtimeDatabaseNames.OtoV1Bak,
        "66029",
        true);

    private static readonly CsdtRealtimeRouteDefinition MotoBackup = new(
        CsdtRealtimeStreamCodes.MotoV2ToV1,
        CsdtRealtimeVehicleTypes.Moto,
        CsdtRealtimeProfileCodes.MotoV2Bak,
        CsdtRealtimeProfileCodes.MotoV1Bak,
        CsdtRealtimeDatabaseNames.MotoV2Bak,
        CsdtRealtimeDatabaseNames.MotoV1Bak,
        "66030",
        true);

    private static readonly IReadOnlyDictionary<string, CsdtRealtimeRouteDefinition> LiveByStream =
        new Dictionary<string, CsdtRealtimeRouteDefinition>(StringComparer.Ordinal)
        {
            [OtoLive.StreamCode] = OtoLive,
            [MotoLive.StreamCode] = MotoLive,
        };

    private static readonly IReadOnlyDictionary<string, CsdtRealtimeRouteDefinition> LiveByVehicle =
        new Dictionary<string, CsdtRealtimeRouteDefinition>(StringComparer.Ordinal)
        {
            [OtoLive.VehicleType] = OtoLive,
            [MotoLive.VehicleType] = MotoLive,
        };

    private static readonly IReadOnlyList<CsdtRealtimeRouteDefinition> AllRoutes =
        [OtoLive, MotoLive, OtoBackup, MotoBackup];

    public static IReadOnlyList<CsdtRealtimeRouteDefinition> LiveRoutes { get; } =
        [OtoLive, MotoLive];

    public static IReadOnlyList<CsdtRealtimeRouteDefinition> BackupRoutes { get; } =
        [OtoBackup, MotoBackup];

    public static CsdtRealtimeRouteDefinition GetLiveByStream(string streamCode)
        => LiveByStream.TryGetValue(streamCode, out var route)
            ? route
            : throw new ArgumentException(
                "StreamCode chi ho tro OTO_V2_TO_V1 hoac MOTO_V2_TO_V1.",
                nameof(streamCode));

    public static CsdtRealtimeRouteDefinition GetLiveByVehicle(string vehicleType)
        => LiveByVehicle.TryGetValue(vehicleType, out var route)
            ? route
            : throw new ArgumentException(
                "VehicleType chi ho tro OTO hoac MOTO va phai dung dung chu hoa.",
                nameof(vehicleType));

    public static bool TryResolveAllowedRoute(
        string streamCode,
        string sourceProfileCode,
        string targetProfileCode,
        out CsdtRealtimeRouteDefinition route)
    {
        route = AllRoutes.FirstOrDefault(candidate =>
            string.Equals(candidate.StreamCode, streamCode, StringComparison.Ordinal) &&
            string.Equals(candidate.SourceProfileCode, sourceProfileCode, StringComparison.Ordinal) &&
            string.Equals(candidate.TargetProfileCode, targetProfileCode, StringComparison.Ordinal))!;
        return route is not null;
    }

    public static bool IsAllowedProfile(string profileCode)
        => AllRoutes.Any(route =>
            string.Equals(route.SourceProfileCode, profileCode, StringComparison.Ordinal) ||
            string.Equals(route.TargetProfileCode, profileCode, StringComparison.Ordinal));

    public static IReadOnlyList<CsdtRealtimeRouteDefinition> GetConfiguredRoutes(
        CsdtRealtimeSyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Streams is null)
        {
            throw new ArgumentException("CsdtRealtimeSync:Streams la bat buoc.", nameof(options));
        }

        return
        [
            ResolveConfigured(
                CsdtRealtimeStreamCodes.OtoV2ToV1,
                options.Streams.Oto,
                options.UseBackupProfiles),
            ResolveConfigured(
                CsdtRealtimeStreamCodes.MotoV2ToV1,
                options.Streams.Moto,
                options.UseBackupProfiles),
        ];
    }

    private static CsdtRealtimeRouteDefinition ResolveConfigured(
        string streamCode,
        CsdtRealtimeStreamOptions stream,
        bool useBackupProfiles)
    {
        if (!TryResolveAllowedRoute(
                streamCode,
                stream.SourceProfile,
                stream.TargetProfile,
                out var route) ||
            !string.Equals(stream.StreamCode, streamCode, StringComparison.Ordinal) ||
            route.IsBackup != useBackupProfiles ||
            !string.Equals(route.MaCSDT, stream.MaCSDT, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Stream {streamCode} khong khop allowlist live/BAK co dinh.",
                nameof(stream));
        }

        return route;
    }
}

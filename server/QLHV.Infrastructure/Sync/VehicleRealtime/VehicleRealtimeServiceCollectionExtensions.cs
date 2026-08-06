using Microsoft.Extensions.DependencyInjection;
using QLHV.Application.Sync.VehicleRealtime;

namespace QLHV.Infrastructure.Sync.VehicleRealtime;

/// <summary>
/// Explicit composition hook for the standalone realtime worker. The current
/// task intentionally does not call this hook or add a hosted service: source CT,
/// target migration, sealed baseline and operator activation must happen first.
/// </summary>
public static class VehicleRealtimeServiceCollectionExtensions
{
    public static IServiceCollection AddVehicleRealtimeIngestionCore(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IVehicleRealtimeSourceFeed, SqlVehicleRealtimeSourceFeed>();
        services.AddScoped<IVehicleRealtimeTargetStore, SqlVehicleRealtimeTargetStore>();
        services.AddScoped<IVehicleFullConvergenceTargetStore,
            SqlVehicleFullConvergenceTargetStore>();
        services.AddScoped<VehicleRealtimeCycleProcessor>();
        return services;
    }
}

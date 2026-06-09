using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QLHV.Application.HocVien;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Infrastructure.HocVien;
using QLHV.Infrastructure.Sync;

namespace QLHV.Infrastructure;

/// <summary>Registers Infrastructure services: data access, sync foundations, Hangfire/Polly structure.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IHocVienRepository, HocVienRepository>();

        services.Configure<SyncOptions>(configuration.GetSection(SyncOptions.SectionName));

        services.AddSingleton<IConnectionSettingsProvider, ServerConnectionSettingsProvider>();
        services.AddSingleton<ISyncConnectionProvider, SyncConnectionProvider>();
        services.AddScoped<IV2HocVienSourceRepository, V2HocVienSourceRepository>();
        services.AddScoped<IQlhvHocVienTargetRepository, QlhvHocVienTargetRepository>();
        services.AddScoped<IHocVienSyncJob, HocVienSyncJob>();

        return services;
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QLHV.Application.HocVien;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Infrastructure.HocVien;
using QLHV.Infrastructure.Sync;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Infrastructure;

/// <summary>Registers Infrastructure services: data access, sync foundations, Hangfire/Polly structure.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string? contentRootPath = null)
    {
        services.AddScoped<IHocVienRepository, HocVienRepository>();

        services.Configure<AppSyncOptions>(configuration.GetSection(AppSyncOptions.SectionName));
        services.Configure<SyncExecutionOptions>(configuration.GetSection(SyncExecutionOptions.SectionName));
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        if (!string.IsNullOrWhiteSpace(contentRootPath))
        {
            services.PostConfigure<FileStorageOptions>(options =>
            {
                options.ContentRootPath = contentRootPath;
            });
        }

        services.AddSingleton<IConnectionSettingsProvider, ServerConnectionSettingsProvider>();
        services.AddSingleton<HocVienPhotoPathResolver>();
        services.AddScoped<IHocVienPhotoService, HocVienPhotoService>();
        services.AddSingleton<ISyncConnectionProvider, SyncConnectionProvider>();
        services.AddScoped<IV2HocVienSourceRepository, V2HocVienSourceRepository>();
        services.AddScoped<IQlhvHocVienTargetRepository, QlhvHocVienTargetRepository>();
        services.AddScoped<ISyncRunLogWriter, SyncRunLogWriter>();
        services.AddScoped<IHocVienSyncJob, HocVienSyncJob>();

        return services;
    }
}

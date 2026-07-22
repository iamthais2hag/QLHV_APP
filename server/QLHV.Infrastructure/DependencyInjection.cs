using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QLHV.Application.Auth;
using QLHV.Application.CsdtConnections;
using QLHV.Application.HocVien;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Infrastructure.Auth;
using QLHV.Infrastructure.CsdtConnections;
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
        services.AddScoped<IAppUserRepository, AppUserRepository>();
        services.AddScoped<ICsdtConnectionProfileRepository, CsdtConnectionProfileRepository>();
        services.AddScoped<IHocVienRepository, HocVienRepository>();

        services.Configure<AppSyncOptions>(configuration.GetSection(AppSyncOptions.SectionName));
        services.Configure<SyncExecutionOptions>(configuration.GetSection(SyncExecutionOptions.SectionName));
        services.Configure<QlhvOperationsOptions>(configuration.GetSection(QlhvOperationsOptions.SectionName));
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        if (!string.IsNullOrWhiteSpace(contentRootPath))
        {
            services.PostConfigure<FileStorageOptions>(options =>
            {
                options.ContentRootPath = contentRootPath;
            });
        }

        services.AddSingleton<IConnectionSettingsProvider, ServerConnectionSettingsProvider>();
        services.AddSingleton<IConnectionPasswordProtector, UnavailableConnectionPasswordProtector>();
        services.AddSingleton<ICsdtConnectionTester, SqlServerCsdtConnectionTester>();
        services.AddSingleton<HocVienPhotoPathResolver>();
        services.AddScoped<IHocVienPhotoService, HocVienPhotoService>();
        services.AddSingleton<ISyncConnectionProvider, SyncConnectionProvider>();
        services.AddScoped<IV2HocVienSourceRepository, V2HocVienSourceRepository>();
        services.AddScoped<QlhvHocVienTargetRepository>();
        services.AddScoped<IQlhvHocVienTargetRepository>(provider =>
            provider.GetRequiredService<QlhvHocVienTargetRepository>());
        services.AddScoped<IQlhvImportWriteRepository>(provider =>
            provider.GetRequiredService<QlhvHocVienTargetRepository>());
        services.AddScoped<IQlhvImportReadRepository, QlhvImportReadRepository>();
        services.AddScoped<QlhvOperationConnectionResolver>();
        services.AddScoped<IQlhvOperationsRepository, QlhvOperationsRepository>();
        services.AddScoped<IQlhvOperationHistoryRepository, QlhvOperationHistoryRepository>();
        services.AddScoped<IQlhvSourceOperationLock, QlhvSqlSourceOperationLock>();
        services.AddScoped<IQlhvBackupRefreshExecutor, QlhvBackupRefreshExecutor>();
        services.AddSingleton<QlhvRefreshBackupQueue>();
        services.AddSingleton<IQlhvRefreshBackupQueue>(provider =>
            provider.GetRequiredService<QlhvRefreshBackupQueue>());
        services.AddHostedService<QlhvRefreshBackupWorker>();
        services.AddScoped<IMotoSyncRepository, MotoSyncRepository>();
        services.AddScoped<IMotoSyncRunHistoryRepository, MotoSyncRunHistoryRepository>();
        services.AddScoped<IMotoCenterTransferRunHistoryRepository, MotoCenterTransferRunHistoryRepository>();
        services.AddScoped<IHocVienSourceAttributionDiagnosticsRepository, HocVienSourceAttributionDiagnosticsRepository>();
        services.AddScoped<ISyncRunLogWriter, SyncRunLogWriter>();
        services.AddScoped<IHocVienSyncJob, HocVienSyncJob>();

        return services;
    }
}

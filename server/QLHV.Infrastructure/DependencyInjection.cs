using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QLHV.Application.Auth;
using QLHV.Application.CsdtConnections;
using QLHV.Application.HocVien;
using QLHV.Application.HocVien.Photos;
using QLHV.Application.Runtime;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.SystemData;
using QLHV.Infrastructure.Auth;
using QLHV.Infrastructure.CsdtConnections;
using QLHV.Infrastructure.HocVien;
using QLHV.Infrastructure.HocVien.Photos;
using QLHV.Infrastructure.Runtime;
using QLHV.Infrastructure.Sync;
using QLHV.Infrastructure.SystemData;
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
        services.AddScoped<AppUserRepository>();
        services.AddScoped<IAppUserRepository>(provider =>
            provider.GetRequiredService<AppUserRepository>());
        services.AddScoped<IAppUserManagementRepository>(provider =>
            provider.GetRequiredService<AppUserRepository>());
        services.AddScoped<ICsdtConnectionProfileRepository, CsdtConnectionProfileRepository>();
        services.AddScoped<IHocVienRepository, HocVienRepository>();

        services.Configure<AppSyncOptions>(configuration.GetSection(AppSyncOptions.SectionName));
        services.Configure<SyncExecutionOptions>(configuration.GetSection(SyncExecutionOptions.SectionName));
        services.Configure<QlhvOperationsOptions>(configuration.GetSection(QlhvOperationsOptions.SectionName));
        services.Configure<QlhvAutoSyncOptions>(configuration.GetSection(QlhvAutoSyncOptions.SectionName));
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.Configure<HocVienPhotoProcessingOptions>(
            configuration.GetSection(HocVienPhotoProcessingOptions.SectionName));
        services.Configure<QlhvRuntimeOptions>(configuration.GetSection(QlhvRuntimeOptions.SectionName));
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
        services.AddSingleton<RuntimeReadinessCache>();
        services.AddScoped<IRuntimeReadinessProbe, SqlServerRuntimeReadinessProbe>();
        services.AddScoped<IRuntimeReadinessService, RuntimeReadinessService>();
        services.AddScoped<IHocVienPhotoService, HocVienPhotoService>();
        services.AddSingleton<IHocVienSourcePhotoPathResolver, SecureHocVienSourcePhotoPathResolver>();
        services.AddSingleton<IHocVienPhotoOutputPathResolver, SecureHocVienPhotoOutputPathResolver>();
        services.AddSingleton<IBackgroundRemovalEngine, OnnxBackgroundRemovalEngine>();
        services.AddSingleton<HocVienPhotoProcessingQueue>();
        services.AddSingleton<IHocVienPhotoProcessingQueue>(provider =>
            provider.GetRequiredService<HocVienPhotoProcessingQueue>());
        services.AddScoped<IHocVienPhotoProcessingRepository, HocVienPhotoProcessingRepository>();
        services.AddScoped<IHocVienPhotoProcessingService, HocVienPhotoProcessingService>();
        services.AddHostedService<HocVienPhotoProcessingWorker>();
        services.AddSingleton<ISyncConnectionProvider, SyncConnectionProvider>();
        services.AddScoped<IV2HocVienSourceRepository, V2HocVienSourceRepository>();
        services.AddScoped<QlhvHocVienTargetRepository>();
        services.AddScoped<IQlhvHocVienTargetRepository>(provider =>
            provider.GetRequiredService<QlhvHocVienTargetRepository>());
        services.AddScoped<IQlhvImportWriteRepository>(provider =>
            provider.GetRequiredService<QlhvHocVienTargetRepository>());
        services.AddScoped<QlhvImportReadRepository>();
        services.AddScoped<IQlhvImportReadRepository>(provider =>
            provider.GetRequiredService<QlhvImportReadRepository>());
        services.AddScoped<IQlhvFreshnessSourceRepository>(provider =>
            provider.GetRequiredService<QlhvImportReadRepository>());
        services.AddScoped<QlhvOperationConnectionResolver>();
        services.AddScoped<IQlhvOperationsRepository, QlhvOperationsRepository>();
        services.AddScoped<IQlhvOperationHistoryRepository, QlhvOperationHistoryRepository>();
        services.AddScoped<IQlhvAutoSyncRunRepository, QlhvAutoSyncRunRepository>();
        services.AddScoped<IQlhvAutoSyncGlobalLock, QlhvSqlAutoSyncGlobalLock>();
        services.AddScoped<ISystemDataVersionRepository, SystemDataVersionRepository>();
        services.AddScoped<IQlhvPartitionSyncStateRepository, QlhvPartitionSyncStateRepository>();
        services.AddScoped<IQlhvSourceOperationLock, QlhvSqlSourceOperationLock>();
        services.AddScoped<IQlhvBackupRefreshExecutor, QlhvBackupRefreshExecutor>();
        services.AddSingleton<QlhvRefreshBackupQueue>();
        services.AddSingleton<IQlhvRefreshBackupQueue>(provider =>
            provider.GetRequiredService<QlhvRefreshBackupQueue>());
        services.AddHostedService<QlhvRefreshBackupWorker>();
        services.AddSingleton<QlhvAutoSyncQueue>();
        services.AddSingleton<IQlhvAutoSyncQueue>(provider =>
            provider.GetRequiredService<QlhvAutoSyncQueue>());
        services.AddHostedService<QlhvAutoSyncWorker>();
        services.AddHostedService<QlhvAutoSyncStartupService>();
        services.AddScoped<IMotoSyncRepository, MotoSyncRepository>();
        services.AddScoped<IMotoSyncRunHistoryRepository, MotoSyncRunHistoryRepository>();
        services.AddScoped<IMotoCenterTransferRunHistoryRepository, MotoCenterTransferRunHistoryRepository>();
        services.AddScoped<IHocVienSourceAttributionDiagnosticsRepository, HocVienSourceAttributionDiagnosticsRepository>();
        services.AddScoped<ISyncRunLogWriter, SyncRunLogWriter>();
        services.AddScoped<IHocVienSyncJob, HocVienSyncJob>();

        return services;
    }
}

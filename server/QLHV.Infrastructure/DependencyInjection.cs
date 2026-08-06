using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QLHV.Application.Auth;
using QLHV.Application.Assignments;
using QLHV.Application.CsdtConnections;
using QLHV.Application.CourseCompletion;
using QLHV.Application.HocVien;
using QLHV.Application.HocVien.Photos;
using QLHV.Application.Runtime;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Realtime;
using QLHV.Application.Sync.Rt03;
using QLHV.Application.SystemData;
using QLHV.Infrastructure.Auth;
using QLHV.Infrastructure.Assignments;
using QLHV.Infrastructure.CsdtConnections;
using QLHV.Infrastructure.CourseCompletion;
using QLHV.Infrastructure.HocVien;
using QLHV.Infrastructure.HocVien.Photos;
using QLHV.Infrastructure.Runtime;
using QLHV.Infrastructure.Sync;
using QLHV.Infrastructure.Sync.Realtime;
using QLHV.Infrastructure.Sync.Rt01;
using QLHV.Infrastructure.Sync.Rt03;
using QLHV.Infrastructure.Sync.VehicleRealtime;
using QLHV.Infrastructure.Sync.TeacherVehicleProjection;
using QLHV.Application.Sync.TeacherVehicleProjection;
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
        => services
            .AddInfrastructureCore(configuration, contentRootPath)
            .AddApiHostedServices();

    /// <summary>
    /// Registers repositories and other non-hosted infrastructure shared by the
    /// API and the standalone Worker. This method must never register background
    /// services because the API is not a realtime executor.
    /// </summary>
    public static IServiceCollection AddInfrastructureCore(
        this IServiceCollection services,
        IConfiguration configuration,
        string? contentRootPath = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AppUserRepository>();
        services.AddScoped<IAppUserRepository>(provider =>
            provider.GetRequiredService<AppUserRepository>());
        services.AddScoped<IAppUserManagementRepository>(provider =>
            provider.GetRequiredService<AppUserRepository>());
        services.AddScoped<ICsdtConnectionProfileRepository, CsdtConnectionProfileRepository>();
        services.AddScoped<IHocVienRepository, HocVienRepository>();
        services.AddScoped<IAssignmentRepository, SqlAssignmentRepository>();
        services.AddScoped<ICourseCompletionRepository, SqlCourseCompletionRepository>();

        services.Configure<AppSyncOptions>(configuration.GetSection(AppSyncOptions.SectionName));
        services.Configure<SyncExecutionOptions>(configuration.GetSection(SyncExecutionOptions.SectionName));
        services.Configure<QlhvOperationsOptions>(configuration.GetSection(QlhvOperationsOptions.SectionName));
        var autoSyncSection = configuration.GetSection(QlhvAutoSyncOptions.SectionName);
        services.AddOptions<QlhvAutoSyncOptions>()
            .Configure(options =>
            {
                // ConfigurationBinder appends array values to an initialized array.
                // Clear only this binding target so a configured [OTO, MOTO] does not
                // become [OTO, MOTO, OTO, MOTO].
                options.SourceOrder = [];
                autoSyncSection.Bind(options);
                options.SourceOrder = autoSyncSection.GetSection(
                        nameof(QlhvAutoSyncOptions.SourceOrder))
                    .Exists()
                    ? QlhvAutoSyncConstants.NormalizeSourceOrderTokens(options.SourceOrder)
                    : QlhvAutoSyncConstants.CanonicalSourceOrder.ToArray();
            })
            .Validate(
                options => QlhvAutoSyncConstants.IsCanonicalSourceOrder(options.SourceOrder),
                "QlhvAutoSync.SourceOrder phai la OTO roi MOTO, moi nguon dung mot lan.")
            .ValidateOnStart();
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.Configure<HocVienPhotoProcessingOptions>(
            configuration.GetSection(HocVienPhotoProcessingOptions.SectionName));
        services.Configure<QlhvRuntimeOptions>(configuration.GetSection(QlhvRuntimeOptions.SectionName));
        services.AddOptions<CsdtRealtimeSyncOptions>()
            .Bind(configuration.GetSection(CsdtRealtimeSyncOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<Rt03ProductionOptions>()
            .Bind(configuration.GetSection(Rt03ProductionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            Microsoft.Extensions.Options.IValidateOptions<Rt03ProductionOptions>,
            Rt03ProductionOptionsValidator>();
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
        services.AddSingleton<IRuntimeBuildIdentity, RuntimeBuildIdentity>();
        services.AddSingleton<IQlhvAutoSyncPollingState, QlhvAutoSyncPollingState>();
        services.AddSingleton<TimeAuthorityClockMonitor>();
        services.AddSingleton<TimeAuthorityNtpProbeMonitor>();
        services.AddScoped<IRuntimeReadinessProbe, SqlServerRuntimeReadinessProbe>();
        services.AddScoped<IDatabaseTimeAuthorityProbe, SqlDatabaseTimeAuthorityProbe>();
        services.AddScoped<ITimeAuthorityService, TimeAuthorityService>();
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
        services.AddScoped<IQlhvOperationsStateProbe, QlhvOperationsStateProbe>();
        services.AddScoped<IRt03RealtimeControlStore, Rt03RealtimeControlStore>();
        services.AddScoped<IRt03EventBacklogProbe, Rt03EventBacklogProbe>();
        services.AddScoped<IRt03WorkerPermissionProbe, Rt03WorkerPermissionProbe>();
        services.AddScoped<IRt03RealtimeControlService, Rt03RealtimeControlService>();
        services.AddScoped<IRt03RealtimeIntegrityPreviewService,
            Rt03RealtimeIntegrityPreviewService>();
        services.TryAddScoped<Rt01aOtoDriftEvidenceReader>();
        services.AddScoped<ISystemDataVersionRepository, SystemDataVersionRepository>();
        services.AddScoped<IQlhvPartitionSyncStateRepository, QlhvPartitionSyncStateRepository>();
        services.AddScoped<IQlhvSourceOperationLock, QlhvSqlSourceOperationLock>();
        services.AddScoped<IQlhvBackupRefreshExecutor, QlhvBackupRefreshExecutor>();
        services.AddSingleton<QlhvRefreshBackupQueue>();
        services.AddSingleton<IQlhvRefreshBackupQueue>(provider =>
            provider.GetRequiredService<QlhvRefreshBackupQueue>());
        services.AddSingleton<QlhvAutoSyncQueue>();
        services.AddSingleton<IQlhvAutoSyncQueue>(provider =>
            provider.GetRequiredService<QlhvAutoSyncQueue>());
        services.AddScoped<IMotoSyncRepository, MotoSyncRepository>();
        services.AddScoped<IMotoSyncRunHistoryRepository, MotoSyncRunHistoryRepository>();
        services.AddScoped<IMotoCenterTransferRunHistoryRepository, MotoCenterTransferRunHistoryRepository>();
        services.AddScoped<IHocVienSourceAttributionDiagnosticsRepository, HocVienSourceAttributionDiagnosticsRepository>();
        services.AddScoped<ISyncRunLogWriter, SyncRunLogWriter>();
        services.AddScoped<IHocVienSyncJob, HocVienSyncJob>();
        services.AddScoped<CsdtRealtimeStateRepository>();
        services.AddScoped<ICsdtRealtimeStateRepository>(provider =>
            provider.GetRequiredService<CsdtRealtimeStateRepository>());
        services.AddScoped<ICsdtRealtimeCommandRepository>(provider =>
            provider.GetRequiredService<CsdtRealtimeStateRepository>());
        services.AddScoped<CsdtRealtimeConnectionResolver>();
        services.AddScoped<CsdtRealtimeSourceReader>();
        services.AddScoped<CsdtRealtimeTargetWriter>();
        services.AddScoped<CsdtRealtimeStreamProcessor>();
        services.AddScoped<CsdtReversePlanRepository>();
        services.AddScoped<ICsdtReversePlanRepository>(provider =>
            provider.GetRequiredService<CsdtReversePlanRepository>());
        services.AddScoped<ICsdtReverseCommandExecutor>(provider =>
            provider.GetRequiredService<CsdtReversePlanRepository>());

        return services;
    }

    /// <summary>
    /// Registers background services which are intentionally hosted by the API.
    /// Realtime V2-to-V1 execution is excluded and belongs only to QLHV.Worker.
    /// </summary>
    public static IServiceCollection AddApiHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<HocVienPhotoProcessingWorker>();
        services.AddHostedService<QlhvRefreshBackupWorker>();
        services.AddHostedService<QlhvAutoSyncWorker>();
        services.AddHostedService<QlhvAutoSyncStartupService>();
        return services;
    }

    /// <summary>Registers the sole realtime executor in the standalone Worker.</summary>
    public static IServiceCollection AddCsdtRealtimeWorkerServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddScoped<CsdtRealtimeStreamProcessor>();
        services.AddHostedService<CsdtRealtimeWorker>();
        return services;
    }

    /// <summary>
    /// Registers the production direct-realtime executor only when the explicit
    /// master flag is true. With source/config defaults OFF, neither the writer
    /// nor its hosted lifecycle can be resolved.
    /// </summary>
    public static IServiceCollection AddRt03ProductionRealtimeWorkerServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue<bool>(
                $"{Rt03ProductionOptions.SectionName}:" +
                nameof(Rt03ProductionOptions.EnableRt03ProductionRealtime)))
        {
            return services;
        }

        services.AddScoped<Rt01aOtoDriftEvidenceReader>();
        services.AddScoped<Rt03ReviewedRetainedEvidenceReader>();
        services.AddScoped<IRt03ReviewedRetainedRereviewService,
            Rt03ReviewedRetainedRereviewService>();
        services.AddScoped<IRt03ProductionRuntimeStateStore,
            Rt03ProductionRuntimeStateStore>();
        services.AddScoped<IQlhvDirectRealtimeGlobalLock,
            QlhvDirectRealtimeGlobalLock>();
        services.AddScoped<IRt03ProductionRealtimeCycleProcessor,
            Rt03ProductionRealtimeCycleProcessor>();
        services.AddVehicleRealtimeIngestionCore();
        services.AddScoped<ITeacherVehicleProjectionCoordinator,
            SqlTeacherVehicleProjectionCoordinator>();
        services.AddHostedService<Rt03ProductionRealtimeWorker>();
        return services;
    }

    /// <summary>
    /// Registers the operator-invoked RT03 V5 one-shot recovery path. It adds no
    /// hosted service and therefore cannot run merely because the Worker starts.
    /// </summary>
    public static IServiceCollection AddRt03FullConvergenceRecoveryServices(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IQlhvDirectRealtimeGlobalLock,
            QlhvDirectRealtimeGlobalLock>();
        services.TryAddScoped<Rt01aOtoDriftEvidenceReader>();
        services.TryAddScoped<Rt03ReviewedRetainedEvidenceReader>();
        services.AddScoped<Rt03FullConvergenceLockFactory>();
        services.AddScoped<Rt03FullConvergenceSourceBarrierFactory>();
        services.AddScoped<IRt03FullConvergenceStateStore,
            Rt03FullConvergenceStateStore>();
        services.AddScoped<IRt03FullConvergenceRecoveryService,
            Rt03FullConvergenceRecoveryService>();
        services.AddVehicleRealtimeIngestionCore();
        services.TryAddScoped<ITeacherVehicleProjectionCoordinator,
            SqlTeacherVehicleProjectionCoordinator>();
        return services;
    }
}

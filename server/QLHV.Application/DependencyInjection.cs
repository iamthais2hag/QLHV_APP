using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using QLHV.Application.Auth;
using QLHV.Application.CsdtConnections;
using QLHV.Application.HocVien;
using QLHV.Application.HocVien.Printing;
using QLHV.Application.Sync;
using QLHV.Application.SystemData;

namespace QLHV.Application;

/// <summary>Đăng ký các dịch vụ của tầng Application.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IPasswordHasher<AppUserCredential>, PasswordHasher<AppUserCredential>>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IFirstAdminSeeder, FirstAdminSeeder>();
        services.AddScoped<IHocVienService, HocVienService>();
        services.AddSingleton(HocVienCardTemplate.Default);
        services.AddSingleton<IHocVienCardPdfGenerator, HocVienCardPdfGenerator>();
        services.AddScoped<ICsdtConnectionProfileService, CsdtConnectionProfileService>();
        services.AddScoped<IHocVienSourceAttributionDiagnosticsService, HocVienSourceAttributionDiagnosticsService>();
        services.AddScoped<IHocVienSyncService, HocVienSyncService>();
        services.AddScoped<IQlhvImportService, QlhvImportService>();
        services.AddScoped<IQlhvOperationsService, QlhvOperationsService>();
        services.AddScoped<IQlhvAutoSyncService, QlhvAutoSyncService>();
        services.AddScoped<IQlhvAutoSyncSourceRunner, QlhvAutoSyncSourceRunner>();
        services.AddScoped<IQlhvSyncFreshnessService, QlhvSyncFreshnessService>();
        services.AddScoped<QlhvAutoSyncCoordinator>();
        services.AddScoped<ISystemDataVersionService, SystemDataVersionService>();
        services.AddScoped<IMotoSyncService, MotoSyncService>();
        return services;
    }
}

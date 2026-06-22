using Microsoft.Extensions.DependencyInjection;
using QLHV.Application.HocVien;
using QLHV.Application.HocVien.Printing;
using QLHV.Application.Sync;

namespace QLHV.Application;

/// <summary>Đăng ký các dịch vụ của tầng Application.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IHocVienService, HocVienService>();
        services.AddSingleton(HocVienCardTemplate.Default);
        services.AddSingleton<IHocVienCardPdfGenerator, HocVienCardPdfGenerator>();
        services.AddScoped<IHocVienSyncService, HocVienSyncService>();
        return services;
    }
}

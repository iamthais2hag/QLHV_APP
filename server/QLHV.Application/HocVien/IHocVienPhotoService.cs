using QLHV.Application.HocVien.Dtos;

namespace QLHV.Application.HocVien;

public interface IHocVienPhotoService
{
    Task<HocVienPhotoPreviewDto?> GetPreviewAsync(
        HocVienListItemDto hocVien,
        CancellationToken cancellationToken = default);

    Task<HocVienPhotoInspectionDto> InspectAsync(
        HocVienListItemDto hocVien,
        bool validateDecode,
        CancellationToken cancellationToken = default);
}

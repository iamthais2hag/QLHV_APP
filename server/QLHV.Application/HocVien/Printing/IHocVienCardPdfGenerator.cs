using QLHV.Application.HocVien.Dtos;

namespace QLHV.Application.HocVien.Printing;

public interface IHocVienCardPdfGenerator
{
    byte[] CreatePdf(
        IReadOnlyList<HocVienListItemDto> hocViens,
        IReadOnlyDictionary<int, HocVienPhotoPreviewDto>? photosByHocVienId = null);
}

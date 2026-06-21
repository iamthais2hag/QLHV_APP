using QLHV.Application.HocVien.Dtos;
using QLHV.Shared.Paging;

namespace QLHV.Application.HocVien;

/// <summary>
/// Nghiệp vụ tìm kiếm học viên (chỉ đọc).
/// </summary>
public interface IHocVienService
{
    /// <summary>Tìm kiếm danh sách học viên theo điều kiện đã chuẩn hóa.</summary>
    Task<PagedResult<HocVienListItemDto>> SearchAsync(
        HocVienSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<HocVienPhotoPreviewDto?> GetPhotoPreviewAsync(
        int hocVienId,
        CancellationToken cancellationToken = default);

    Task<HocVienExportFileDto> PrintCardsAsync(
        HocVienCardPrintRequest request,
        CancellationToken cancellationToken = default);

    Task<HocVienCardPrintPreviewDto> PreviewPrintCardsAsync(
        HocVienCardPrintRequest request,
        CancellationToken cancellationToken = default);

    Task<HocVienPhotoAuditResultDto> AuditPhotosAsync(
        HocVienPhotoAuditRequest request,
        CancellationToken cancellationToken = default);

    Task<HocVienExportFileDto> ExportExcelAsync(
        HocVienSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HocVienKhoaLookupDto>> SearchKhoaLookupsAsync(
        string? keyword,
        string? maHangDT,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HocVienHangHocLookupDto>> SearchHangHocLookupsAsync(
        string? keyword,
        int limit,
        CancellationToken cancellationToken = default);
}

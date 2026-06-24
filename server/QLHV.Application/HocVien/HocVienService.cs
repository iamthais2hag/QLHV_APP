using QLHV.Application.HocVien.Dtos;
using QLHV.Application.HocVien.Printing;
using QLHV.Shared.Paging;

namespace QLHV.Application.HocVien;

/// <summary>
/// Cài đặt nghiệp vụ tìm kiếm học viên. Chuẩn hóa tham số rồi ủy quyền cho repository.
/// </summary>
public sealed class HocVienService : IHocVienService
{
    private const int DefaultLookupLimit = 20;
    private const int MaxLookupLimit = 50;
    private const int MaxExportRows = 10_000;
    private const int MaxPrintRows = 1_000;

    private readonly IHocVienRepository _repository;
    private readonly IHocVienPhotoService _photoService;
    private readonly IHocVienCardPdfGenerator _cardPdfGenerator;
    private readonly HocVienCardTemplate _cardTemplate;

    public HocVienService(
        IHocVienRepository repository,
        IHocVienPhotoService photoService,
        IHocVienCardPdfGenerator cardPdfGenerator,
        HocVienCardTemplate cardTemplate)
    {
        _repository = repository;
        _photoService = photoService;
        _cardPdfGenerator = cardPdfGenerator;
        _cardTemplate = cardTemplate;
    }

    public Task<PagedResult<HocVienListItemDto>> SearchAsync(
        HocVienSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = request.Normalized();
        return _repository.SearchAsync(normalized, cancellationToken);
    }

    public async Task<HocVienPhotoPreviewDto?> GetPhotoPreviewAsync(
        int hocVienId,
        CancellationToken cancellationToken = default)
    {
        if (hocVienId <= 0)
        {
            return null;
        }

        var hocVien = await _repository.GetByIdAsync(hocVienId, cancellationToken);
        return hocVien is null
            ? null
            : await _photoService.GetPreviewAsync(hocVien, cancellationToken);
    }

    public async Task<HocVienExportFileDto> PrintCardsAsync(
        HocVienCardPrintRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = request.Normalized();
        var loadedRows = await LoadRowsForPrintAsync(normalized, cancellationToken);
        var prepared = await PreparePrintRowsAsync(normalized, loadedRows, cancellationToken);
        var rows = prepared.Rows;

        if (rows.Count == 0)
        {
            throw new InvalidOperationException("Khong co hoc vien phu hop de in the.");
        }

        var photos = await LoadPrintablePhotosAsync(rows, cancellationToken);
        var titleOptions = CreateCardOptions(normalized);
        var content = _cardPdfGenerator.CreatePdf(rows, photos, titleOptions);
        return new HocVienExportFileDto
        {
            FileName = $"TheHocVien_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
            ContentType = "application/pdf",
            Content = content,
        };
    }

    public async Task<HocVienCardPrintPreviewDto> PreviewPrintCardsAsync(
        HocVienCardPrintRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = request.Normalized();
        var loadedRows = await LoadRowsForPrintAsync(normalized, cancellationToken);
        var prepared = await PreparePrintRowsAsync(normalized, loadedRows, cancellationToken);
        var cardOptions = CreateCardOptions(normalized);
        var titles = _cardTemplate.ResolveTitles(cardOptions);

        return new HocVienCardPrintPreviewDto
        {
            TotalStudents = prepared.Rows.Count,
            TotalPages = HocVienCardLayout.GetPageCount(prepared.Rows.Count),
            CardsPerPage = HocVienCardLayout.CardsPerPage,
            LayoutName = "A4 ngang 3x4",
            MissingPhotoCount = prepared.MissingPhotoCount,
            OrganizationLine1 = titles.TitleLine1,
            OrganizationLine2 = titles.TitleLine2,
            CardTitle = _cardTemplate.ResolveCardTitle(cardOptions),
            Items = prepared.Items,
        };
    }

    private static HocVienCardTitleOptions CreateCardOptions(HocVienCardPrintRequest request)
        => new(
            request.TitleLine1,
            request.TitleLine2,
            HocVienCardTypographyOptions.FromRequest(request.Typography),
            request.CardTitle,
            request.TrainingRankLabel,
            HocVienCardLogoOptions.FromRequest(request.Logo));

    public async Task<HocVienPhotoAuditResultDto> AuditPhotosAsync(
        HocVienPhotoAuditRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = request.Normalized();
        var rows = await _repository.ExportRowsAsync(new HocVienSearchRequest
        {
            Keyword = normalized.Keyword,
            MaKhoa = normalized.MaKhoa,
            MaHangDT = normalized.MaHangDT,
            Page = 1,
            PageSize = normalized.PageSize,
        }, MaxExportRows + 1, cancellationToken);

        if (rows.Count > MaxExportRows)
        {
            throw new InvalidOperationException(
                $"Ket qua doi soat anh vuot qua gioi han an toan {MaxExportRows:N0} dong.");
        }

        var items = new List<HocVienPhotoAuditItemDto>(rows.Count);
        foreach (var row in rows)
        {
            var photo = await _photoService.InspectAsync(row, normalized.ValidateDecode, cancellationToken);
            items.Add(new HocVienPhotoAuditItemDto
            {
                HocVienId = row.HocVienId,
                MaDangKy = row.MaDangKy,
                HoVaTen = row.HoVaTen,
                MaKhoa = row.MaKhoa,
                TenKhoa = row.TenKhoa,
                MaHangDT = row.MaHangDT,
                HangGplxHoc = row.HangGplxHoc,
                ExpectedRelativePath = photo.ExpectedRelativePath,
                HasPhoto = photo.HasPhoto,
                PhotoStatus = photo.PhotoStatus,
                Message = photo.Message,
            });
        }

        var totalHasPhoto = items.Count(item => item.HasPhoto);
        var totalMissingPhoto = items.Count(item => item.PhotoStatus == "Missing");
        var totalInvalidPhoto = items.Count(item => IsInvalidPhotoStatus(item.PhotoStatus));
        var filtered = items
            .Where(item => MatchesAuditStatusFilters(item, normalized.OnlyMissing, normalized.OnlyInvalid))
            .ToArray();
        var pageItems = filtered
            .Skip((normalized.Page - 1) * normalized.PageSize)
            .Take(normalized.PageSize)
            .ToArray();

        return new HocVienPhotoAuditResultDto
        {
            TotalItems = filtered.Length,
            TotalHasPhoto = totalHasPhoto,
            TotalMissingPhoto = totalMissingPhoto,
            TotalInvalidPhoto = totalInvalidPhoto,
            Page = normalized.Page,
            PageSize = normalized.PageSize,
            Items = pageItems,
        };
    }

    public async Task<HocVienExportFileDto> ExportExcelAsync(
        HocVienSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = request.Normalized();
        var rows = await _repository.ExportRowsAsync(normalized, MaxExportRows + 1, cancellationToken);
        if (rows.Count > MaxExportRows)
        {
            throw new InvalidOperationException(
                $"Ket qua xuat Excel vuot qua gioi han an toan {MaxExportRows:N0} dong.");
        }

        var content = HocVienExcelExporter.CreateWorkbook(rows, rows.Count);
        return new HocVienExportFileDto
        {
            FileName = $"HocVien_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            Content = content,
        };
    }

    private async Task<IReadOnlyList<HocVienListItemDto>> LoadRowsForPrintAsync(
        HocVienCardPrintRequest request,
        CancellationToken cancellationToken)
    {
        var mode = request.Mode?.ToLowerInvariant();
        return mode switch
        {
            "single" => await LoadSingleForPrintAsync(request, cancellationToken),
            "selected" => await LoadSelectedForPrintAsync(request, cancellationToken),
            "course" => await LoadCourseForPrintAsync(request, cancellationToken),
            "teacherincourse" => throw new InvalidOperationException(
                "teacherInCourse chua co du lieu phan cong giao vien-hoc vien de in theo giao vien."),
            _ => throw new InvalidOperationException("Mode in the hoc vien khong hop le."),
        };
    }

    private async Task<PreparedPrintRows> PreparePrintRowsAsync(
        HocVienCardPrintRequest request,
        IReadOnlyList<HocVienListItemDto> rows,
        CancellationToken cancellationToken)
    {
        var missingPhotoMode = (request.MissingPhotoMode ?? "placeholder").ToLowerInvariant();
        if (missingPhotoMode is not ("placeholder" or "skip"))
        {
            throw new InvalidOperationException("missingPhotoMode chi ho tro placeholder hoac skip.");
        }

        var inspected = new List<(HocVienListItemDto Row, HocVienPhotoInspectionDto Photo)>(rows.Count);
        foreach (var row in rows)
        {
            inspected.Add((row, await _photoService.InspectAsync(row, validateDecode: false, cancellationToken)));
        }

        var missingPhotoCount = inspected.Count(item => IsMissingForPrint(item.Photo));
        var filtered = missingPhotoMode == "skip"
            ? inspected.Where(item => !IsMissingForPrint(item.Photo))
            : inspected;
        var sorted = SortPrintRows(filtered, request.SortBy).ToArray();

        return new PreparedPrintRows(
            sorted.Select(item => item.Row).ToArray(),
            sorted.Select(item => new HocVienCardPrintPreviewItemDto
            {
                HocVienId = item.Row.HocVienId,
                MaDangKy = item.Row.MaDangKy,
                HoVaTen = item.Row.HoVaTen,
                MaKhoa = item.Row.MaKhoa,
                TenKhoa = item.Row.TenKhoa,
                MaHangDT = item.Row.MaHangDT,
                HangGplxHoc = item.Row.HangGplxHoc,
                HasPhoto = item.Photo.HasPhoto,
                PhotoStatus = item.Photo.PhotoStatus,
            }).ToArray(),
            missingPhotoCount);
    }

    private async Task<IReadOnlyDictionary<int, HocVienPhotoPreviewDto>> LoadPrintablePhotosAsync(
        IReadOnlyList<HocVienListItemDto> rows,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, HocVienPhotoPreviewDto>();
        foreach (var row in rows)
        {
            try
            {
                var photo = await _photoService.GetPreviewAsync(row, cancellationToken);
                if (photo is not null)
                {
                    result[row.HocVienId] = photo;
                }
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // Keep PDF generation read-only and resilient: bad photos print as placeholders.
            }
        }

        return result;
    }

    private static IEnumerable<(HocVienListItemDto Row, HocVienPhotoInspectionDto Photo)> SortPrintRows(
        IEnumerable<(HocVienListItemDto Row, HocVienPhotoInspectionDto Photo)> rows,
        string? sortBy)
    {
        return (sortBy ?? "current").ToLowerInvariant() switch
        {
            "current" => rows,
            "hoten" => rows.OrderBy(item => item.Row.HoVaTen).ThenBy(item => item.Row.HocVienId),
            "madk" => rows.OrderBy(item => item.Row.MaDangKy).ThenBy(item => item.Row.HocVienId),
            _ => throw new InvalidOperationException("sortBy chi ho tro current, hoTen hoac maDK."),
        };
    }

    private static bool IsMissingForPrint(HocVienPhotoInspectionDto photo)
        => photo.PhotoStatus is "Missing" or "UnsafePath";

    private static bool IsInvalidPhotoStatus(string status)
        => status is "Invalid" or "Unsupported" or "UnsafePath";

    private static bool MatchesAuditStatusFilters(
        HocVienPhotoAuditItemDto item,
        bool onlyMissing,
        bool onlyInvalid)
    {
        if (!onlyMissing && !onlyInvalid)
        {
            return true;
        }

        return (onlyMissing && item.PhotoStatus == "Missing") ||
            (onlyInvalid && IsInvalidPhotoStatus(item.PhotoStatus));
    }

    private sealed record PreparedPrintRows(
        IReadOnlyList<HocVienListItemDto> Rows,
        IReadOnlyList<HocVienCardPrintPreviewItemDto> Items,
        int MissingPhotoCount);

    private async Task<IReadOnlyList<HocVienListItemDto>> LoadSingleForPrintAsync(
        HocVienCardPrintRequest request,
        CancellationToken cancellationToken)
    {
        if (request.HocVienId is null or <= 0)
        {
            throw new InvalidOperationException("Mode single can hocVienId hop le.");
        }

        var row = await _repository.GetByIdAsync(request.HocVienId.Value, cancellationToken);
        return row is null ? [] : [row];
    }

    private Task<IReadOnlyList<HocVienListItemDto>> LoadSelectedForPrintAsync(
        HocVienCardPrintRequest request,
        CancellationToken cancellationToken)
    {
        if (request.HocVienIds is not { Count: > 0 })
        {
            throw new InvalidOperationException("Mode selected can danh sach hocVienIds.");
        }

        return _repository.GetByIdsAsync(request.HocVienIds, MaxPrintRows, cancellationToken);
    }

    private Task<IReadOnlyList<HocVienListItemDto>> LoadCourseForPrintAsync(
        HocVienCardPrintRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.MaKhoa))
        {
            throw new InvalidOperationException("Mode course can maKhoa hop le.");
        }

        return _repository.GetByCourseAsync(request.MaKhoa, MaxPrintRows, cancellationToken);
    }

    public Task<IReadOnlyList<HocVienKhoaLookupDto>> SearchKhoaLookupsAsync(
        string? keyword,
        string? maHangDT,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return _repository.SearchKhoaLookupsAsync(
            NormalizeLookupKeyword(keyword),
            NormalizeLookupKeyword(maHangDT),
            NormalizeLookupLimit(limit),
            cancellationToken);
    }

    public Task<IReadOnlyList<HocVienHangHocLookupDto>> SearchHangHocLookupsAsync(
        string? keyword,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return _repository.SearchHangHocLookupsAsync(
            NormalizeLookupKeyword(keyword),
            NormalizeLookupLimit(limit),
            cancellationToken);
    }

    private static string? NormalizeLookupKeyword(string? keyword)
        => string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();

    private static int NormalizeLookupLimit(int limit)
        => Math.Clamp(limit <= 0 ? DefaultLookupLimit : limit, 1, MaxLookupLimit);
}

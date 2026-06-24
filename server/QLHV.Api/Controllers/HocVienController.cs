using Microsoft.AspNetCore.Mvc;
using QLHV.Application.HocVien;
using QLHV.Application.HocVien.Dtos;
using QLHV.Application.HocVien.Printing;
using QLHV.Shared.Paging;

namespace QLHV.Api.Controllers;

/// <summary>
/// API tra cứu học viên (chỉ đọc). Dữ liệu gốc từ nguồn V2 là chỉ đọc.
/// </summary>
[ApiController]
[Route("api/hoc-vien")]
[Produces("application/json")]
public sealed class HocVienController : ControllerBase
{
    private readonly IHocVienService _service;

    public HocVienController(IHocVienService service)
    {
        _service = service;
    }

    /// <summary>Tìm kiếm danh sách học viên theo từ khóa và bộ lọc.</summary>
    /// <param name="request">Tham số tìm kiếm và phân trang.</param>
    /// <param name="cancellationToken">Token hủy.</param>
    /// <returns>Danh sách học viên đã phân trang.</returns>
    /// <response code="200">Trả về danh sách học viên (có thể rỗng).</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<HocVienListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<HocVienListItemDto>>> Search(
        [FromQuery] HocVienSearchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.SearchAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>Lookup khoa hoc distinct tu App_HocVien.</summary>
    /// <param name="keyword">Tu khoa tim theo TenKhoa hoac MaKhoa.</param>
    /// <param name="maHangDT">Neu co, chi goi y khoa co du lieu thuoc ma hang hoc nay.</param>
    /// <param name="limit">So goi y toi da.</param>
    /// <param name="cancellationToken">Token huy.</param>
    [HttpGet("lookups/khoa")]
    [ProducesResponseType(typeof(IReadOnlyList<HocVienKhoaLookupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HocVienKhoaLookupDto>>> LookupKhoa(
        [FromQuery] string? keyword,
        [FromQuery] string? maHangDT,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.SearchKhoaLookupsAsync(keyword, maHangDT, limit, cancellationToken);
        return Ok(result);
    }

    [HttpGet("photos/audit")]
    [ProducesResponseType(typeof(HocVienPhotoAuditResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HocVienPhotoAuditResultDto>> AuditPhotos(
        [FromQuery] HocVienPhotoAuditRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.AuditPhotosAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{hocVienId:int}/photo/preview")]
    [Produces("image/jpeg", "image/png")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> PhotoPreview(
        [FromRoute] int hocVienId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.GetPhotoPreviewAsync(hocVienId, cancellationToken);
            if (result is null)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "private, max-age=300";
            return File(result.Content, result.ContentType);
        }
        catch (NotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new { message = ex.Message });
        }
        catch (InvalidDataException ex)
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new { message = ex.Message });
        }
    }

    [HttpGet("the-hoc-vien/logo")]
    [Produces("image/png")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public IActionResult CardLogo()
    {
        Response.Headers.CacheControl = "public, max-age=86400";
        return File(HocVienCardLogo.Content.ToArray(), "image/png");
    }

    /// <summary>Lookup hang hoc distinct tu App_HocVien.</summary>
    /// <param name="keyword">Tu khoa tim theo MaHangDT hoac HangGPLXHoc.</param>
    /// <param name="limit">So goi y toi da.</param>
    /// <param name="cancellationToken">Token huy.</param>
    [HttpGet("lookups/hang-hoc")]
    [ProducesResponseType(typeof(IReadOnlyList<HocVienHangHocLookupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HocVienHangHocLookupDto>>> LookupHangHoc(
        [FromQuery] string? keyword,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.SearchHangHocLookupsAsync(keyword, limit, cancellationToken);
        return Ok(result);
    }

    /// <summary>Xuáº¥t Excel toÃ n bá»™ há»c viÃªn phÃ¹ há»£p vá»›i bá»™ lá»c hiá»‡n táº¡i.</summary>
    /// <param name="request">Tham sá»‘ tÃ¬m kiáº¿m; phÃ¢n trang khÃ´ng Ä‘Æ°á»£c Ã¡p dá»¥ng cho export.</param>
    /// <param name="cancellationToken">Token há»§y.</param>
    /// <returns>File Excel .xlsx chá»‰ Ä‘á»c.</returns>
    [HttpGet("export-excel")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportExcel(
        [FromQuery] HocVienSearchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.ExportExcelAsync(request, cancellationToken);
            return File(result.Content, result.ContentType, result.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("the-hoc-vien/print-preview")]
    [ProducesResponseType(typeof(HocVienCardPrintPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PrintStudentCardsPreview(
        [FromBody] HocVienCardPrintRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.PreviewPrintCardsAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("the-hoc-vien/print-a4")]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PrintStudentCardsA4(
        [FromBody] HocVienCardPrintRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.PrintCardsAsync(request, cancellationToken);
            return File(result.Content, result.ContentType, result.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

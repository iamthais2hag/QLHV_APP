using Microsoft.AspNetCore.Mvc;
using QLHV.Application.HocVien;
using QLHV.Application.HocVien.Dtos;
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
    /// <param name="limit">So goi y toi da.</param>
    /// <param name="cancellationToken">Token huy.</param>
    [HttpGet("lookups/khoa")]
    [ProducesResponseType(typeof(IReadOnlyList<HocVienKhoaLookupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HocVienKhoaLookupDto>>> LookupKhoa(
        [FromQuery] string? keyword,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.SearchKhoaLookupsAsync(keyword, limit, cancellationToken);
        return Ok(result);
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

    /// <summary>Xuat Excel toan bo hoc vien phu hop voi bo loc hien tai.</summary>
    /// <param name="request">Tham so tim kiem; phan trang khong duoc ap dung cho export.</param>
    /// <param name="cancellationToken">Token huy.</param>
    /// <returns>File Excel .xlsx chi doc.</returns>
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
}

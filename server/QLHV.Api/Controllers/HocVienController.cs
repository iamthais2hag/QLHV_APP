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

    /// <summary>Gợi ý khóa học từ App_HocVien, chỉ đọc.</summary>
    [HttpGet("lookups/khoa")]
    [ProducesResponseType(typeof(IReadOnlyList<HocVienKhoaLookupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HocVienKhoaLookupDto>>> LookupKhoa(
        [FromQuery] string? keyword,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        var result = await _service.LookupKhoaAsync(keyword, limit <= 0 ? 20 : limit, cancellationToken);
        return Ok(result);
    }

    /// <summary>Gợi ý hạng học từ App_HocVien, chỉ đọc.</summary>
    [HttpGet("lookups/hang-hoc")]
    [ProducesResponseType(typeof(IReadOnlyList<HocVienHangHocLookupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HocVienHangHocLookupDto>>> LookupHangHoc(
        [FromQuery] string? keyword,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        var result = await _service.LookupHangHocAsync(keyword, limit <= 0 ? 20 : limit, cancellationToken);
        return Ok(result);
    }
}

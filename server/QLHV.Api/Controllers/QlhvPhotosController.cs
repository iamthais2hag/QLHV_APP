using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QLHV.Application.Auth;
using QLHV.Application.HocVien.Photos;

namespace QLHV.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthPolicies.CanViewBusinessData)]
[Route("api/dong-bo-v2/qlhv/photos")]
[Produces("application/json")]
public sealed class QlhvPhotosController : ControllerBase
{
    private readonly IHocVienPhotoProcessingService _service;

    public QlhvPhotosController(IHocVienPhotoProcessingService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(HocVienPhotoProcessingPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HocVienPhotoProcessingPageDto>> Search(
        [FromQuery] HocVienPhotoSearchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.SearchAsync(request, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bo loc anh khong hop le.",
                detail: exception.Message));
        }
    }

    [HttpGet("readiness")]
    [ProducesResponseType(typeof(BackgroundRemovalEngineReadiness), StatusCodes.Status200OK)]
    public async Task<ActionResult<BackgroundRemovalEngineReadiness>> Readiness(
        CancellationToken cancellationToken) =>
        Ok(await _service.GetReadinessAsync(cancellationToken));

    [HttpGet("{id:long}/source-preview")]
    [Produces("image/jpeg")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SourcePreview(
        long id,
        CancellationToken cancellationToken)
    {
        var image = await _service.GetSourceImageAsync(id, cancellationToken);
        if (image is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(image.Content, image.ContentType);
    }

    [HttpGet("{id:long}/output-preview")]
    [Produces("image/jpeg")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OutputPreview(
        long id,
        CancellationToken cancellationToken)
    {
        var image = await _service.GetDerivedImageAsync(id, cancellationToken);
        if (image is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(image.Content, image.ContentType);
    }

    [HttpPost("{id:long}/approve")]
    [Authorize(Policy = AuthPolicies.CanImportData)]
    [ProducesResponseType(typeof(HocVienPhotoRecordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HocVienPhotoRecordDto>> Approve(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetUser(out var userId, out var actor))
        {
            return Unauthorized();
        }

        try
        {
            var updated = await _service.ApproveAsync(
                id,
                userId,
                actor,
                cancellationToken);
            return updated is null
                ? Conflict(Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Anh chua the duyet.",
                    detail: "Anh dan xuat khong ton tai hoac chua o trang thai co the duyet."))
                : Ok(updated);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Anh chua the duyet.",
                detail: exception.Message));
        }
    }

    [HttpPost("{id:long}/reprocess")]
    [Authorize(Policy = AuthPolicies.CanImportData)]
    [ProducesResponseType(typeof(HocVienPhotoRecordDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HocVienPhotoRecordDto>> Reprocess(
        long id,
        CancellationToken cancellationToken)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "QLHV_ADMIN";
        try
        {
            var updated = await _service.ReprocessAsync(id, actor, cancellationToken);
            return updated is null ? NotFound() : Accepted(updated);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Chua the xu ly lai anh.",
                detail: exception.Message));
        }
    }

    private bool TryGetUser(out long userId, out string actor)
    {
        actor = User.FindFirstValue(ClaimTypes.Name) ?? "QLHV_ADMIN";
        return long.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out userId) &&
               userId > 0;
    }
}

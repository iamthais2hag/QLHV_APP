using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QLHV.Application.CourseCompletion;

namespace QLHV.Api.Controllers;

[ApiController]
[Route("api/khoa-hoc/{courseId:long}/hoan-thanh")]
public sealed class CourseCompletionController : ControllerBase
{
    private readonly ICourseCompletionService _service;

    public CourseCompletionController(ICourseCompletionService service) => _service = service;

    [HttpGet]
    [Authorize(Policy = CourseCompletionPolicies.ViewStatus)]
    public Task<ActionResult<CourseCompletionStatusResult>> GetStatus(
        long courseId,
        CancellationToken cancellationToken) =>
        Execute(() => _service.GetStatusAsync(courseId, cancellationToken));

    [HttpPost("preview")]
    [Authorize(Policy = CourseCompletionPolicies.Preview)]
    public Task<ActionResult<CourseCompletionPreviewResult>> Preview(
        long courseId,
        [FromBody] CourseCompletionPreviewRequest request,
        CancellationToken cancellationToken) =>
        Execute(() => _service.PreviewAsync(courseId, request, Actor, cancellationToken));

    [HttpPost("confirm")]
    [Authorize(Policy = CourseCompletionPolicies.Complete)]
    public Task<ActionResult<CourseCompletionConfirmResult>> Confirm(
        long courseId,
        [FromBody] CourseCompletionConfirmRequest request,
        CancellationToken cancellationToken) =>
        Execute(() => _service.ConfirmAsync(courseId, request, Actor, cancellationToken));

    private string Actor => User.FindFirstValue(ClaimTypes.Name)?.Trim() is { Length: > 0 } actor
        ? actor
        : throw new CourseCompletionDomainException("UNAUTHORIZED", "Không xác định được người thực hiện.", 401);

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (CourseCompletionDomainException exception)
        {
            var details = new ProblemDetails
            {
                Status = exception.StatusCode,
                Title = exception.StatusCode == 409
                    ? "Dữ liệu đã thay đổi hoặc cần xử lý riêng."
                    : "Không thể xử lý yêu cầu.",
                Detail = exception.Message,
            };
            details.Extensions["code"] = exception.Code;
            details.Extensions["traceId"] = HttpContext.TraceIdentifier;
            return StatusCode(exception.StatusCode, details);
        }
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QLHV.Application.Auth;
using QLHV.Application.Sync.Rt03;

namespace QLHV.Api.Controllers;

[ApiController]
[Route("api/system")]
[Produces("application/json")]
public sealed class SystemRealtimeControlController : ControllerBase
{
    private readonly IRt03RealtimeControlService _control;
    private readonly IRt03RealtimeIntegrityPreviewService _integrity;

    public SystemRealtimeControlController(
        IRt03RealtimeControlService control,
        IRt03RealtimeIntegrityPreviewService integrity)
    {
        _control = control;
        _integrity = integrity;
    }

    [Authorize(Policy = AuthPolicies.CanViewBusinessData)]
    [HttpGet("realtime-control")]
    [ProducesResponseType(typeof(Rt03RealtimeControlStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<Rt03RealtimeControlStatusDto>> Get(
        CancellationToken cancellationToken) =>
        Ok(await _control.GetAsync(cancellationToken));

    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [HttpPost("realtime-control/enable")]
    [ProducesResponseType(typeof(Rt03RealtimeControlStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<Rt03RealtimeControlStatusDto>> Enable(
        [FromBody] Rt03RealtimeControlChangeRequest request,
        CancellationToken cancellationToken) =>
        ChangeAsync(request, enable: true, cancellationToken);

    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [HttpPost("realtime-control/disable")]
    [ProducesResponseType(typeof(Rt03RealtimeControlStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<Rt03RealtimeControlStatusDto>> Disable(
        [FromBody] Rt03RealtimeControlChangeRequest request,
        CancellationToken cancellationToken) =>
        ChangeAsync(request, enable: false, cancellationToken);

    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [HttpPost("realtime-control/run-once")]
    [ProducesResponseType(typeof(Rt03RealtimeRunRequest), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Rt03RealtimeRunRequest>> RunOnce(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _control.RunOnceAsync(Actor, cancellationToken);
            return Accepted(result);
        }
        catch (Rt03SafetyException exception)
        {
            return Conflict(Problem(exception.Code, exception.Message));
        }
    }

    [Authorize(Policy = AuthPolicies.RequireAdmin)]
    [HttpPost("realtime-integrity/preview")]
    [ProducesResponseType(
        typeof(Rt03RealtimeIntegrityPreviewDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<Rt03RealtimeIntegrityPreviewDto>> IntegrityPreview(
        CancellationToken cancellationToken) =>
        Ok(await _integrity.PreviewAsync(cancellationToken));

    private async Task<ActionResult<Rt03RealtimeControlStatusDto>> ChangeAsync(
        Rt03RealtimeControlChangeRequest request,
        bool enable,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = enable
                ? await _control.EnableAsync(request, Actor, cancellationToken)
                : await _control.DisableAsync(request, Actor, cancellationToken);
            return Ok(result);
        }
        catch (Rt03RealtimeControlConcurrencyException exception)
        {
            return Conflict(Problem(
                Rt03RealtimeMasterErrors.ControlConcurrencyConflict,
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(Problem("INVALID_REQUEST", exception.Message));
        }
    }

    private string Actor =>
        User.FindFirstValue(ClaimTypes.Name)?.Trim() is { Length: > 0 } actor
            ? actor
            : "AUTHENTICATED_ADMIN";

    private static ProblemDetails Problem(string title, string detail) => new()
    {
        Title = title,
        Detail = detail,
    };
}

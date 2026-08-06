using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QLHV.Application.Auth;
using QLHV.Application.Sync.Realtime;

namespace QLHV.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthPolicies.CanViewBusinessData)]
[Route("api/dong-bo-v2/csdt-realtime")]
[Produces("application/json")]
public sealed class CsdtRealtimeController : ControllerBase
{
    private readonly ICsdtRealtimeService _service;

    public CsdtRealtimeController(ICsdtRealtimeService service)
    {
        _service = service;
    }

    [HttpGet("streams")]
    [ProducesResponseType(typeof(CsdtRealtimeStreamsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CsdtRealtimeStreamsResponseDto>> Streams(
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.GetStreamsAsync(CurrentUser(), cancellationToken));
        }
        catch (CsdtRealtimeStoreUnavailableException exception)
        {
            return StoreUnavailable(exception);
        }
    }

    [HttpGet("streams/{streamCode}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<CsdtRealtimeHistoryItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<CsdtRealtimeHistoryItemDto>>> History(
        string streamCode,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _service.GetHistoryAsync(streamCode, take, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
        catch (CsdtRealtimeStoreUnavailableException exception)
        {
            return StoreUnavailable(exception);
        }
    }

    [HttpGet("streams/{streamCode}/tombstones")]
    [ProducesResponseType(typeof(IReadOnlyList<CsdtRealtimeTombstoneDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<CsdtRealtimeTombstoneDto>>> Tombstones(
        string streamCode,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _service.GetTombstonesAsync(streamCode, take, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
        catch (CsdtRealtimeStoreUnavailableException exception)
        {
            return StoreUnavailable(exception);
        }
    }

    [HttpPut("streams/{streamCode}/enabled")]
    [Authorize(Policy = AuthPolicies.CanSynchronizeCSDT)]
    [ProducesResponseType(typeof(CsdtRealtimeActionResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(CsdtRealtimeActionResultDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CsdtRealtimeActionResultDto>> SetEnabled(
        string streamCode,
        [FromBody] CsdtRealtimeEnableRequest request,
        CancellationToken cancellationToken)
        => await RunActionAsync(
            () => _service.SetEnabledAsync(
                streamCode,
                request,
                CurrentUser(),
                cancellationToken));

    [HttpPost("streams/{streamCode}/baseline")]
    [Authorize(Policy = AuthPolicies.CanSynchronizeCSDT)]
    [ProducesResponseType(typeof(CsdtRealtimeActionResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(CsdtRealtimeActionResultDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CsdtRealtimeActionResultDto>> Baseline(
        string streamCode,
        [FromBody] CsdtRealtimeBaselineRequest request,
        CancellationToken cancellationToken)
        => await RunActionAsync(
            () => _service.QueueBaselineAsync(
                streamCode,
                request,
                CurrentUser(),
                cancellationToken));

    [HttpPost("streams/{streamCode}/retry")]
    [Authorize(Policy = AuthPolicies.CanSynchronizeCSDT)]
    [ProducesResponseType(typeof(CsdtRealtimeActionResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(CsdtRealtimeActionResultDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CsdtRealtimeActionResultDto>> Retry(
        string streamCode,
        [FromBody] CsdtRealtimeRetryRequest request,
        CancellationToken cancellationToken)
        => await RunActionAsync(
            () => _service.QueueRetryAsync(
                streamCode,
                request,
                CurrentUser(),
                cancellationToken));

    [HttpGet("reverse-plan")]
    [ProducesResponseType(typeof(CsdtReversePlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CsdtReversePlanDto>> ReversePlan(
        [FromQuery] string vehicleType,
        [FromQuery] string? maKhoaHoc,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.GetReversePlanAsync(
                vehicleType,
                maKhoaHoc,
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
        catch (CsdtRealtimeStoreUnavailableException exception)
        {
            return StoreUnavailable(exception);
        }
    }

    [HttpPost("reverse-execute")]
    [Authorize(Policy = AuthPolicies.CanSynchronizeCSDT)]
    [ProducesResponseType(typeof(CsdtReverseExecuteResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(CsdtReverseExecuteResultDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CsdtReverseExecuteResultDto>> ReverseExecute(
        [FromBody] CsdtReverseExecuteRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.ExecuteReverseAsync(
                request,
                CurrentUser(),
                cancellationToken);
            if (result.Accepted)
            {
                return Accepted(result);
            }

            if (IsConflictOrRejected(result.Status))
            {
                return Conflict(result);
            }

            return BadRequest(result);
        }
        catch (CsdtRealtimeAuthorizationException)
        {
            return Forbid();
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
        catch (CsdtRealtimeStoreUnavailableException exception)
        {
            return StoreUnavailable(exception);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Khong the thuc hien dong bo.",
                Detail = exception.Message,
            });
        }
    }

    private async Task<ActionResult<CsdtRealtimeActionResultDto>> RunActionAsync(
        Func<Task<CsdtRealtimeActionResultDto>> action)
    {
        try
        {
            var result = await action();
            if (result.Accepted)
            {
                return Accepted(result);
            }

            if (IsConflictOrRejected(result.Status))
            {
                return Conflict(result);
            }

            return string.Equals(
                    result.Status,
                    CsdtRealtimeActionStatuses.Unavailable,
                    StringComparison.Ordinal)
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, result)
                : BadRequest(result);
        }
        catch (CsdtRealtimeAuthorizationException)
        {
            return Forbid();
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
        catch (CsdtRealtimeStoreUnavailableException exception)
        {
            return StoreUnavailable(exception);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Khong the thuc hien dong bo.",
                Detail = exception.Message,
            });
        }
    }

    private CsdtRealtimeUserContext CurrentUser()
    {
        var role = AppRoles.SelectPrimary(
            User.FindAll(ClaimTypes.Role).Select(claim => claim.Value)) ?? string.Empty;
        return new CsdtRealtimeUserContext(
            User.FindFirstValue(ClaimTypes.Name) ?? "AUTHENTICATED_USER",
            role,
            User.IsInRole(AppRoles.Admin));
    }

    private static bool IsConflictOrRejected(string status)
        => string.Equals(status, CsdtRealtimeActionStatuses.Conflict, StringComparison.Ordinal) ||
           string.Equals(status, CsdtRealtimeActionStatuses.Rejected, StringComparison.Ordinal);

    private BadRequestObjectResult InvalidRequest(Exception exception)
        => BadRequest(new ProblemDetails
        {
            Title = "Yeu cau dong bo khong hop le.",
            Detail = exception.Message,
        });

    private ObjectResult StoreUnavailable(Exception exception)
        => StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            new ProblemDetails
            {
                Title = "Realtime sync state chua san sang.",
                Detail = exception.Message,
            });
}

using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QLHV.Application.Auth;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Api.Controllers;

/// <summary>
/// Legacy local-machine session-start bridge. The Desktop launcher no longer
/// depends on this route; callers must have an authenticated Admin cookie and
/// must also pass the loopback/header checks below.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.CanSynchronizeCSDT)]
[Route("api/dong-bo-v2/qlhv/operations/session-start-sync")]
[Produces("application/json")]
public sealed class QlhvSessionStartController : ControllerBase
{
    internal const string LauncherHeaderName = "X-QLHV-Local-Launcher";
    internal const string LauncherHeaderValue = "session-start-v1";

    private readonly IQlhvAutoSyncService _autoSync;

    public QlhvSessionStartController(IQlhvAutoSyncService autoSync)
    {
        _autoSync = autoSync;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(QlhvSessionStartStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(QlhvSessionStartStatusDto), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<QlhvSessionStartStatusDto>> Status(
        [FromQuery] bool serverStartedByLauncher,
        [FromQuery] Guid? runId,
        CancellationToken cancellationToken)
    {
        if (!IsTrustedLocalLauncherRequest())
        {
            // Do not advertise a privileged local bridge to LAN callers.
            return NotFound();
        }

        try
        {
            return Ok(await _autoSync.GetSessionStartStatusAsync(
                serverStartedByLauncher,
                runId,
                cancellationToken));
        }
        catch (QlhvAutoSyncStoreUnavailableException ex)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new QlhvSessionStartStatusDto
                {
                    Found = false,
                    CanStart = false,
                    State = "unavailable",
                    Blockers = [ex.Message],
                    Message = ex.Message,
                });
        }
    }

    [HttpPost]
    [ProducesResponseType(typeof(QlhvAutoSyncQueueResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(QlhvAutoSyncQueueResultDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(QlhvAutoSyncQueueResultDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(QlhvAutoSyncQueueResultDto), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<QlhvAutoSyncQueueResultDto>> Start(
        [FromBody] QlhvSessionStartSyncRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsTrustedLocalLauncherRequest())
        {
            // Do not advertise a privileged local bridge to LAN callers.
            return NotFound();
        }

        var result = await _autoSync.QueueSessionStartAsync(
            request?.ServerStartedByLauncher ?? false,
            cancellationToken);
        if (result.Accepted)
        {
            return Accepted(result);
        }

        return result.IsUnavailable
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, result)
            : result.IsConflict
                ? Conflict(result)
                : BadRequest(result);
    }

    private bool IsTrustedLocalLauncherRequest()
    {
        var remoteAddress = HttpContext.Connection.RemoteIpAddress;
        if (remoteAddress is null)
        {
            return false;
        }

        if (remoteAddress.IsIPv4MappedToIPv6)
        {
            remoteAddress = remoteAddress.MapToIPv4();
        }

        return IPAddress.IsLoopback(remoteAddress) &&
               Request.Headers.TryGetValue(LauncherHeaderName, out var marker) &&
               string.Equals(
                   marker.ToString(),
                   LauncherHeaderValue,
                   StringComparison.Ordinal);
    }
}

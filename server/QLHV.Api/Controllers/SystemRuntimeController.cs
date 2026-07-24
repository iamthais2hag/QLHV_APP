using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QLHV.Application.Auth;
using QLHV.Application.Runtime;
using QLHV.Application.Sync;
using QLHV.Application.SystemData;

namespace QLHV.Api.Controllers;

[ApiController]
[Route("api/system")]
[Produces("application/json")]
public sealed class SystemRuntimeController : ControllerBase
{
    private readonly IRuntimeReadinessService _readiness;
    private readonly ISystemDataVersionService _dataVersion;

    public SystemRuntimeController(
        IRuntimeReadinessService readiness,
        ISystemDataVersionService dataVersion)
    {
        _readiness = readiness;
        _dataVersion = dataVersion;
    }

    [AllowAnonymous]
    [HttpGet("runtime-status")]
    [ProducesResponseType(typeof(RuntimeStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RuntimeStatusDto>> GetRuntimeStatus(
        CancellationToken cancellationToken)
    {
        var status = await _readiness.GetStatusAsync(cancellationToken);
        return Ok(status);
    }

    [Authorize(Policy = AuthPolicies.CanViewBusinessData)]
    [HttpGet("data-version")]
    [ProducesResponseType(typeof(SystemDataVersionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SystemDataVersionDto>> GetDataVersion(
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _dataVersion.GetAsync(cancellationToken));
        }
        catch (QlhvAutoSyncStoreUnavailableException ex)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails
                {
                    Title = "Data version chua san sang.",
                    Detail = ex.Message,
                });
        }
    }
}

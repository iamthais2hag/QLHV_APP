using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QLHV.Application.Runtime;

namespace QLHV.Api.Controllers;

[ApiController]
[Route("api/system")]
[Produces("application/json")]
public sealed class SystemRuntimeController : ControllerBase
{
    private readonly IRuntimeReadinessService _readiness;

    public SystemRuntimeController(IRuntimeReadinessService readiness)
    {
        _readiness = readiness;
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
}

using Microsoft.AspNetCore.Mvc;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Api.Controllers;

[ApiController]
[Route("api/dong-bo-v2/qlhv")]
[Produces("application/json")]
public sealed class QlhvImportController : ControllerBase
{
    private readonly IQlhvImportService _importService;

    public QlhvImportController(IQlhvImportService importService)
    {
        _importService = importService;
    }

    /// <summary>
    /// Read-only plan for importing one configured CSDT profile into QLHV_APP.
    /// </summary>
    [HttpGet("import-plan")]
    [ProducesResponseType(typeof(QlhvImportPlanDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<QlhvImportPlanDto>> ImportPlan(
        [FromQuery] QlhvImportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _importService.GetPlanAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Read-only source/target safety diagnostics for a QLHV import request.
    /// </summary>
    [HttpGet("import-diagnostics")]
    [ProducesResponseType(typeof(QlhvImportDiagnosticsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<QlhvImportDiagnosticsDto>> ImportDiagnostics(
        [FromQuery] QlhvImportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _importService.GetDiagnosticsAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Guarded import from one configured CSDT profile into QLHV_APP.dbo.App_HocVien.
    /// </summary>
    [HttpPost("import-execute")]
    [ProducesResponseType(typeof(QlhvImportExecuteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(QlhvImportExecuteResultDto), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QlhvImportExecuteResultDto>> ImportExecute(
        [FromBody] QlhvImportExecuteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _importService.ExecuteAsync(request, cancellationToken);
        return result.Executed ? Ok(result) : Conflict(result);
    }
}

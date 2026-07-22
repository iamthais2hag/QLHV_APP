using Microsoft.AspNetCore.Mvc;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Api.Controllers;

[ApiController]
[Route("api/dong-bo-v2/qlhv")]
[Produces("application/json")]
public sealed class QlhvImportController : ControllerBase
{
    public const string OperationsKeyHeaderName = "X-QLHV-Operations-Key";

    private readonly IQlhvImportService _importService;
    private readonly IQlhvOperationsService _operationsService;
    private readonly IQlhvOperationsKeyValidator _operationsKeyValidator;

    public QlhvImportController(
        IQlhvImportService importService,
        IQlhvOperationsService operationsService,
        IQlhvOperationsKeyValidator operationsKeyValidator)
    {
        _importService = importService;
        _operationsService = operationsService;
        _operationsKeyValidator = operationsKeyValidator;
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
        var keyGuard = ValidateOperationsKey();
        if (keyGuard is not null)
        {
            return keyGuard;
        }

        var result = await _importService.ExecuteAsync(request, cancellationToken);
        return result.Executed ? Ok(result) : Conflict(result);
    }

    /// <summary>Read-only operational status for the fixed OTO or MOTO source.</summary>
    [HttpGet("operations/status")]
    [ProducesResponseType(typeof(QlhvOperationsStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<QlhvOperationsStatusDto>> OperationsStatus(
        [FromQuery] string sourceType,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _operationsService.GetStatusAsync(sourceType, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "SourceType khong hop le.", Detail = ex.Message });
        }
    }

    /// <summary>Queues a guarded live-to-BAK database refresh.</summary>
    [HttpPost("operations/refresh-backup")]
    [ProducesResponseType(typeof(QlhvRefreshBackupResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(QlhvRefreshBackupResultDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(QlhvRefreshBackupResultDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<QlhvRefreshBackupResultDto>> RefreshBackup(
        [FromBody] QlhvRefreshBackupRequest request,
        CancellationToken cancellationToken)
    {
        var keyGuard = ValidateOperationsKey();
        if (keyGuard is not null)
        {
            return keyGuard;
        }

        var result = await _operationsService.QueueRefreshBackupAsync(request, cancellationToken);
        if (result.Accepted)
        {
            return Accepted(result);
        }

        if (result.IsConflict)
        {
            return Conflict(result);
        }

        return result.IsUnavailable
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, result)
            : BadRequest(result);
    }

    /// <summary>Read-only recent operation history for the fixed OTO or MOTO source.</summary>
    [HttpGet("operations/history")]
    [ProducesResponseType(typeof(IReadOnlyList<QlhvOperationHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<QlhvOperationHistoryDto>>> OperationsHistory(
        [FromQuery] string sourceType,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _operationsService.GetHistoryAsync(sourceType, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "SourceType khong hop le.", Detail = ex.Message });
        }
        catch (QlhvOperationsStoreUnavailableException ex)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails { Title = "Lich su van hanh chua san sang.", Detail = ex.Message });
        }
    }

    private ActionResult? ValidateOperationsKey()
    {
        if (!_operationsKeyValidator.IsConfigured)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails
                {
                    Title = "Operations key chua duoc cau hinh.",
                    Detail = "Dat bien moi truong QlhvOperations__AdminKey tren API server.",
                });
        }

        var provided = Request.Headers[OperationsKeyHeaderName].FirstOrDefault();
        if (!_operationsKeyValidator.IsValid(provided))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Operations key khong hop le.",
                Detail = "Endpoint ghi yeu cau operations key hop le.",
            });
        }

        return null;
    }
}

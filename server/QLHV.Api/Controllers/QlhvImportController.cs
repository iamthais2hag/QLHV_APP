using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using QLHV.Application.Auth;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthPolicies.CanViewBusinessData)]
[Route("api/dong-bo-v2/qlhv")]
[Produces("application/json")]
public sealed class QlhvImportController : ControllerBase
{
    private readonly IQlhvImportService _importService;
    private readonly IQlhvOperationsService _operationsService;
    private readonly IQlhvAutoSyncService _autoSyncService;

    public QlhvImportController(
        IQlhvImportService importService,
        IQlhvOperationsService operationsService,
        IQlhvAutoSyncService autoSyncService)
    {
        _importService = importService;
        _operationsService = operationsService;
        _autoSyncService = autoSyncService;
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
    [Authorize(Policy = AuthPolicies.CanSynchronizeCSDT)]
    [ProducesResponseType(typeof(QlhvImportExecuteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(QlhvImportExecuteResultDto), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QlhvImportExecuteResultDto>> ImportExecute(
        [FromBody] QlhvImportExecuteRequest request,
        CancellationToken cancellationToken)
    {
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
            var currentUserRole = AppRoles.SelectPrimary(
                User.FindAll(ClaimTypes.Role).Select(claim => claim.Value)) ?? string.Empty;
            var writeAuthorized = User.IsInRole(AppRoles.Admin);
            return Ok(await _operationsService.GetStatusAsync(
                sourceType,
                currentUserRole,
                writeAuthorized,
                cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "SourceType khong hop le.", Detail = ex.Message });
        }
    }

    /// <summary>Queues a guarded live-to-BAK database refresh.</summary>
    [HttpPost("operations/refresh-backup")]
    [Authorize(Policy = AuthPolicies.CanSynchronizeCSDT)]
    [ProducesResponseType(typeof(QlhvRefreshBackupResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(QlhvRefreshBackupResultDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(QlhvRefreshBackupResultDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<QlhvRefreshBackupResultDto>> RefreshBackup(
        [FromBody] QlhvRefreshBackupRequest request,
        CancellationToken cancellationToken)
    {
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

    /// <summary>Read-only status of the durable server-side Auto Sync run.</summary>
    [HttpGet("operations/auto-sync/status")]
    [ProducesResponseType(typeof(QlhvAutoSyncStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<QlhvAutoSyncStatusDto>> AutoSyncStatus(
        [FromQuery] Guid? runId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _autoSyncService.GetStatusAsync(runId, cancellationToken));
        }
        catch (QlhvAutoSyncStoreUnavailableException ex)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails
                {
                    Title = "Auto Sync history chua san sang.",
                    Detail = ex.Message,
                });
        }
    }

    /// <summary>
    /// Read-only, privacy-safe comparison of the fixed Live, BAK and QLHV_APP
    /// scopes used by Auto Sync. It returns counts and aggregate content hashes,
    /// never learner identifiers.
    /// </summary>
    [HttpGet("operations/auto-sync/diagnostics")]
    [ProducesResponseType(typeof(QlhvAutoSyncDataGapDiagnosticsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<QlhvAutoSyncDataGapDiagnosticsDto>> AutoSyncDiagnostics(
        CancellationToken cancellationToken)
    {
        var currentUserRole = AppRoles.SelectPrimary(
            User.FindAll(ClaimTypes.Role).Select(claim => claim.Value)) ?? string.Empty;
        var writeAuthorized = User.IsInRole(AppRoles.Admin);
        var sourceStatuses = new List<QlhvOperationsStatusDto>();
        foreach (var sourceType in new[] { "OTO", "MOTO" })
        {
            sourceStatuses.Add(await _operationsService.GetStatusAsync(
                sourceType,
                currentUserRole,
                writeAuthorized,
                cancellationToken));
        }

        return Ok(new QlhvAutoSyncDataGapDiagnosticsDto
        {
            CapturedAtUtc = DateTime.UtcNow,
            SourceStatuses = sourceStatuses,
            Freshness = await _autoSyncService.GetDiagnosticsAsync(cancellationToken),
            ScopeNotes =
            [
                "SourceStatuses.LiveRows/BackupRows dem toan bang vat ly; khong loc MaCSDT hoac TrangThai.",
                "SourceStatuses.TargetActiveRows loc SourceProfileCode co dinh va IsDeleted = 0 trong QLHV_APP.",
                "Freshness snapshots dung cung request SourceProfileCode/MaCSDT cua import de so sanh noi dung theo pham vi.",
                "ContentToken la SHA-256 cua snapshot chuan hoa; response khong chua MaDK, ho ten hoac giay to.",
                "Neu can ket luan row phat sinh truoc/sau refresh, phai doi chieu probe runtime voi audit timestamp cua CSDL nguon.",
            ],
        });
    }

    /// <summary>
    /// Checks freshness after an authorized user opens the application and queues
    /// at most one system Auto Sync run. The caller cannot select a source, force
    /// execution, or bypass the server-side cooldown.
    /// </summary>
    [HttpPost("operations/ensure-fresh")]
    [Authorize(Policy = AuthPolicies.CanEditBusinessData)]
    [ProducesResponseType(typeof(QlhvAutoSyncQueueResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(QlhvAutoSyncQueueResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(QlhvAutoSyncQueueResultDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(QlhvAutoSyncQueueResultDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(QlhvAutoSyncQueueResultDto), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<QlhvAutoSyncQueueResultDto>> EnsureFresh(
        CancellationToken cancellationToken)
    {
        var result = await _autoSyncService.QueueEnsureFreshAsync(cancellationToken);
        if (result.Accepted)
        {
            return IsActiveAutoSyncStatus(result.Status)
                ? Accepted(result)
                : Ok(result);
        }

        if (result.IsConflict)
        {
            return Conflict(result);
        }

        return result.IsUnavailable
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, result)
            : BadRequest(result);
    }

    /// <summary>Queues one Admin-triggered OTO then MOTO Auto Sync run.</summary>
    [HttpPost("operations/auto-sync")]
    [Authorize(Policy = AuthPolicies.CanSynchronizeCSDT)]
    [ProducesResponseType(typeof(QlhvAutoSyncQueueResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(QlhvAutoSyncQueueResultDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(QlhvAutoSyncQueueResultDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(QlhvAutoSyncQueueResultDto), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<QlhvAutoSyncQueueResultDto>> AutoSync(
        CancellationToken cancellationToken)
    {
        var result = await _autoSyncService.QueueAsync(
            QlhvAutoSyncConstants.ManualTrigger,
            cancellationToken);
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

    private static bool IsActiveAutoSyncStatus(string status)
        => string.Equals(status, QlhvAutoSyncConstants.Queued, StringComparison.Ordinal) ||
           string.Equals(status, QlhvAutoSyncConstants.Running, StringComparison.Ordinal);
}

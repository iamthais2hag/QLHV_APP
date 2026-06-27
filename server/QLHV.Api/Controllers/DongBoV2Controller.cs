using Microsoft.AspNetCore.Mvc;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Api.Controllers;

/// <summary>
/// API for one-way sync from CSDT_V2 to QLHV_APP.
/// Phase A exposes dry-run only. It does not write SQL Server data or return secrets.
/// </summary>
[ApiController]
[Route("api/dong-bo-v2")]
[Produces("application/json")]
public sealed class DongBoV2Controller : ControllerBase
{
    private readonly IHocVienSyncService _syncService;

    public DongBoV2Controller(IHocVienSyncService syncService)
    {
        _syncService = syncService;
    }

    /// <summary>
    /// Safe local configuration check for HocVien sync. Does not return connection strings or credentials.
    /// </summary>
    [HttpGet("hoc-vien/config-check")]
    [ProducesResponseType(typeof(SyncConfigCheckDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SyncConfigCheckDto>> ConfigCheckHocVien(CancellationToken cancellationToken)
    {
        var result = await _syncService.ConfigCheckHocVienAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>Dry-run HocVien sync configuration and mapping plan.</summary>
    [HttpPost("hoc-vien/dry-run")]
    [ProducesResponseType(typeof(DryRunResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DryRunResultDto>> DryRunHocVien(CancellationToken cancellationToken)
    {
        var result = await _syncService.DryRunHocVienAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Read-only source diagnostics for HocVien data in CSDT_V2 before any guarded execute run.
    /// Returns aggregate counts only; does not write data or expose connection details.
    /// </summary>
    [HttpGet("hoc-vien/source-diagnostics")]
    [ProducesResponseType(typeof(V2HocVienSourceDiagnosticsResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<V2HocVienSourceDiagnosticsResultDto>> SourceDiagnosticsHocVien(
        CancellationToken cancellationToken)
    {
        var result = await _syncService.GetHocVienSourceDiagnosticsAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Read-only target diagnostics for HocVien data in QLHV_APP before any guarded execute run.
    /// Returns schema and aggregate counts only; does not write data or expose connection details.
    /// </summary>
    [HttpGet("hoc-vien/target-diagnostics")]
    [ProducesResponseType(typeof(QlhvHocVienTargetDiagnosticsResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<QlhvHocVienTargetDiagnosticsResultDto>> TargetDiagnosticsHocVien(
        CancellationToken cancellationToken)
    {
        var result = await _syncService.GetHocVienTargetDiagnosticsAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Read-only pre-execute plan for HocVien sync. Compares source rows with target hashes without writing data.
    /// </summary>
    [HttpGet("hoc-vien/pre-execute-plan")]
    [ProducesResponseType(typeof(HocVienPreExecutePlanResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<HocVienPreExecutePlanResultDto>> PreExecutePlanHocVien(
        CancellationToken cancellationToken)
    {
        var result = await _syncService.GetHocVienPreExecutePlanAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Guarded manual execution for HocVien sync.
    /// Defaults reject unless server config enables writes and the request includes explicit confirmation.
    /// </summary>
    [HttpPost("hoc-vien/execute")]
    [ProducesResponseType(typeof(SyncExecuteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SyncExecuteResultDto), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SyncExecuteResultDto>> ExecuteHocVien(
        [FromBody] SyncExecuteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _syncService.ExecuteHocVienAsync(request, cancellationToken);
        if (!result.Executed)
        {
            return Conflict(result);
        }

        return Ok(result);
    }
}

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

    /// <summary>Dry-run HocVien sync configuration and mapping plan.</summary>
    [HttpPost("hoc-vien/dry-run")]
    [ProducesResponseType(typeof(DryRunResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DryRunResultDto>> DryRunHocVien(CancellationToken cancellationToken)
    {
        var result = await _syncService.DryRunHocVienAsync(cancellationToken);
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

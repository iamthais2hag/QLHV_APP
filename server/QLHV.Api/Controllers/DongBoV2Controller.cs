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
}

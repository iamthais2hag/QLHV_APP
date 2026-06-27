using Microsoft.AspNetCore.Mvc;
using QLHV.Application.CsdtConnections;
using QLHV.Application.CsdtConnections.Dtos;

namespace QLHV.Api.Controllers;

/// <summary>
/// API contract for the CSDT connection profile admin menu.
/// Responses never include plaintext passwords or encrypted password bytes.
/// </summary>
[ApiController]
[Route("api/csdt-connection-profiles")]
[Produces("application/json")]
public sealed class CsdtConnectionProfilesController : ControllerBase
{
    private readonly ICsdtConnectionProfileService _service;

    public CsdtConnectionProfilesController(ICsdtConnectionProfileService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CsdtConnectionProfileListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CsdtConnectionProfileListItemDto>>> GetProfiles(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.GetProfilesAsync(cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return ToSafeError<IReadOnlyList<CsdtConnectionProfileListItemDto>>(ex);
        }
    }

    [HttpGet("{profileCode}")]
    [ProducesResponseType(typeof(CsdtConnectionProfileDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CsdtConnectionProfileDetailDto>> GetProfile(
        [FromRoute] string profileCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.GetProfileAsync(profileCode, cancellationToken);
            return result is null
                ? NotFound(SafeError("PROFILE_NOT_FOUND", "Profile khong ton tai trong bang cau hinh."))
                : Ok(result);
        }
        catch (Exception ex)
        {
            return ToSafeError<CsdtConnectionProfileDetailDto>(ex);
        }
    }

    [HttpPut("{profileCode}")]
    [ProducesResponseType(typeof(CsdtConnectionProfileDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CsdtConnectionProfileDetailDto>> SaveProfile(
        [FromRoute] string profileCode,
        [FromBody] SaveCsdtConnectionProfileRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request is null)
            {
                return BadRequest(SafeError("REQUEST_REQUIRED", "Thieu body cau hinh."));
            }

            var result = await _service.SaveProfileAsync(profileCode, request, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return ToSafeError<CsdtConnectionProfileDetailDto>(ex);
        }
    }

    [HttpPost("{profileCode}/test")]
    [ProducesResponseType(typeof(TestCsdtConnectionProfileResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TestCsdtConnectionProfileResultDto>> TestProfile(
        [FromRoute] string profileCode,
        [FromBody] TestCsdtConnectionProfileRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.TestProfileAsync(profileCode, request, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return ToSafeError<TestCsdtConnectionProfileResultDto>(ex);
        }
    }

    private ActionResult<T> ToSafeError<T>(Exception exception)
    {
        if (exception is CsdtConnectionProfileException profileException)
        {
            return StatusCode(
                profileException.StatusCode,
                SafeError(profileException.Code, profileException.Message));
        }

        return StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            SafeError(
                "CSDT_CONNECTION_PROFILE_UNAVAILABLE",
                $"Khong doc/ghi duoc cau hinh ket noi CSDT. Chi tiet an toan: {exception.GetType().Name}."));
    }

    private static object SafeError(string code, string message) => new
    {
        code,
        message,
    };
}

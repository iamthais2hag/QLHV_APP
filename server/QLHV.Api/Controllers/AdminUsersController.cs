using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QLHV.Application.Auth;

namespace QLHV.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthPolicies.CanManageUsers)]
[Route("api/admin/users")]
[Produces("application/json")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IAppUserManagementService _users;

    public AdminUsersController(IAppUserManagementService users)
    {
        _users = users;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AppUserListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AppUserListItemDto>>> List(
        CancellationToken cancellationToken) =>
        Ok(await _users.ListAsync(cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(AppUserListItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AppUserListItemDto>> Create(
        [FromBody] CreateAppUserRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorUserId, out var actorUsername))
        {
            return Unauthorized();
        }

        var result = await _users.CreateAsync(
            request,
            actorUserId,
            actorUsername,
            cancellationToken);
        return result.Succeeded && result.User is not null
            ? StatusCode(StatusCodes.Status201Created, result.User)
            : ToError<AppUserListItemDto>(result);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType(typeof(AppUserListItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AppUserListItemDto>> Update(
        long id,
        [FromBody] UpdateAppUserRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorUserId, out var actorUsername))
        {
            return Unauthorized();
        }

        var result = await _users.UpdateAsync(
            id,
            request,
            actorUserId,
            actorUsername,
            cancellationToken);
        return result.Succeeded && result.User is not null
            ? Ok(result.User)
            : ToError<AppUserListItemDto>(result);
    }

    [HttpPost("{id:long}/reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(
        long id,
        [FromBody] ResetAppUserPasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorUserId, out var actorUsername))
        {
            return Unauthorized();
        }

        var result = await _users.ResetPasswordAsync(
            id,
            request,
            actorUserId,
            actorUsername,
            cancellationToken);
        return result.Succeeded
            ? NoContent()
            : ToError(result);
    }

    private bool TryGetActor(out long userId, out string username)
    {
        username = User.FindFirstValue(ClaimTypes.Name)?.Trim() ?? string.Empty;
        return long.TryParse(
                   User.FindFirstValue(ClaimTypes.NameIdentifier),
                   System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out userId) &&
               userId > 0 &&
               username.Length is >= 1 and <= 100;
    }

    private ActionResult<T> ToError<T>(AppUserManagementResult result)
    {
        var error = ToErrorResult(result);
        return StatusCode(error.StatusCode ?? StatusCodes.Status400BadRequest, error.Value);
    }

    private IActionResult ToError(AppUserManagementResult result) =>
        ToErrorResult(result);

    private ObjectResult ToErrorResult(AppUserManagementResult result)
    {
        var status = result.Status switch
        {
            AppUserManagementStatus.NotFound => StatusCodes.Status404NotFound,
            AppUserManagementStatus.UsernameExists or
            AppUserManagementStatus.SelfDeactivationDenied or
            AppUserManagementStatus.LastActiveAdminDenied or
            AppUserManagementStatus.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return StatusCode(
            status,
            new ProblemDetails
            {
                Status = status,
                Title = "Không thể cập nhật tài khoản.",
                Detail = result.Message,
            });
    }
}

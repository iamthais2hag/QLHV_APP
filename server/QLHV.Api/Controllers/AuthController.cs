using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QLHV.Application.Auth;

namespace QLHV.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthSessionDto>> Login(
        [FromBody] LoginRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.AuthenticateAsync(request, cancellationToken);
        if (!result.Succeeded || result.Session is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Dang nhap khong thanh cong.",
                Detail = "Ten dang nhap hoac mat khau khong dung, hoac tai khoan khong hoat dong.",
            });
        }

        var session = result.Session;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, session.Username),
            new(ClaimTypes.GivenName, session.DisplayName),
        };
        claims.AddRange(session.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                AllowRefresh = true,
                IsPersistent = true,
            });

        return Ok(session);
    }

    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(AuthSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<AuthSessionDto> Me()
    {
        var idValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var username = User.FindFirstValue(ClaimTypes.Name);
        var displayName = User.FindFirstValue(ClaimTypes.GivenName);
        var roles = User.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Where(AppRoles.IsKnown)
            .Select(AppRoles.Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var primaryRole = AppRoles.SelectPrimary(roles);

        if (!long.TryParse(idValue, out var id) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(displayName) ||
            primaryRole is null)
        {
            return Unauthorized();
        }

        return Ok(new AuthSessionDto
        {
            Id = id,
            Username = username,
            DisplayName = displayName,
            Role = primaryRole,
            Roles = roles,
        });
    }
}

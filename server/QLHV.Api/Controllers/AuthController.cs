using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QLHV.Application.Auth;
using QLHV.Application.Runtime;

namespace QLHV.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IAppUserManagementService _userManagement;
    private readonly IRuntimeReadinessService _readiness;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        IAppUserManagementService userManagement,
        IRuntimeReadinessService readiness,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _userManagement = userManagement;
        _readiness = readiness;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), 423)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AuthSessionDto>> Login(
        [FromBody] LoginRequestDto request,
        CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        RuntimeStatusDto runtimeStatus;
        try
        {
            runtimeStatus = await _readiness.GetStatusAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Login readiness check failed. CorrelationId={CorrelationId}; FailureType={FailureType}",
                correlationId,
                exception.GetType().Name);
            return RuntimeUnavailable(correlationId);
        }

        if (!runtimeStatus.IsReady)
        {
            _logger.LogWarning(
                "Login refused because runtime is not ready. CorrelationId={CorrelationId}",
                correlationId);
            return RuntimeUnavailable(correlationId);
        }

        AuthLoginResult result;
        try
        {
            result = await _authService.AuthenticateAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Login authentication store is unavailable. CorrelationId={CorrelationId}; FailureType={FailureType}",
                correlationId,
                exception.GetType().Name);
            return RuntimeUnavailable(correlationId);
        }

        if (!result.Succeeded || result.Session is null)
        {
            if (string.Equals(result.FailureCode, "ACCOUNT_LOCKED", StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Login refused because account is temporarily locked. CorrelationId={CorrelationId}",
                    correlationId);
                return StatusCode(423, Error(
                    423,
                    "Tài khoản tạm khóa.",
                    "Tài khoản đang tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau.",
                    correlationId));
            }

            _logger.LogWarning(
                "Login rejected. CorrelationId={CorrelationId}",
                correlationId);
            return Unauthorized(Error(
                StatusCodes.Status401Unauthorized,
                "Đăng nhập không thành công.",
                "Tên đăng nhập hoặc mật khẩu không đúng.",
                correlationId));
        }

        var session = result.Session;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, session.Username),
            new(ClaimTypes.GivenName, session.DisplayName),
            new(
                AppClaimTypes.MustChangePassword,
                AppClaimTypes.ToClaimValue(session.MustChangePassword)),
            new(
                AppClaimTypes.SecurityStamp,
                AppClaimTypes.ToClaimValue(result.SecurityStamp)),
        };
        claims.AddRange(session.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme));
        try
        {
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    AllowRefresh = true,
                    IsPersistent = true,
                });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Login cookie creation failed. CorrelationId={CorrelationId}; FailureType={FailureType}",
                correlationId,
                exception.GetType().Name);
            return RuntimeUnavailable(correlationId);
        }

        return Ok(session);
    }

    private ObjectResult RuntimeUnavailable(string correlationId) => StatusCode(
        StatusCodes.Status503ServiceUnavailable,
        Error(
            StatusCodes.Status503ServiceUnavailable,
            "Hệ thống chưa sẵn sàng.",
            "Hệ thống chưa sẵn sàng. Vui lòng liên hệ quản trị viên.",
            correlationId));

    private static ProblemDetails Error(
        int status,
        string title,
        string detail,
        string correlationId)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
        };
        problem.Extensions["correlationId"] = correlationId;
        return problem;
    }

    [Authorize]
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangeOwnPasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var userId) ||
            userId <= 0 ||
            string.IsNullOrWhiteSpace(User.FindFirstValue(ClaimTypes.Name)))
        {
            return Unauthorized();
        }

        var result = await _userManagement.ChangeOwnPasswordAsync(
            userId,
            User.FindFirstValue(ClaimTypes.Name)!,
            request,
            cancellationToken);
        if (result.Status == AppUserManagementStatus.Success &&
            result.SecurityStamp is { } securityStamp)
        {
            var retainedClaims = User.Claims
                .Where(claim =>
                    !string.Equals(
                        claim.Type,
                        AppClaimTypes.MustChangePassword,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        claim.Type,
                        AppClaimTypes.SecurityStamp,
                        StringComparison.Ordinal))
                .ToList();
            retainedClaims.Add(new Claim(
                AppClaimTypes.MustChangePassword,
                AppClaimTypes.FalseValue));
            retainedClaims.Add(new Claim(
                AppClaimTypes.SecurityStamp,
                AppClaimTypes.ToClaimValue(securityStamp)));
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(
                    retainedClaims,
                    CookieAuthenticationDefaults.AuthenticationScheme)),
                new AuthenticationProperties
                {
                    AllowRefresh = true,
                    IsPersistent = true,
                });
            return NoContent();
        }

        return result.Status switch
        {
            AppUserManagementStatus.Success => Conflict(
                ManagementError(
                    StatusCodes.Status409Conflict,
                    "Không thể làm mới phiên đăng nhập sau khi đổi mật khẩu.")),
            AppUserManagementStatus.NotFound => NotFound(
                ManagementError(StatusCodes.Status404NotFound, result.Message)),
            AppUserManagementStatus.Conflict => Conflict(
                ManagementError(StatusCodes.Status409Conflict, result.Message)),
            _ => BadRequest(
                ManagementError(StatusCodes.Status400BadRequest, result.Message)),
        };
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
            MustChangePassword = string.Equals(
                User.FindFirstValue(AppClaimTypes.MustChangePassword),
                AppClaimTypes.TrueValue,
                StringComparison.Ordinal),
        });
    }

    private static ProblemDetails ManagementError(int status, string detail) => new()
    {
        Status = status,
        Title = "Không thể đổi mật khẩu.",
        Detail = detail,
    };
}

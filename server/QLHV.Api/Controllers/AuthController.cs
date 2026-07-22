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
    private readonly IRuntimeReadinessService _readiness;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        IRuntimeReadinessService readiness,
        ILogger<AuthController> logger)
    {
        _authService = authService;
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

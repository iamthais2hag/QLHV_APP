using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using QLHV.Application.Auth;

namespace QLHV.Api.Auth;

public sealed class QlhvCookieAuthenticationEvents : CookieAuthenticationEvents
{
    private readonly IAppUserRepository _users;

    public QlhvCookieAuthenticationEvents(IAppUserRepository users)
    {
        _users = users;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var idValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idValue, out var userId))
        {
            await RejectAsync(context);
            return;
        }

        AppUserCredential? user;
        try
        {
            user = await _users.FindByIdAsync(userId, context.HttpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Authorization fails closed if current account state cannot be verified.
            await RejectAsync(context);
            return;
        }

        var roles = user?.Roles
            .Where(AppRoles.IsKnown)
            .Select(AppRoles.Normalize)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(role => string.Equals(role, AppRoles.Admin, StringComparison.Ordinal) ? 0 : 1)
            .ToArray() ?? [];

        if (user is null ||
            !user.IsActive ||
            user.IsDeleted ||
            string.IsNullOrWhiteSpace(user.PasswordHash) ||
            string.IsNullOrWhiteSpace(user.Username) ||
            string.IsNullOrWhiteSpace(user.DisplayName) ||
            roles.Length == 0)
        {
            await RejectAsync(context);
            return;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, user.DisplayName),
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var refreshedPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme));
        var changed = !HasEquivalentIdentity(context.Principal, refreshedPrincipal);
        context.ReplacePrincipal(refreshedPrincipal);
        context.ShouldRenew = changed;
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static bool HasEquivalentIdentity(
        ClaimsPrincipal? current,
        ClaimsPrincipal refreshed)
    {
        static string[] Values(ClaimsPrincipal? principal, string claimType) =>
            principal?.FindAll(claimType)
                .Select(claim => claim.Value)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray() ?? [];

        return Values(current, ClaimTypes.NameIdentifier)
                   .SequenceEqual(Values(refreshed, ClaimTypes.NameIdentifier), StringComparer.Ordinal) &&
               Values(current, ClaimTypes.Name)
                   .SequenceEqual(Values(refreshed, ClaimTypes.Name), StringComparer.Ordinal) &&
               Values(current, ClaimTypes.GivenName)
                   .SequenceEqual(Values(refreshed, ClaimTypes.GivenName), StringComparer.Ordinal) &&
               Values(current, ClaimTypes.Role)
                   .SequenceEqual(Values(refreshed, ClaimTypes.Role), StringComparer.Ordinal);
    }
}

using Microsoft.AspNetCore.Mvc;
using QLHV.Application.Runtime;

namespace QLHV.Api.Runtime;

/// <summary>
/// Fails closed for production API mutations only when the one-query SQL UTC
/// contract is unavailable. W32Time/NTP values are diagnostics and are never
/// consulted on this path.
/// </summary>
public sealed class TimeAuthorityWriteGuardMiddleware
{
    private readonly RequestDelegate _next;

    public TimeAuthorityWriteGuardMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITimeAuthorityService timeAuthority)
    {
        if (!context.Request.Path.StartsWithSegments("/api") ||
            HttpMethods.IsGet(context.Request.Method) ||
            HttpMethods.IsHead(context.Request.Method) ||
            HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        TimeHealthDto health;
        try
        {
            health = await timeAuthority.GetWriteAuthorizationAsync(
                context.RequestAborted);
        }
        catch (OperationCanceledException)
            when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            health = new TimeHealthDto
            {
                TimeHealth = TimeHealthStatuses.Blocked,
                ReasonCode = TimeHealthReasonCodes.DatabaseUtcUnavailable,
                WritesAllowed = false,
                DatabaseClockAvailable = false,
                ServerUtcNow = DateTimeOffset.UtcNow,
                EvaluatedAtUtc = DateTimeOffset.UtcNow,
                TimeZone = TimeZoneInfo.Local.Id,
                Messages =
                [
                    "Không thể đọc SYSUTCDATETIME() từ SQL Server."
                ],
            };
        }

        if (TimeAuthorityPolicy.IsMutationAllowed(health))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Thao tác ghi bị chặn vì SQL Server không sẵn sàng.",
                Detail = health.Messages.FirstOrDefault() ??
                    "Không thể đọc SYSUTCDATETIME() từ SQL Server.",
                Extensions =
                {
                    ["code"] = "DATABASE_CLOCK_UNAVAILABLE",
                    ["timeHealth"] = health.TimeHealth,
                    ["reasonCode"] = health.ReasonCode,
                    ["databaseUtcNow"] = health.DatabaseUtcNow,
                    ["databaseClockAvailable"] = health.DatabaseClockAvailable,
                },
            },
            cancellationToken: context.RequestAborted);
    }
}

// QLHV.Api - startup/host project
// NOTE: Real connection strings and secrets must be supplied via
// user-secrets or environment variables. appsettings.json contains
// placeholders only and must never hold production credentials.

using System.Reflection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using QLHV.Api.Auth;
using QLHV.Api.Runtime;
using QLHV.Application;
using QLHV.Application.Auth;
using QLHV.Application.Runtime;
using QLHV.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var runtimeConfigurationState = ProductionLocalConfigurationLoader.Load(
    builder.Configuration,
    builder.Environment,
    args);
builder.Services.AddSingleton(runtimeConfigurationState);

if (builder.Environment.IsProduction())
{
    // Production must not persist raw console output because framework/third-party exceptions can
    // contain environment details. The runtime uses only the bounded, redacting file provider.
    builder.Logging.ClearProviders();
    if (builder.Configuration.GetValue("QlhvRuntime:FileLogging:Enabled", true))
    {
        try
        {
            var runtimeRoot = builder.Configuration["QlhvRuntime:Root"];
            if (string.IsNullOrWhiteSpace(runtimeRoot))
            {
                runtimeRoot = ProductionLocalConfigurationLoader.DefaultRuntimeRoot;
            }

            var logDirectory = Path.Combine(Path.GetFullPath(runtimeRoot), "logs");
            builder.Logging.AddProvider(new QlhvRollingFileLoggerProvider(
                logDirectory,
                builder.Configuration.GetValue<long?>("QlhvRuntime:FileLogging:MaxFileSizeBytes")
                    ?? 10 * 1024 * 1024,
                builder.Configuration.GetValue<int?>("QlhvRuntime:FileLogging:RetainedFileCount")
                    ?? 14));
        }
        catch
        {
            // Launcher diagnostics remain available. Never echo configuration values here.
        }
    }
}

// MVC controllers
builder.Services.AddControllers();
builder.Services.AddScoped<QlhvCookieAuthenticationEvents>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "QLHV.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Configuration.GetValue(
            "Authentication:Cookie:SecurePolicy",
            builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always);
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.EventsType = typeof(QlhvCookieAuthenticationEvents);
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(AppClaimTypes.MustChangePassword, bool.FalseString.ToLowerInvariant())
        .Build();
    options.AddPolicy(AuthPolicies.CanViewBusinessData, policy =>
        policy
            .RequireRole(AppRoles.Admin, AppRoles.Employee, AppRoles.Viewer)
            .RequireClaim(AppClaimTypes.MustChangePassword, bool.FalseString.ToLowerInvariant()));
    options.AddPolicy(AuthPolicies.CanEditBusinessData, policy =>
        policy
            .RequireRole(AppRoles.Admin, AppRoles.Employee)
            .RequireClaim(AppClaimTypes.MustChangePassword, bool.FalseString.ToLowerInvariant()));
    options.AddPolicy(AuthPolicies.RequireAdmin, policy =>
        policy
            .RequireRole(AppRoles.Admin)
            .RequireClaim(AppClaimTypes.MustChangePassword, bool.FalseString.ToLowerInvariant()));
    options.AddPolicy(AuthPolicies.CanManageUsers, policy =>
        policy
            .RequireRole(AppRoles.Admin)
            .RequireClaim(AppClaimTypes.MustChangePassword, bool.FalseString.ToLowerInvariant()));
    options.AddPolicy(AuthPolicies.CanSynchronizeCSDT, policy =>
        policy
            .RequireRole(AppRoles.Admin)
            .RequireClaim(AppClaimTypes.MustChangePassword, bool.FalseString.ToLowerInvariant()));
    options.AddPolicy(AuthPolicies.CanImportData, policy =>
        policy
            .RequireRole(AppRoles.Admin)
            .RequireClaim(AppClaimTypes.MustChangePassword, bool.FalseString.ToLowerInvariant()));
});

// In-memory cache for lookups
builder.Services.AddMemoryCache();

// Application + Infrastructure services
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);

// CORS for the internal frontend. Local dev origins are allowed only in Development by default.
const string FrontendCors = "frontend";
var configuredFrontendOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>();
var frontendOrigins = configuredFrontendOrigins is { Length: > 0 }
    ? configuredFrontendOrigins
    : builder.Environment.IsDevelopment()
        ? ["http://localhost:5173", "http://qlhv.local:5173"]
        : [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCors, policy =>
        policy.WithOrigins(frontendOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders("Content-Disposition"));
});

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "QLHV API",
        Version = "v1",
        Description = "API nội bộ QLHV - Trung tâm Đào tạo lái xe Thành Công.",
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

if (FirstAdminSeedCommand.IsRequested(args))
{
    Environment.ExitCode = await FirstAdminSeedCommand.RunAsync(app.Services, Console.Out);
    return;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var enableHttpsRedirection = builder.Configuration.GetValue(
    "HttpsRedirection:Enabled",
    !app.Environment.IsDevelopment());
if (enableHttpsRedirection)
{
    app.UseHttpsRedirection();
}

app.UseDefaultFiles();
app.UseStaticFiles();
// Keep endpoint selection after the static middleware so the catch-all SPA
// endpoint cannot pre-empt real files under wwwroot.
app.UseRouting();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    if (HttpMethods.IsGet(context.Request.Method) &&
        context.Request.Path.StartsWithSegments("/api"))
    {
        // Business data is versioned in QLHV_APP and must be re-read after a
        // successful sync. Never let a browser/proxy serve a stale API snapshot.
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }
    await next();
});
app.UseCors(FrontendCors);
app.UseAuthentication();
app.UseAuthorization();

// Liveness proves only that the process and HTTP pipeline are responding.
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
    .AllowAnonymous();

// Preserve the legacy liveness alias for installed launchers during an update.
app.MapGet("/health", () => Results.Ok(new { status = "live" }))
    .AllowAnonymous();

app.MapGet("/health/ready", async (
        IRuntimeReadinessService readiness,
        CancellationToken cancellationToken) =>
    {
        var status = await readiness.GetStatusAsync(cancellationToken);
        return Results.Json(
            status,
            statusCode: status.IsReady
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable);
    })
    .AllowAnonymous();

app.MapControllers();

// Serve client-side routes from the published SPA without ever disguising an
// unknown API, diagnostics, health, or static-file request as an HTML page.
app.MapMethods("/{**path}", [HttpMethods.Get, HttpMethods.Head], async context =>
    {
        var requestPath = context.Request.Path;
        var isPageRequest = HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method);
        var isReservedPath = requestPath.StartsWithSegments("/api")
            || requestPath.StartsWithSegments("/swagger")
            || requestPath.StartsWithSegments("/health");
        var hasFileExtension = Path.HasExtension(requestPath.Value);

        if (!isPageRequest || isReservedPath || hasFileExtension)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var indexFile = app.Environment.WebRootFileProvider.GetFileInfo("index.html");
        if (!indexFile.Exists)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(indexFile);
    })
    .AllowAnonymous()
    .WithOrder(int.MaxValue);

app.Run();

public partial class Program
{
}

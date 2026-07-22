// QLHV.Api - startup/host project
// NOTE: Real connection strings and secrets must be supplied via
// user-secrets or environment variables. appsettings.json contains
// placeholders only and must never hold production credentials.

using System.Reflection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using QLHV.Api.Auth;
using QLHV.Application;
using QLHV.Application.Auth;
using QLHV.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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
        .Build();
    options.AddPolicy(AuthPolicies.Read, policy =>
        policy.RequireRole(AppRoles.Admin, AppRoles.Viewer));
    options.AddPolicy(AuthPolicies.Admin, policy =>
        policy.RequireRole(AppRoles.Admin));
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
app.UseCors(FrontendCors);
app.UseAuthentication();
app.UseAuthorization();

// Minimal health endpoint to keep the host observable.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
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

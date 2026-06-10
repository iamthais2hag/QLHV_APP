// QLHV.Api - startup/host project
// NOTE: Real connection strings and secrets must be supplied via
// user-secrets or environment variables. appsettings.json contains
// placeholders only and must never hold production credentials.

using System.Reflection;
using Microsoft.OpenApi.Models;
using QLHV.Application;
using QLHV.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// MVC controllers
builder.Services.AddControllers();

// In-memory cache for lookups
builder.Services.AddMemoryCache();

// Application + Infrastructure services
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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

app.UseCors(FrontendCors);
app.UseAuthorization();

// Minimal health endpoint to keep the host observable.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapControllers();

app.Run();

// QLHV.Api - startup/host project
// NOTE: Real connection strings and secrets must be supplied via
// user-secrets or environment variables. appsettings.json contains
// placeholders only and must never hold production credentials.

var builder = WebApplication.CreateBuilder(args);

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Minimal health endpoint to keep the host buildable and runnable.
// Business module endpoints are intentionally NOT added during scaffolding.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

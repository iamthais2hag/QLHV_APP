// QLHV.Worker - Generic Host for Hangfire background jobs.
// NOTE: Real connection strings and secrets must be supplied via
// user-secrets or environment variables. appsettings.json contains
// placeholders only and must never hold production credentials.
//
// This is a minimal, buildable host. Real Hangfire jobs/servers are
// intentionally NOT wired during scaffolding.

var builder = Host.CreateApplicationBuilder(args);

using var host = builder.Build();
await host.RunAsync();

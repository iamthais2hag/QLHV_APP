using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QLHV.Application;
using QLHV.Application.Sync.Rt03;
using QLHV.Application.Sync.TeacherVehicleProjection;
using QLHV.Infrastructure;
using QLHV.Infrastructure.Runtime;

// This executable is a deployment-only Windows Service. Pinning Production here
// prevents a machine or service-manager environment variable from making it load
// appsettings.Development.json. Machine-local values are loaded explicitly below.
// SCM has no working-directory setting, so pin it to the published worker folder.
Environment.CurrentDirectory = AppContext.BaseDirectory;
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    EnvironmentName = Environments.Production,
    ContentRootPath = AppContext.BaseDirectory,
});
_ = ProductionLocalHostConfiguration.Load(
    builder.Configuration,
    builder.Environment,
    args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "QLHV_APP_RealtimeWorker";
});
builder.Services.AddApplication();
builder.Services.AddInfrastructureCore(
    builder.Configuration,
    builder.Environment.ContentRootPath);
builder.Services.AddCsdtRealtimeWorkerServices(builder.Configuration);
builder.Services.AddRt03ProductionRealtimeWorkerServices(builder.Configuration);
builder.Services.AddRt03FullConvergenceRecoveryServices();

using var host = builder.Build();
if (args.Any(value => string.Equals(
        value,
        "--teacher-vehicle-projection-bootstrap",
        StringComparison.Ordinal)))
{
    try
    {
        var request = ParseTeacherVehicleBootstrapRequest(args);
        await using var scope = host.Services.CreateAsyncScope();
        var bootstrap = scope.ServiceProvider
            .GetRequiredService<ITeacherVehicleProjectionCoordinator>();
        var result = await bootstrap.BootstrapAsync(request);
        Console.WriteLine(JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions { WriteIndented = true }));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"TEACHER_VEHICLE_PROJECTION_BOOTSTRAP_FAILED: {ex.Message}");
        Environment.ExitCode = 4;
    }

    return;
}

var v9CommitRereview = args.Any(value => string.Equals(
    value,
    "--rt03-v9-reviewed-retained-rereview",
    StringComparison.Ordinal));
var v9PreflightRereview = args.Any(value => string.Equals(
    value,
    "--rt03-v9-reviewed-retained-preflight",
    StringComparison.Ordinal));
if (v9CommitRereview || v9PreflightRereview)
{
    try
    {
        if (v9CommitRereview == v9PreflightRereview)
        {
            throw new ArgumentException(
                "Select exactly one V9 preflight or commit re-review mode.");
        }
        var request = ParseRereviewRequest(args, v9CommitRereview);
        await using var scope = host.Services.CreateAsyncScope();
        var rereview = scope.ServiceProvider
            .GetRequiredService<IRt03ReviewedRetainedRereviewService>();
        var result = await rereview.ExecuteAsync(request);
        Console.WriteLine(JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions { WriteIndented = true }));
    }
    catch (Rt03SafetyException ex)
    {
        Console.Error.WriteLine($"{ex.Code}: {ex.Message}");
        Environment.ExitCode = 2;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"RT03_V9_REREVIEW_FAILED: {ex.GetType().Name}");
        Environment.ExitCode = 3;
    }

    return;
}

if (args.Any(value => string.Equals(
        value,
        "--rt03-v5-full-convergence-recovery",
        StringComparison.Ordinal)))
{
    try
    {
        var request = ParseRecoveryRequest(args);
        await using var scope = host.Services.CreateAsyncScope();
        var recovery = scope.ServiceProvider
            .GetRequiredService<IRt03FullConvergenceRecoveryService>();
        var result = await recovery.ExecuteAsync(request);
        Console.WriteLine(JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions { WriteIndented = true }));
    }
    catch (Rt03SafetyException ex)
    {
        Console.Error.WriteLine($"{ex.Code}: {ex.Message}");
        Environment.ExitCode = 2;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"RT03_V5_RECOVERY_FAILED: {ex.GetType().Name}");
        Environment.ExitCode = 3;
    }

    return;
}

await host.RunAsync();

static TeacherVehicleProjectionBootstrapRequest ParseTeacherVehicleBootstrapRequest(
    string[] args)
{
    var values = args
        .Where(value => value.StartsWith("--", StringComparison.Ordinal) &&
                        value.Contains('='))
        .Select(value => value[2..].Split('=', 2))
        .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
    if (!values.TryGetValue("profile", out var profile) ||
        !values.TryGetValue("bootstrap-id", out var bootstrapIdText) ||
        !values.TryGetValue("artifact-sha256", out var artifactSha256) ||
        !Guid.TryParse(bootstrapIdText, out var bootstrapId))
    {
        throw new ArgumentException(
            "Teacher/vehicle bootstrap requires profile, bootstrap-id and artifact-sha256.");
    }

    return new(bootstrapId, profile, artifactSha256);
}

static Rt03ReviewedRetainedRereviewRequest ParseRereviewRequest(
    string[] args,
    bool commit)
{
    var values = args
        .Where(value => value.StartsWith("--", StringComparison.Ordinal) &&
                        value.Contains('='))
        .Select(value => value[2..].Split('=', 2))
        .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
    if (!values.TryGetValue("profile", out var profile) ||
        !values.TryGetValue("review-versions", out var versionsText))
    {
        throw new ArgumentException(
            "RT03 V9 re-review requires profile and review-versions.");
    }

    var versions = versionsText
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => long.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture))
        .ToArray();
    return new(profile, versions, commit);
}

static Rt03FullConvergenceRecoveryRequest ParseRecoveryRequest(string[] args)
{
    var values = args
        .Where(value => value.StartsWith("--", StringComparison.Ordinal) &&
                        value.Contains('='))
        .Select(value => value[2..].Split('=', 2))
        .ToDictionary(
            pair => pair[0],
            pair => pair[1],
            StringComparer.Ordinal);
    if (!values.TryGetValue("profile", out var profile) ||
        !values.TryGetValue("recovery-id", out var recoveryIdText) ||
        !values.TryGetValue("expected-checkpoint", out var checkpointText) ||
        !values.TryGetValue("artifact-sha256", out var artifactSha256) ||
        !Guid.TryParse(recoveryIdText, out var recoveryId) ||
        !long.TryParse(
            checkpointText,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var checkpoint))
    {
        throw new ArgumentException(
            "RT03 V5 recovery requires profile, recovery-id, " +
            "expected-checkpoint and artifact-sha256.");
    }

    return new(
        recoveryId,
        profile,
        checkpoint,
        artifactSha256);
}

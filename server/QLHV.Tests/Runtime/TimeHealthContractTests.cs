using System.Text.Json;
using QLHV.Application.Runtime;

namespace QLHV.Tests.Runtime;

public sealed class TimeHealthContractTests
{
    [Fact]
    public void Contract_2_0_accepts_fresh_available_database_clock()
    {
        var now = DateTimeOffset.UtcNow;
        var result = Validate(Contract(now, available: true), now);

        Assert.True(result.IsHealthy);
        Assert.Equal(TimeHealthPreflightClassifications.TimeHealthy,
            result.Classification);
    }

    [Fact]
    public void Strict_mode_rejects_missing_database_clock()
    {
        var now = DateTimeOffset.UtcNow;
        var result = Validate(Contract(now, available: false), now);

        Assert.Equal(TimeHealthPreflightExitCode.TimeHealthBlocked,
            result.ExitCode);
    }

    [Fact]
    public void Strict_mode_ignores_w32time_warning_fields()
    {
        var now = DateTimeOffset.UtcNow;
        var contract = Contract(now, available: true);
        var time = Assert.IsType<Dictionary<string, object?>>(contract["time"]);
        time["windowsTimeServiceState"] = "NotRunningOrUnavailable";
        time["lastSyncError"] = 2;
        time["currentSource"] = "Pending";

        Assert.True(Validate(contract, now).IsHealthy);
    }

    [Fact]
    public void Contract_version_mismatch_fails_closed()
    {
        var now = DateTimeOffset.UtcNow;
        var contract = Contract(now, available: true);
        contract["timeContractVersion"] = "1.1";

        Assert.Equal(TimeHealthPreflightExitCode.ContractVersionMismatch,
            Validate(contract, now).ExitCode);
    }

    [Fact]
    public void Stale_database_probe_fails_strict_preflight()
    {
        var now = DateTimeOffset.UtcNow;
        var result = Validate(Contract(now.AddMinutes(-2), true), now);

        Assert.Equal(TimeHealthPreflightExitCode.ContractStale, result.ExitCode);
    }

    [Theory]
    [InlineData(404, TimeHealthPreflightExitCode.HttpNotFound)]
    [InlineData(401, TimeHealthPreflightExitCode.Unauthorized)]
    [InlineData(500, TimeHealthPreflightExitCode.ApiUnavailable)]
    public void Api_failure_codes_remain_deterministic(
        int status,
        TimeHealthPreflightExitCode expected)
    {
        Assert.Equal(expected,
            TimeHealthContractValidator.FromApiFailure(status).ExitCode);
    }

    private static TimeHealthContractValidationResult Validate(
        Dictionary<string, object?> contract,
        DateTimeOffset now) =>
        TimeHealthContractValidator.ValidateRuntimeStatusJson(
            JsonSerializer.Serialize(contract, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }),
            now,
            TimeSpan.FromSeconds(30));

    private static Dictionary<string, object?> Contract(
        DateTimeOffset evaluatedAt,
        bool available) => new()
        {
            ["timeContractVersion"] = TimeHealthContract.Version,
            ["time"] = new Dictionary<string, object?>
            {
                ["health"] = available ? "HEALTHY" : "BLOCKED",
                ["reasonCode"] = available ? "NONE" : "DATABASE_UTC_UNAVAILABLE",
                ["writesAllowed"] = available,
                ["databaseClockAvailable"] = available,
                ["databaseUtcNow"] = available ? evaluatedAt : null,
                ["evaluatedAtUtc"] = evaluatedAt,
                ["windowsTimeServiceState"] = "Running",
                ["lastSyncError"] = 0,
                ["currentSource"] = TimeHealthContract.ApprovedPeer,
            },
        };
}

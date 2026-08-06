using QLHV.Application.Runtime;

namespace QLHV.Tests.Runtime;

public sealed class TimeAuthorityTests
{
    [Fact]
    public void Sql_clock_success_is_the_only_positive_write_gate()
    {
        var health = TimeAuthorityPolicy.Evaluate(Observation(DateTimeOffset.UtcNow));

        Assert.True(health.DatabaseClockAvailable);
        Assert.True(health.WritesAllowed);
        Assert.True(TimeAuthorityPolicy.IsMutationAllowed(health));
        Assert.Equal(TimeHealthStatuses.Healthy, health.TimeHealth);
        Assert.Equal(TimeHealthReasonCodes.None, health.ReasonCode);
    }

    [Fact]
    public void Sql_clock_failure_blocks_writes()
    {
        var health = TimeAuthorityPolicy.Evaluate(Observation(null));

        Assert.False(health.DatabaseClockAvailable);
        Assert.False(health.WritesAllowed);
        Assert.False(TimeAuthorityPolicy.IsMutationAllowed(health));
        Assert.Equal(TimeHealthReasonCodes.DatabaseUtcUnavailable, health.ReasonCode);
    }

    [Theory]
    [InlineData(false, 2, "Pending")]
    [InlineData(false, null, null)]
    [InlineData(true, 5, "time.windows.com,0x9")]
    public void W32time_diagnostics_never_block_available_sql_clock(
        bool running,
        int? error,
        string? source)
    {
        var health = TimeAuthorityPolicy.Evaluate(
            Observation(DateTimeOffset.UtcNow) with
            {
                WindowsTimeRunning = running,
                LastSyncError = error,
                CurrentSource = source,
            });

        Assert.True(TimeAuthorityPolicy.IsMutationAllowed(health));
        Assert.Equal(TimeHealthStatuses.Healthy, health.TimeHealth);
    }

    [Fact]
    public void Api_sql_skew_is_diagnostic_not_authorization()
    {
        var now = DateTimeOffset.UtcNow;
        var health = TimeAuthorityPolicy.Evaluate(Observation(now.AddHours(-12)));

        Assert.True(Math.Abs(health.ClockSkewMilliseconds!.Value) > 30_000);
        Assert.True(TimeAuthorityPolicy.IsMutationAllowed(health));
    }

    [Fact]
    public void Client_clock_is_absent_from_the_authorization_model()
    {
        Assert.DoesNotContain(
            typeof(TimeAuthorityObservation).GetProperties(),
            property => property.Name.Contains("Client", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(TimeHealthDto).GetProperties(),
            property => property.Name.Contains("Client", StringComparison.Ordinal));
    }

    [Fact]
    public void Health_label_cannot_override_missing_sql_evidence()
    {
        var forged = new TimeHealthDto
        {
            TimeHealth = TimeHealthStatuses.Healthy,
            ReasonCode = TimeHealthReasonCodes.None,
            WritesAllowed = true,
            DatabaseClockAvailable = false,
            DatabaseUtcNow = null,
        };

        Assert.False(TimeAuthorityPolicy.IsMutationAllowed(forged));
    }

    private static TimeAuthorityObservation Observation(DateTimeOffset? databaseUtc)
    {
        var now = DateTimeOffset.UtcNow;
        return new TimeAuthorityObservation(
            now,
            now.AddMilliseconds(2),
            databaseUtc,
            TimeSpan.FromMilliseconds(2),
            "SE Asia Standard Time",
            true,
            TimeHealthContract.ApprovedPeer,
            TimeHealthContract.ApprovedPeer,
            TimeSpan.FromMinutes(5),
            100,
            0)
        {
            EffectivePollInterval = TimeSpan.FromMinutes(15),
        };
    }
}

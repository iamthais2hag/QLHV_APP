namespace QLHV.Tests.Sync;

public sealed class SqlIntegrationSafetyTests
{
    [Fact(Skip = "SQL integration tests are disabled by default. Set QLHV_RUN_SQL_INTEGRATION_TESTS=true and use disposable local test databases only.")]
    public void Sql_integration_tests_are_opt_in_only()
    {
        Assert.Equal("true", Environment.GetEnvironmentVariable("QLHV_RUN_SQL_INTEGRATION_TESTS"));
    }
}

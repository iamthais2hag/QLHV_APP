namespace QLHV.Tests.Sync;

public sealed class SqlIntegrationFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "QLHV_RUN_SQL_INTEGRATION_TESTS";

    public SqlIntegrationFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(EnvironmentVariable),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"SQL integration tests are disabled. Set {EnvironmentVariable}=true and use disposable local test databases only.";
        }
    }
}

public sealed class SqlIntegrationSafetyTests
{
    [SqlIntegrationFact]
    public void Sql_integration_tests_are_opt_in_only()
    {
        Assert.Equal(
            "true",
            Environment.GetEnvironmentVariable(SqlIntegrationFactAttribute.EnvironmentVariable),
            StringComparer.OrdinalIgnoreCase);
    }
}

using Xunit;

namespace QLHV.Tests.Sync;

public sealed class SqlIntegrationSafetyTests
{
    [SqlIntegrationFact]
    public void Sql_integration_tests_are_opt_in_only()
    {
        Assert.Equal(
            "true",
            Environment.GetEnvironmentVariable("QLHV_RUN_SQL_INTEGRATION_TESTS"),
            StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class SqlIntegrationFactAttribute : FactAttribute
{
    public SqlIntegrationFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("QLHV_RUN_SQL_INTEGRATION_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = "SQL integration tests are skipped by default. Set QLHV_RUN_SQL_INTEGRATION_TESTS=true to opt in locally.";
        }
    }
}

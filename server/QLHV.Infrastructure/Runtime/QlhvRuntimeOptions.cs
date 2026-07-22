namespace QLHV.Infrastructure.Runtime;

public sealed class QlhvRuntimeOptions
{
    public const string SectionName = "QlhvRuntime";

    public string Root { get; set; } = @"D:\QLHV_APP_RUNTIME";

    public int ReadinessTimeoutSeconds { get; set; } = 10;

    public int ReadinessOverallTimeoutSeconds { get; set; } = 25;

    public int ReadinessCacheSeconds { get; set; } = 5;
}

using System.Runtime.CompilerServices;

namespace QLHV.Tests.Runtime;

public sealed class TimeHealthFrontendContractTests
{
    [Fact]
    public void Frontend_requires_contract_2_and_sql_clock_authorization()
    {
        var api = Read("client/src/features/runtime-status/api.ts");
        var types = Read("client/src/features/runtime-status/types.ts");

        Assert.Contains("value.timeContractVersion !== '2.0'", api,
            StringComparison.Ordinal);
        Assert.Contains("databaseClockAvailable", api, StringComparison.Ordinal);
        Assert.Contains("time.databaseClockAvailable", types, StringComparison.Ordinal);
        Assert.DoesNotContain("TRANSIENT_W32TIME_DIAGNOSTIC", types,
            StringComparison.Ordinal);
        Assert.DoesNotContain("durable", api, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Status_page_labels_w32time_as_diagnostic()
    {
        var page = Read("client/src/features/runtime-status/RuntimeStatusPage.tsx");
        Assert.Contains("W32Time/NTP chỉ dùng để chẩn đoán", page,
            StringComparison.Ordinal);
        Assert.Contains("SQL clock sẵn sàng", page, StringComparison.Ordinal);
        Assert.DoesNotContain("TRANSIENT_W32TIME_DIAGNOSTIC", page,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Client_cannot_submit_audit_timestamp()
    {
        var runtimeApi = Read("client/src/features/runtime-status/api.ts");
        Assert.DoesNotContain("clientUtc", runtimeApi,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auditAtUtc", runtimeApi,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(
        string relativePath,
        [CallerFilePath] string sourcePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourcePath)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}

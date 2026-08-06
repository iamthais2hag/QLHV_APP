using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync.QlhvDirectRealtime;

namespace QLHV.Tests.Sync.Rt02;

public sealed class Rt02SchemaSetOptionsHotfixTests
{
    private const string SchemaHotfixFile =
        "20260727_rt02b2_schema_set_options_hotfix.sql";

    private const string ProtectedConfigSha256 =
        "12FF324C8A3F844360DF8A68AEAE1556C9F12A49249220811541586086A21E5E";

    private static readonly string CanonicalSetPreamble = string.Join(
        "\n",
        "SET ANSI_NULLS ON;",
        "SET ANSI_PADDING ON;",
        "SET ANSI_WARNINGS ON;",
        "SET ARITHABORT ON;",
        "SET CONCAT_NULL_YIELDS_NULL ON;",
        "SET QUOTED_IDENTIFIER ON;",
        "SET NUMERIC_ROUNDABORT OFF;");

    public static IEnumerable<object[]> RequiredSessionOptions()
    {
        yield return ["ANSI_NULLS", 1];
        yield return ["ANSI_PADDING", 1];
        yield return ["ANSI_WARNINGS", 1];
        yield return ["ARITHABORT", 1];
        yield return ["CONCAT_NULL_YIELDS_NULL", 1];
        yield return ["QUOTED_IDENTIFIER", 1];
        yield return ["NUMERIC_ROUNDABORT", 0];
    }

    [Fact]
    public void Every_filtered_index_batch_has_the_exact_canonical_SET_preamble()
    {
        var batches = SplitBatches(ReadSchemaHotfix());
        var filteredIndexBatchIndexes = Enumerable
            .Range(0, batches.Length)
            .Where(index => IsFilteredIndexBatch(batches[index]))
            .ToArray();

        var indexBatch = Assert.Single(filteredIndexBatchIndexes);
        Assert.True(indexBatch > 0);
        Assert.EndsWith(
            CanonicalSetPreamble,
            batches[indexBatch - 1].TrimEnd(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void QUOTED_IDENTIFIER_is_resolved_ON_before_the_filtered_index()
    {
        var (preambleBatch, indexBatch) = ReadFilteredIndexBatches();

        Assert.Contains(
            "SET QUOTED_IDENTIFIER ON;",
            preambleBatch,
            StringComparison.Ordinal);
        Assert.Matches(
            NullSafeSessionPropertyPattern("QUOTED_IDENTIFIER", 1),
            PrefixBeforeCreateIndex(indexBatch));
    }

    [Fact]
    public void NUMERIC_ROUNDABORT_is_resolved_OFF_before_the_filtered_index()
    {
        var (preambleBatch, indexBatch) = ReadFilteredIndexBatches();

        Assert.Contains(
            "SET NUMERIC_ROUNDABORT OFF;",
            preambleBatch,
            StringComparison.Ordinal);
        Assert.Matches(
            NullSafeSessionPropertyPattern("NUMERIC_ROUNDABORT", 0),
            PrefixBeforeCreateIndex(indexBatch));
    }

    [Theory]
    [MemberData(nameof(RequiredSessionOptions))]
    public void Filtered_index_preflight_checks_every_SET_option_null_safely(
        string option,
        int expectedValue)
    {
        var (_, indexBatch) = ReadFilteredIndexBatches();

        Assert.Matches(
            NullSafeSessionPropertyPattern(option, expectedValue),
            PrefixBeforeCreateIndex(indexBatch));
    }

    [Fact]
    public void SET_option_preflight_throws_before_literal_filtered_index_DDL()
    {
        var (_, indexBatch) = ReadFilteredIndexBatches();
        var createIndexAt = indexBatch.IndexOf(
            "CREATE UNIQUE INDEX UX_Rt02Learner_ActiveIdentity",
            StringComparison.Ordinal);
        var prefix = PrefixBeforeCreateIndex(indexBatch);
        var guardAt = prefix.IndexOf("IF ISNULL(", StringComparison.Ordinal);
        Assert.True(createIndexAt >= 0);
        Assert.True(guardAt >= 0);

        var throwAt = prefix.IndexOf("THROW ", guardAt, StringComparison.Ordinal);
        Assert.True(throwAt >= 0);

        var throwEnd = prefix.IndexOf(';', throwAt);
        Assert.True(throwEnd > throwAt);

        var guardBlock = prefix[guardAt..(throwEnd + 1)];
        Assert.Matches(
            new Regex(
                @"\bIF\b[\s\S]*\bSESSIONPROPERTY\b[\s\S]*" +
                @"\bTHROW\s+5\d{4,5}\s*,\s*" +
                @"'RT02 filtered-index canonical SET options are unresolved\.'\s*,\s*1\s*;",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            guardBlock);
        Assert.Equal(7, Count(guardBlock, @"\bSESSIONPROPERTY\s*\("));
        Assert.Equal(6, Count(guardBlock, @"\bOR\b"));
        Assert.DoesNotMatch(
            new Regex(
                @"\bEXEC(?:UTE)?\b|\bsp_executesql\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            indexBatch);
    }

    [Fact]
    public void Schema_hotfix_is_resumable_per_table_and_has_no_cleanup_or_fixture_DML()
    {
        var script = ReadSchemaHotfix();
        var expectedCreateCounts = new Dictionary<string, int>(
            StringComparer.Ordinal)
        {
            ["NguoiLX"] = 2,
            ["NguoiLX_HoSo"] = 2,
            ["Rt02Learner"] = 1,
            ["Rt02ManualReviewEvidence"] = 1,
            ["Rt02ApplyMarker"] = 1,
            ["Rt02ApplyCheckpoint"] = 1,
            ["Rt02EnvironmentState"] = 1,
        };

        foreach (var (table, expectedCount) in expectedCreateCounts)
        {
            var guardedCreate = new Regex(
                $@"IF\s+OBJECT_ID\s*\(\s*N'dbo\.{Regex.Escape(table)}'\s*,\s*N'U'\s*\)" +
                $@"\s+IS\s+NULL\s+BEGIN\s+CREATE\s+TABLE\s+dbo\.{Regex.Escape(table)}\b",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant |
                RegexOptions.Singleline);
            Assert.Equal(expectedCount, guardedCreate.Matches(script).Count);
        }

        Assert.DoesNotContain(
            "test schema already exists",
            script,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            new Regex(
                @"\bDROP\b|\bTRUNCATE\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            script);
        Assert.DoesNotMatch(
            new Regex(
                @"(?m)^\s*(?:INSERT|UPDATE|DELETE|MERGE)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            script);
        Assert.Contains(
            "schema shape is incompatible",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "filtered-index allowlist verification failed",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_hotfix_cannot_enable_CT_Snapshot_or_RCSI()
    {
        var script = ReadSchemaHotfix();
        var forbidden = new[]
        {
            @"\bALTER\s+DATABASE\b",
            @"\bENABLE\s+CHANGE_TRACKING\b",
            @"\bSET\s+CHANGE_TRACKING\s*=\s*ON\b",
            @"\bALLOW_SNAPSHOT_ISOLATION\s+ON\b",
            @"\bREAD_COMMITTED_SNAPSHOT\s+ON\b",
            @"\bSET\s+TRANSACTION\s+ISOLATION\s+LEVEL\s+SNAPSHOT\b",
            @"\bCHANGETABLE\s*\(",
        };

        foreach (var pattern in forbidden)
        {
            Assert.DoesNotMatch(
                new Regex(
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                script);
        }
    }

    [Fact]
    public void Isolated_SQL_route_rejects_every_named_production_database()
    {
        foreach (var productionDatabase in
                 QlhvDirectRealtimeIsolatedEnvironmentValidator.ProductionDatabaseNames)
        {
            var error = Assert.Throws<QlhvDirectRealtimeSafetyException>(
                () => Rt02b2SqlRoute.ConnectionString(productionDatabase));

            Assert.Equal(
                QlhvDirectRealtimeErrors.IsolatedDatabaseIdentityRejected,
                error.Code);
        }
    }

    [Theory]
    [InlineData(Rt02b2SqlRoute.OtoDatabase)]
    [InlineData(Rt02b2SqlRoute.MotoDatabase)]
    [InlineData(Rt02b2SqlRoute.TargetDatabase)]
    public void Isolated_SQL_route_is_exact_Shared_Memory_without_credentials(
        string database)
    {
        var route = new SqlConnectionStringBuilder(
            Rt02b2SqlRoute.ConnectionString(database));

        Assert.Equal(Rt02b2SqlRoute.SharedMemoryServer, route.DataSource);
        Assert.Equal(database, route.InitialCatalog);
        Assert.True(route.IntegratedSecurity);
        Assert.False(route.Pooling);
        Assert.Equal(string.Empty, route.UserID);
        Assert.Equal(string.Empty, route.Password);
        Assert.Equal(Rt02b2SqlRoute.ApplicationName, route.ApplicationName);
    }

    [Fact]
    public void Production_configs_have_no_isolated_route_and_protected_hashes_match()
    {
        var productionConfigs = new[]
        {
            WorkspacePath("server", "QLHV.Api", "appsettings.json"),
            WorkspacePath("server", "QLHV.Api", "appsettings.Development.json"),
            WorkspacePath("server", "QLHV.Worker", "appsettings.json"),
            WorkspacePath("server", "QLHV.Worker", "appsettings.Development.json"),
        };

        foreach (var path in productionConfigs)
        {
            var contents = File.ReadAllText(path);
            Assert.DoesNotContain("QLHVRT02", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("QLHV_RT02_", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                Rt02b2SqlRoute.EnvironmentId,
                contents,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                Rt02b2SqlRoute.ApprovalId,
                contents,
                StringComparison.Ordinal);
        }

        Assert.Equal(
            ProtectedConfigSha256,
            Sha256(WorkspacePath(
                "server",
                "QLHV.Api",
                "appsettings.Development.json")));
        Assert.Equal(
            ProtectedConfigSha256,
            Sha256(WorkspacePath(
                "server",
                "QLHV.Worker",
                "appsettings.Development.json")));
    }

    private static (string PreambleBatch, string IndexBatch)
        ReadFilteredIndexBatches()
    {
        var batches = SplitBatches(ReadSchemaHotfix());
        var index = Assert.Single(
            Enumerable
                .Range(0, batches.Length)
                .Where(batchIndex => IsFilteredIndexBatch(batches[batchIndex])));
        Assert.True(index > 0);
        return (batches[index - 1], batches[index]);
    }

    private static bool IsFilteredIndexBatch(string batch)
        => Regex.IsMatch(
            batch,
            @"\bCREATE\s+UNIQUE\s+INDEX\s+UX_Rt02Learner_ActiveIdentity\b" +
            @"[\s\S]*?\bWHERE\s+Active\s*=\s*1\s+AND\s+SoftDeleted\s*=\s*0\s*;",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string PrefixBeforeCreateIndex(string indexBatch)
    {
        const string statement =
            "CREATE UNIQUE INDEX UX_Rt02Learner_ActiveIdentity";
        var createIndexAt = indexBatch.IndexOf(statement, StringComparison.Ordinal);
        Assert.True(createIndexAt >= 0);
        return indexBatch[..createIndexAt];
    }

    private static Regex NullSafeSessionPropertyPattern(
        string option,
        int expectedValue)
        => new(
            $@"ISNULL\s*\(\s*CONVERT\s*\(\s*int\s*,\s*" +
            $@"SESSIONPROPERTY\s*\(\s*N'{Regex.Escape(option)}'\s*\)\s*\)" +
            $@"\s*,\s*-1\s*\)\s*<>\s*{expectedValue}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static int Count(string source, string pattern)
        => Regex.Matches(
            source,
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;

    private static string[] SplitBatches(string script)
        => Regex.Split(
            script,
            @"(?m)^[ \t]*GO[ \t]*(?:\n|$)",
            RegexOptions.CultureInvariant);

    private static string ReadSchemaHotfix()
        => Normalize(File.ReadAllText(WorkspacePath(
            "database",
            "proofs",
            SchemaHotfixFile)));

    private static string Normalize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Sha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string WorkspacePath(
        string first,
        string second,
        string third,
        [CallerFilePath] string testFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(testFile)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                first,
                second,
                third);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Cannot locate RT02 schema hotfix test artifact.",
            Path.Combine(first, second, third));
    }
}

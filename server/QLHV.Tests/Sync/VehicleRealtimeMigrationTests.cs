using System.Text.RegularExpressions;
using QLHV.Infrastructure.Sync.VehicleRealtime;

namespace QLHV.Tests.Sync;

public sealed class VehicleRealtimeMigrationTests
{
    [Fact]
    public void Target_migration_is_exact_no_backfill_and_profile_safe()
    {
        var sql = ReadPatch("20260730_add_vehicle_realtime_mapping.sql");

        Assert.StartsWith("/*", sql, StringComparison.Ordinal);
        Assert.Contains("USE [QLHV_APP];", sql, StringComparison.Ordinal);
        Assert.Contains(
            "9C44B304-8A84-4D0D-9A82-19C7233FF6BB",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("SourceProfileCode nvarchar(16)", sql, StringComparison.Ordinal);
        Assert.Contains("SourceBienSoXe nvarchar(20)", sql, StringComparison.Ordinal);
        Assert.Contains("SourceRowHash char(64)", sql, StringComparison.Ordinal);
        Assert.Contains(
            "UNIQUE INDEX UX_App_XeTap_SourceIdentity",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ON dbo.App_XeTap(SourceProfileCode,SourceBienSoXe)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "App_XeTap_RealtimeManualReview",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "App_XeTap_RealtimeCheckpoint",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("ON DELETE NO ACTION", sql, StringComparison.Ordinal);
        Assert.Contains("DENY DELETE ON dbo.App_XeTap", sql, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(
                @"(?im)^\s*INSERT\s+INTO\s+dbo\.App_XeTap\b",
                RegexOptions.CultureInvariant),
            sql);
        Assert.DoesNotMatch(
            new Regex(
                @"(?im)^\s*INSERT\s+INTO\s+dbo\.App_XeTap_RealtimeCheckpoint\b",
                RegexOptions.CultureInvariant),
            sql);
    }

    [Theory]
    [InlineData(
        "20260730_enable_oto_vehicle_change_tracking.sql",
        "CSDL_OTO",
        "9A8B9BC1-18F3-4823-8123-3DC197A9D540")]
    [InlineData(
        "20260730_enable_moto_vehicle_change_tracking.sql",
        "CSDL_MOTO",
        "308BDDA8-80F3-4ACB-9836-578D80A9E98E")]
    public void Source_ct_migration_is_exact_table_only_and_has_no_checkpoint(
        string file,
        string database,
        string databaseGuid)
    {
        var sql = ReadPatch(file);

        Assert.Contains($"USE [{database}];", sql, StringComparison.Ordinal);
        Assert.Contains(databaseGuid, sql, StringComparison.Ordinal);
        Assert.Single(
            Regex.Matches(
                sql,
                @"ALTER TABLE dbo\.XeTap ENABLE CHANGE_TRACKING",
                RegexOptions.CultureInvariant).Cast<Match>());
        Assert.Contains(
            "TRACK_COLUMNS_UPDATED=ON",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("App_HocVien", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "App_XeTap_RealtimeCheckpoint",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("READ_COMMITTED_SNAPSHOT ON", sql,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Source_reader_uses_one_complete_ct_version_and_key_only_revalidation()
    {
        Assert.Contains(
            "MIN(SourceCtVersion)",
            SqlVehicleRealtimeSourceFeed.ReadNextChangeVersionSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "changeRow.SYS_CHANGE_VERSION<=@SealedCurrentVersion",
            SqlVehicleRealtimeSourceFeed.ReadNextChangeVersionSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "changeRow.BienSoXe IN @SourceBienSoXe",
            SqlVehicleRealtimeSourceFeed.RevalidateKeysSql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "NguoiLX",
            SqlVehicleRealtimeSourceFeed.RevalidateKeysSql,
            StringComparison.Ordinal);
    }

    private static string ReadPatch(string name)
        => File.ReadAllText(Path.Combine(
            Root(),
            "database",
            "patches",
            name));

    private static string Root()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "server", "QLHV.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}

namespace QLHV.Tests.Sync;

public sealed class SchemaPatchSafetyTests
{
    [Fact]
    public void MaHangDT_patch_is_idempotent_and_adds_filtered_index()
    {
        var patchPath = Path.Combine(
            FindRepositoryRoot(),
            "database",
            "patches",
            "20260610_add_mahangdt_app_hocvien.sql");

        var sql = File.ReadAllText(patchPath);

        Assert.Contains("IF COL_LENGTH('dbo.App_HocVien', 'MaHangDT') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE dbo.App_HocVien", sql, StringComparison.Ordinal);
        Assert.Contains("ADD MaHangDT NVARCHAR(20) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("IX_App_HocVien_MaHangDT", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE IsDeleted = 0", sql, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "database")) &&
                Directory.Exists(Path.Combine(directory.FullName, "server")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot find QLHV_APP repository root.");
    }
}

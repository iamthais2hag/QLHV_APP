using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace QLHV.Tests.Auth;

public sealed class AppUserAuthorizationPatchTests
{
    [Fact]
    public void Patch_is_transactional_idempotent_and_targets_only_qlhv_app_auth_schema()
    {
        var patch = ReadPatch();

        Assert.Contains("USE [QLHV_APP];", patch, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("COMMIT TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'dbo.App_User', N'U') IS NULL", patch, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'dbo.App_Role', N'U') IS NULL", patch, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'dbo.App_UserRole', N'U') IS NULL", patch, StringComparison.Ordinal);
        Assert.Contains("IF NOT EXISTS", patch, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("DROP TABLE", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BACKUP DATABASE", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RESTORE DATABASE", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE LOGIN", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER LOGIN", patch, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Patch_defines_password_hash_and_admin_viewer_roles_without_seeding_credentials()
    {
        var patch = ReadPatch();

        foreach (var requiredColumn in new[]
                 {
                     "UserId",
                     "UserName",
                     "DisplayName",
                     "PasswordHash",
                     "IsActive",
                     "CreatedAt",
                     "UpdatedAt",
                 })
        {
            Assert.Contains(requiredColumn, patch, StringComparison.Ordinal);
        }

        Assert.Contains("RoleCode", patch, StringComparison.Ordinal);
        Assert.Contains("N'Admin'", patch, StringComparison.Ordinal);
        Assert.Contains("N'Viewer'", patch, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(@"INSERT\s+INTO\s+dbo\.App_User\b", RegexOptions.IgnoreCase),
            patch);
        Assert.DoesNotContain("QLHV_SEED_ADMIN_PASSWORD", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("X-QLHV-Operations-Key", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("QlhvOperations__AdminKey", patch, StringComparison.Ordinal);
    }

    private static string ReadPatch()
        => File.ReadAllText(FindWorkspaceFile(
            "database",
            "patches",
            "20260722_add_app_user_authorization.sql"));

    private static string FindWorkspaceFile(
        string firstPathPart,
        params string[] remainingPathParts)
        => FindWorkspaceFileFromCaller(
            new[] { firstPathPart }.Concat(remainingPathParts).ToArray());

    private static string FindWorkspaceFileFromCaller(
        string[] pathParts,
        [CallerFilePath] string testFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(testFile)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot locate workspace file.", Path.Combine(pathParts));
    }
}

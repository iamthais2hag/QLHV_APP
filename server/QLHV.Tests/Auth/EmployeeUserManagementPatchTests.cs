using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace QLHV.Tests.Auth;

public sealed class EmployeeUserManagementPatchTests
{
    [Fact]
    public void Patch_is_transactional_idempotent_and_does_not_seed_credentials()
    {
        var patch = ReadPatch();

        Assert.Contains("USE [QLHV_APP];", patch, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", patch, StringComparison.Ordinal);
        Assert.Contains("SET QUOTED_IDENTIFIER ON;", patch, StringComparison.Ordinal);
        Assert.Contains("SET ARITHABORT ON;", patch, StringComparison.Ordinal);
        Assert.Contains("SET NUMERIC_ROUNDABORT OFF;", patch, StringComparison.Ordinal);
        Assert.Contains("IF @@TRANCOUNT <> 0", patch, StringComparison.Ordinal);
        Assert.Contains("IMPLICIT_TRANSACTIONS OFF", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("COMMIT TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH(N'dbo.App_User', N'MustChangePassword')", patch, StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH(N'dbo.App_User', N'LastFailedLoginAt')", patch, StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH(N'dbo.App_User', N'SecurityStamp')", patch, StringComparison.Ordinal);
        Assert.Contains("NormalizedUserName AS", patch, StringComparison.Ordinal);
        Assert.Contains("EXEC sys.sp_executesql", patch, StringComparison.Ordinal);
        Assert.Contains("PERSISTED", patch, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UX_App_User_NormalizedUserName", patch, StringComparison.Ordinal);
        Assert.Contains("indexRow.has_filter = 0", patch, StringComparison.Ordinal);
        Assert.Contains("indexRow.is_hypothetical = 0", patch, StringComparison.Ordinal);
        Assert.Contains("HAVING COUNT_BIG(*) > 1", patch, StringComparison.Ordinal);
        Assert.Contains("CK_App_User_SecurityStamp_NotEmpty", patch, StringComparison.Ordinal);
        Assert.Contains("CK_App_User_UserName_NotBlank", patch, StringComparison.Ordinal);
        Assert.Contains("dbo.App_UserManagementAudit", patch, StringComparison.Ordinal);
        Assert.Contains("CK_App_UserManagementAudit_ActionCode", patch, StringComparison.Ordinal);
        Assert.Contains("TR_App_UserManagementAudit_AppendOnly", patch, StringComparison.Ordinal);
        Assert.Contains("CK_App_Role_RoleCode_Allowed", patch, StringComparison.Ordinal);
        Assert.Contains("sys.sql_expression_dependencies", patch, StringComparison.Ordinal);
        Assert.Contains("RoleCode NOT IN (N'Admin', N'Employee', N'Viewer')", patch, StringComparison.Ordinal);
        Assert.Contains("CHECK (RoleCode IN (N'Admin', N'Employee', N'Viewer'))", patch, StringComparison.Ordinal);
        Assert.Contains("AND is_not_trusted = 0", patch, StringComparison.Ordinal);
        Assert.Contains("N'RESET_PASSWORD'", patch, StringComparison.Ordinal);
        Assert.Contains("N'CHANGE_PASSWORD'", patch, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(
                @"INSERT\s+INTO\s+dbo\.App_User(?:\s|\()",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            patch);
        Assert.DoesNotContain("PasswordHash =", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TemporaryPassword", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            new Regex(
                @"UPDATE\s+dbo\.App_Role",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            patch);
    }

    [Fact]
    public void Patch_seeds_localized_roles_and_extends_app_open_constraints()
    {
        var patch = ReadPatch();

        Assert.Contains("N'Admin'", patch, StringComparison.Ordinal);
        Assert.Contains("N'Quản trị viên'", patch, StringComparison.Ordinal);
        Assert.Contains("N'Employee'", patch, StringComparison.Ordinal);
        Assert.Contains("N'Nhân viên'", patch, StringComparison.Ordinal);
        Assert.Contains("N'Viewer'", patch, StringComparison.Ordinal);
        Assert.Contains("N'Chỉ xem'", patch, StringComparison.Ordinal);
        Assert.Contains("CK_App_QlhvAutoSyncRun_TriggerType", patch, StringComparison.Ordinal);
        Assert.Contains("APP_OPEN", patch, StringComparison.Ordinal);
        Assert.Contains("SYSTEM_APP_OPEN", patch, StringComparison.Ordinal);
        Assert.Contains(
            "COL_LENGTH(\n               N'dbo.App_QlhvSyncOperationHistory',\n               N'Actor')",
            patch,
            StringComparison.Ordinal);
        Assert.Contains("WITH CHECK CHECK CONSTRAINT", patch, StringComparison.Ordinal);
    }

    private static string ReadPatch() =>
        File.ReadAllText(FindWorkspaceFile(
            "database",
            "patches",
            "20260724_add_employee_user_management.sql"));

    private static string FindWorkspaceFile(
        string firstPathPart,
        params string[] remainingPathParts) =>
        FindWorkspaceFileFromCaller(
            new[] { firstPathPart }.Concat(remainingPathParts).ToArray());

    private static string FindWorkspaceFileFromCaller(
        string[] pathParts,
        [CallerFilePath] string testFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(testFile)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Cannot locate workspace file.",
            Path.Combine(pathParts));
    }
}

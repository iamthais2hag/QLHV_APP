using System.Runtime.CompilerServices;

namespace QLHV.Tests.Auth;

public sealed class AppUserManagementRepositorySourceTests
{
    [Fact]
    public void Repository_uses_parameterized_normalized_identity_and_atomic_admin_guards()
    {
        var source = ReadSource(
            "server",
            "QLHV.Infrastructure",
            "Auth",
            "AppUserRepository.cs");

        Assert.Contains("NormalizedUserName = @NormalizedUsername", source, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", source, StringComparison.Ordinal);
        Assert.Contains("sys.sp_getapplock", source, StringComparison.Ordinal);
        Assert.Contains("command.ActorUserId == command.UserId && !command.IsActive", source, StringComparison.Ordinal);
        Assert.Contains("CountOtherActiveAdminsSql", source, StringComparison.Ordinal);
        Assert.Contains("LastActiveAdminDenied", source, StringComparison.Ordinal);
        Assert.Contains("FailedLoginCount = CASE WHEN @IsActive = 1 THEN 0", source, StringComparison.Ordinal);
        Assert.Contains("MustChangePassword = 0", source, StringComparison.Ordinal);
        Assert.Contains("PasswordHash = @ExpectedPasswordHash", source, StringComparison.Ordinal);
        Assert.Contains("TryUpdatePasswordHashAsync", source, StringComparison.Ordinal);
        Assert.Contains("TryRecordSuccessfulLoginAsync", source, StringComparison.Ordinal);
        Assert.Contains("SecurityStamp = @ExpectedSecurityStamp", source, StringComparison.Ordinal);
        Assert.Contains("SecurityStamp = NEWID()", source, StringComparison.Ordinal);
        Assert.Contains("AppPasswordHashFormat.IsSupported", source, StringComparison.Ordinal);
        Assert.Contains("InsertUserManagementAuditSql", source, StringComparison.Ordinal);
        Assert.Contains("\"RESET_PASSWORD\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TemporaryPassword", source, StringComparison.OrdinalIgnoreCase);
        var auditStart = source.IndexOf(
            "private const string InsertUserManagementAuditSql",
            StringComparison.Ordinal);
        Assert.True(auditStart >= 0);
        var auditSql = source[auditStart..];
        Assert.DoesNotContain("PasswordHash", auditSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordSalt", auditSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TemporaryPassword", auditSql, StringComparison.OrdinalIgnoreCase);
        Assert.False(
            System.Text.RegularExpressions.Regex.IsMatch(
                source,
                @"DELETE\s+FROM\s+dbo\.App_User(?:\s|;|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant),
            "The account repository must never physically delete App_User rows.");
    }

    [Fact]
    public void Runtime_readiness_requires_a_supported_Admin_credential_hash()
    {
        var source = ReadSource(
            "server",
            "QLHV.Infrastructure",
            "Runtime",
            "SqlServerRuntimeReadinessProbe.cs");

        Assert.Contains("ActiveAdminCredentialHashesSql", source, StringComparison.Ordinal);
        Assert.Contains("AppPasswordHashFormat.IsSupported", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "NULLIF(LTRIM(RTRIM(userRow.PasswordHash))",
            source,
            StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] pathParts) =>
        File.ReadAllText(FindWorkspaceFileFromCaller(pathParts));

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

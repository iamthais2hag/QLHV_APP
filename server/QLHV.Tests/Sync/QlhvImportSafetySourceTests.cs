using System.Runtime.CompilerServices;

namespace QLHV.Tests.Sync;

public sealed class QlhvImportSafetySourceTests
{
    [Fact]
    public void Constraint_diagnostics_discovers_column_constraints_from_metadata()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "QlhvImportReadRepository.cs"));

        Assert.Contains("sys.check_constraints", source, StringComparison.Ordinal);
        Assert.Contains("sys.sql_expression_dependencies", source, StringComparison.Ordinal);
        Assert.Contains("QualifiedTableName = \"dbo.App_HocVien\"", source, StringComparison.Ordinal);
        Assert.Contains("ColumnName = \"SourceProfileCode\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("name = N'CK_App_HocVien_SourceProfileCode'", source, StringComparison.Ordinal);
        Assert.Contains("TargetHocVienWriteColumnsSql", source, StringComparison.Ordinal);
        Assert.Contains("N'CreatedBy'", source, StringComparison.Ordinal);
        Assert.Contains("CurrentAppHocVienRowsSql", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_profile_patch_is_transactional_idempotent_and_not_bound_to_old_constraint_name()
    {
        var patch = File.ReadAllText(FindWorkspaceFile(
            "database", "patches", "20260722_expand_app_hocvien_source_profile_codes.sql"));

        Assert.Contains("USE [QLHV_APP];", patch, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("COMMIT TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("sys.sql_expression_dependencies", patch, StringComparison.Ordinal);
        Assert.Contains("ConstraintDefinition", patch, StringComparison.Ordinal);
        Assert.Contains("not the recognized allow-list", patch, StringComparison.Ordinal);
        Assert.Contains("SQL Server can expose an IN allow-list", patch, StringComparison.Ordinal);
        Assert.Contains("@RemainingConstraintDefinition", patch, StringComparison.Ordinal);
        Assert.Contains("SourceProfileCode NOT IN", patch, StringComparison.Ordinal);
        Assert.Contains("N'DATA_V1'", patch, StringComparison.Ordinal);
        Assert.Contains("N'DATA_V2'", patch, StringComparison.Ordinal);
        Assert.Contains("N'CSDT_MOTO'", patch, StringComparison.Ordinal);
        Assert.Contains("N'CSDT_OTO'", patch, StringComparison.Ordinal);
        Assert.Contains("QUOTENAME(@ExistingConstraintName)", patch, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DROP CONSTRAINT [CK_App_HocVien_SourceProfileCode]",
            patch,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Import_reader_binds_logical_profiles_to_exact_bak_databases()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "QlhvImportReadRepository.cs"));

        Assert.Contains("CsdtConnectionProfileCodes.CsdtOtoBak", source, StringComparison.Ordinal);
        Assert.Contains("\"CSDL_OTO_BAK\"", source, StringComparison.Ordinal);
        Assert.Contains("CsdtConnectionProfileCodes.CsdtMotoBak", source, StringComparison.Ordinal);
        Assert.Contains("\"CSDL_MOTO_BAK\"", source, StringComparison.Ordinal);
        Assert.Contains("SELECT DB_NAME();", source, StringComparison.Ordinal);
        Assert.Contains("StringComparison.Ordinal", source, StringComparison.Ordinal);
        Assert.Contains("SourceDatabaseName = sourceDatabaseName", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Bak_profile_patch_is_transactional_idempotent_and_has_no_secret()
    {
        var patch = File.ReadAllText(FindWorkspaceFile(
            "database", "patches", "20260722_add_bak_csdt_connection_profiles.sql"));

        Assert.Contains("USE [QLHV_APP];", patch, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("CSDT_OTO_BAK", patch, StringComparison.Ordinal);
        Assert.Contains("CSDL_OTO_BAK", patch, StringComparison.Ordinal);
        Assert.Contains("CSDT_MOTO_BAK", patch, StringComparison.Ordinal);
        Assert.Contains("CSDL_MOTO_BAK", patch, StringComparison.Ordinal);
        Assert.Contains("AuthMode = N'Windows'", patch, StringComparison.Ordinal);
        Assert.Contains("PasswordCipherText = NULL", patch, StringComparison.Ordinal);
        Assert.Contains("WHERE NOT EXISTS", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("CSDT_OTO'", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("CSDT_MOTO'", patch, StringComparison.Ordinal);
    }

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

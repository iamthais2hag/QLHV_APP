using System.Text.RegularExpressions;

namespace QLHV.Tests.Security;

public sealed class ApiLeastPrivilegePrincipalTests
{
    private static readonly string Root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..",".."));
    private const string Artifact="handoff/API_LEAST_PRIVILEGE_PRINCIPAL_20260801";

    [Fact]
    public void Api_host_supports_exact_dedicated_windows_service_identity()
    {
        var program=Read("server/QLHV.Api/Program.cs");
        var project=Read("server/QLHV.Api/QLHV.Api.csproj");
        var install=Read($"{Artifact}/windows/Install-QLHV-ApiService.ps1");
        Assert.Contains("UseWindowsService",program,StringComparison.Ordinal);
        Assert.Contains("QLHV_APP_Api",program,StringComparison.Ordinal);
        Assert.Contains("Microsoft.Extensions.Hosting.WindowsServices",project,StringComparison.Ordinal);
        Assert.Contains("NT SERVICE\\QLHV_APP_Api",install,StringComparison.Ordinal);
        Assert.Contains("D:\\QLHV_APP_RUNTIME\\app\\QLHV.Api.exe",install,StringComparison.Ordinal);
        Assert.Contains("ASPNETCORE_URLS=http://0.0.0.0:8088",install,StringComparison.Ordinal);
        Assert.Contains("QlhvAutoSync__Enabled=false",install,StringComparison.Ordinal);
        Assert.DoesNotContain("LocalSystem",install,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Source_and_v1_matrix_is_select_only_and_denies_every_mutation_surface()
    {
        string[] names=["BaoCaoI","BaoCaoII","DM_DonViGTVT","DM_DVHC","DM_HangDT","DM_HangGPLX","GiaoVien","KhoaHoc","KhoaHoc_GiaoVien","KhoaHoc_XeTap","NguoiLX","NguoiLX_GPLX","NguoiLX_HoSo","NguoiLXHS_GiayTo","XeTap"];
        foreach(var file in new[]{"01_source_oto.sql","01_source_moto.sql","01_source_oto_v1.sql","01_source_moto_v1.sql"})
        {
            var sql=Read($"{Artifact}/sql/{file}");
            foreach(var name in names) Assert.Contains($"(N'{name}')",sql,StringComparison.Ordinal);
            Assert.Contains("GRANT SELECT ON OBJECT::dbo.",sql,StringComparison.Ordinal);
            Assert.Contains("DENY INSERT,UPDATE,DELETE,EXECUTE,ALTER,TAKE OWNERSHIP",sql,StringComparison.Ordinal);
            Assert.DoesNotMatch(new Regex(@"(?i)GRANT\s+(INSERT|UPDATE|DELETE|MERGE|EXECUTE)\b"),sql);
            Assert.DoesNotContain("VIEW CHANGE TRACKING",sql,StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Target_matrix_is_object_scoped_and_keeps_worker_owned_state_read_only()
    {
        var sql=Read($"{Artifact}/sql/01_provision_api_principal.sql");
        Assert.Contains("qlhv_api_runtime",sql,StringComparison.Ordinal);
        Assert.Contains("(N'App_User',N'UPDATE')",sql,StringComparison.Ordinal);
        Assert.Contains("(N'App_Rt03RealtimeControl',N'UPDATE')",sql,StringComparison.Ordinal);
        Assert.Contains("(N'App_QlhvDirectRealtimeApplyCheckpoint',N'SELECT')",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("(N'App_QlhvDirectRealtimeApplyCheckpoint',N'UPDATE')",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("(N'App_QlhvDirectRealtimeApplyMarker',N'INSERT')",sql,StringComparison.Ordinal);
        Assert.Contains("DENY ALTER,TAKE OWNERSHIP TO qlhv_api_runtime",sql,StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"(?im)^\s*GRANT\s+(CONTROL|ALTER)\b"),sql);
        Assert.DoesNotContain("db_owner] ADD MEMBER",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("db_datawriter] ADD MEMBER",sql,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verification_proves_no_sysadmin_db_owner_or_db_datawriter()
    {
        var sql=Read($"{Artifact}/sql/02_verify_api_principal.sql");
        Assert.Contains("IS_SRVROLEMEMBER(N'sysadmin',@Principal)<>0",sql,StringComparison.Ordinal);
        Assert.Contains("IS_ROLEMEMBER(N'db_owner',@Principal)<>0",sql,StringComparison.Ordinal);
        Assert.Contains("IS_ROLEMEMBER(N'db_datawriter',@Principal)<>0",sql,StringComparison.Ordinal);
        Assert.Contains("API_LEAST_PRIVILEGE_VERIFY_PASS",sql,StringComparison.Ordinal);
        foreach(var file in new[]{"02_verify_source_oto.sql","02_verify_source_moto.sql","02_verify_source_oto_v1.sql","02_verify_source_moto_v1.sql"})
        {
            var source=Read($"{Artifact}/sql/{file}");
            Assert.Contains("HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'INSERT')<>0",source,StringComparison.Ordinal);
            Assert.Contains("HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'EXECUTE')<>0",source,StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Feature_roles_are_bound_only_after_their_sealed_schema_exists()
    {
        var assignment=Read($"{Artifact}/sql/03_bind_assignment_role_after_migration.sql");
        var completion=Read($"{Artifact}/sql/04_bind_course_completion_role_after_migration.sql");
        Assert.Contains("QLHV_AssignmentApiRole",assignment,StringComparison.Ordinal);
        Assert.Contains("App_AssignmentOperation",assignment,StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TABLE",assignment,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("qlhv_course_completion_api",completion,StringComparison.Ordinal);
        Assert.Contains("App_CourseCompletionOperation",completion,StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TABLE",completion,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Forward_and_rollback_never_modify_existing_operator_principal_or_business_data()
    {
        var all=string.Join("\n",Directory.GetFiles(Path.Combine(Root,Artifact,"sql"),"*.sql").Select(File.ReadAllText));
        Assert.DoesNotMatch(new Regex(@"(?i)(DROP|ALTER|DENY|REVOKE)\s+(LOGIN|USER)?.*CSDLTTTC\\tttcd"),all);
        var rollback=Read($"{Artifact}/sql/05_rollback_api_principal.sql");
        Assert.Contains("sys.dm_exec_sessions",rollback,StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"(?im)^\s*(DELETE|UPDATE|MERGE|TRUNCATE)\s+dbo\.App_"),all);
    }

    private static string Read(string relative)=>File.ReadAllText(Path.Combine(Root,relative.Replace('/',Path.DirectorySeparatorChar)));
}

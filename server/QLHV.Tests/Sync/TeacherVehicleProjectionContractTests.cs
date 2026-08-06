using System.Text.RegularExpressions;

namespace QLHV.Tests.Sync;

public sealed class TeacherVehicleProjectionContractTests
{
    [Fact]
    public void Target_migration_is_additive_profile_scoped_and_has_separate_checkpoints()
    {
        var sql = Read("database", "patches", "20260806_add_teacher_vehicle_projection_sync.sql");

        Assert.Contains("9C44B304-8A84-4D0D-9A82-19C7233FF6BB", sql, StringComparison.Ordinal);
        Assert.Contains("App_TeacherVehicleProjectionCheckpoint", sql, StringComparison.Ordinal);
        Assert.Contains("SourceProfileCode,DomainName,ContractVersion", sql, StringComparison.Ordinal);
        Assert.Contains("SourceMaHocVien", sql, StringComparison.Ordinal);
        Assert.Contains("DiaDiem", sql, StringComparison.Ordinal);
        Assert.Contains("TenHocVien", sql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE INDEX UX_App_KhoaHoc_XeTap_TvpSourceIdentity", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("App_Rt03RealtimeCheckpoint", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("20260806_enable_teacher_vehicle_projection_ct_oto.sql", "CSDL_OTO", "9A8B9BC1-18F3-4823-8123-3DC197A9D540")]
    [InlineData("20260806_enable_teacher_vehicle_projection_ct_moto.sql", "CSDL_MOTO", "308BDDA8-80F3-4ACB-9836-578D80A9E98E")]
    public void Source_migration_enables_only_the_four_approved_projection_tables(
        string file,
        string database,
        string databaseGuid)
    {
        var sql = Read("database", "patches", file);

        Assert.Contains($"USE [{database}]", sql, StringComparison.Ordinal);
        Assert.Contains(databaseGuid, sql, StringComparison.Ordinal);
        foreach (var table in new[] { "GiaoVien", "XeTap", "KhoaHoc_GiaoVien", "KhoaHoc_XeTap" })
        {
            Assert.Single(Regex.Matches(sql,
                $@"ALTER TABLE dbo\.{table} ENABLE CHANGE_TRACKING",
                RegexOptions.CultureInvariant).Cast<Match>());
        }
        Assert.DoesNotContain("INSERT ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runtime_uses_existing_worker_and_real_production_writer_gates()
    {
        var worker = Read("server", "QLHV.Infrastructure", "Sync", "Rt03", "Rt03ProductionRealtimeWorker.cs");
        var coordinator = Read("server", "QLHV.Infrastructure", "Sync", "TeacherVehicleProjection", "SqlTeacherVehicleProjectionCoordinator.cs");
        var vehicleFullStore = Read("server", "QLHV.Infrastructure", "Sync", "VehicleRealtime", "SqlVehicleFullConvergenceTargetStore.cs");
        var program = Read("server", "QLHV.Worker", "Program.cs");

        Assert.Contains("ITeacherVehicleProjectionCoordinator", worker, StringComparison.Ordinal);
        Assert.Contains("--teacher-vehicle-projection-bootstrap", program, StringComparison.Ordinal);
        Assert.Contains("App_QlhvAutoSyncRun", coordinator, StringComparison.Ordinal);
        Assert.Contains("App_QlhvSyncOperationHistory", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoSyncEnabled", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("OperationInProgress", coordinator, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Snapshot", coordinator, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", coordinator, StringComparison.Ordinal);
        Assert.Contains("TVP_CT_WINDOW_EXPIRED", coordinator, StringComparison.Ordinal);
        Assert.Contains("CSDLTTTC\\QLHVRT02", coordinator, StringComparison.Ordinal);
        Assert.Contains("DisposableRehearsalEnabled", coordinator, StringComparison.Ordinal);
        Assert.Contains("return route.ExpectedProductionDatabaseGuid", coordinator, StringComparison.Ordinal);
        Assert.Contains("CSDLTTTC\\QLHVRT02", vehicleFullStore, StringComparison.Ordinal);
        Assert.Contains("VehicleRealtimeTargetDatabase.ExpectedProductionDatabaseGuid", vehicleFullStore, StringComparison.Ordinal);
    }

    [Fact]
    public void Permission_contract_keeps_api_and_sources_read_only()
    {
        var forward = Read("database", "patches", "20260806_grant_teacher_vehicle_projection_worker.sql");
        var verify = Read("database", "patches", "20260806_verify_teacher_vehicle_projection_security.sql");

        foreach (var table in new[] { "App_GiaoVien", "App_XeTap", "App_KhoaHoc_GiaoVien", "App_KhoaHoc_XeTap" })
            Assert.Contains($"DENY INSERT,UPDATE,DELETE ON dbo.{table} TO [NT SERVICE\\QLHV_APP_Api]", forward, StringComparison.Ordinal);
        foreach (var table in new[] { "GiaoVien", "XeTap", "KhoaHoc_GiaoVien", "KhoaHoc_XeTap" })
        {
            Assert.Contains($"GRANT SELECT,VIEW CHANGE TRACKING ON dbo.{table}", forward, StringComparison.Ordinal);
            Assert.Contains($"DENY INSERT,UPDATE,DELETE ON dbo.{table}", forward, StringComparison.Ordinal);
        }
        Assert.Contains("TVP_TARGET_API_READONLY_REJECTED", verify, StringComparison.Ordinal);
        Assert.Contains("TVP_OTO_WORKER_SOURCE_PERMISSION_REJECTED", verify, StringComparison.Ordinal);
        Assert.Contains("TVP_MOTO_WORKER_SOURCE_PERMISSION_REJECTED", verify, StringComparison.Ordinal);
    }

    [Fact]
    public void Dossier_tab_fails_closed_without_manual_master_creation()
    {
        var page = Read("client", "src", "features", "course-assignment", "TeacherPage.tsx");
        var controller = Read("server", "QLHV.Api", "Controllers", "AssignmentControllers.cs");
        var repository = Read("server", "QLHV.Infrastructure", "Assignments", "SqlAssignmentRepository.cs");

        Assert.Contains("Chờ bằng chứng quan hệ nguồn", page, StringComparison.Ordinal);
        Assert.DoesNotContain("searchDossierReceivers", page, StringComparison.Ordinal);
        Assert.Contains("DOSSIER_MAPPING_EVIDENCE_REQUIRED", controller, StringComparison.Ordinal);
        Assert.Contains("TotalItems = 0", repository, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine([Root(), .. parts]));

    private static string Root()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "server", "QLHV.sln")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}

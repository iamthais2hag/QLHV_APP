using System.Runtime.CompilerServices;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Tests.Sync;

public sealed class MotoSyncServiceTests
{
    [Fact]
    public async Task Execute_rejects_when_confirm_text_is_missing()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan());
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(new MotoSyncTestExecuteRequest
        {
            Direction = MotoSyncDirection.V1_TO_V2,
            SourceProfileCode = "CSDT_V1",
            TargetProfileCode = "CSDT_V2",
            ConfirmText = "WRONG",
        });

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains("SYNC TEST DATABASE", result.Message);
        Assert.Equal(0, repo.PlanCalls);
        Assert.Equal(0, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Insert_and_update_requires_update_confirm_text()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(plannedUpdateNguoiLx: 1));
        var service = new MotoSyncService(repo);

        var request = ConfirmedRequest();
        request.SyncMode = MotoSyncMode.INSERT_AND_UPDATE;

        var result = await service.ExecuteTestAsync(request);

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains(MotoSyncService.UpdateConfirmationText, result.Message);
        Assert.Equal(0, repo.PlanCalls);
        Assert.Equal(0, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Plan_blocks_non_test_profile()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan());
        var service = new MotoSyncService(repo);

        var result = await service.GetPlanAsync(new MotoSyncPlanRequest
        {
            Direction = MotoSyncDirection.V1_TO_V2,
            SourceProfileCode = "DATA_V1",
            TargetProfileCode = "CSDT_V2",
        });

        Assert.False(result.Executable);
        Assert.Contains(result.Blockers, blocker => blocker.Contains("CSDT_V1 va CSDT_V2", StringComparison.Ordinal));
        Assert.Equal(0, repo.PlanCalls);
        Assert.Equal(0, repo.ExecuteCalls);
    }

    [Fact]
    public async Task Khoa_hoc_options_blocks_non_test_profile_without_repo_call()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan());
        var service = new MotoSyncService(repo);

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetKhoaHocOptionsAsync(new MotoSyncKhoaHocOptionsQuery
        {
            Direction = MotoSyncDirection.V1_TO_V2,
            SourceProfileCode = "DATA_V1",
            TargetProfileCode = "CSDT_V2",
        }));

        Assert.Equal(0, repo.KhoaHocOptionCalls);
    }

    [Fact]
    public async Task Khoa_hoc_options_normalizes_profiles_search_and_take()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan());
        var service = new MotoSyncService(repo);

        await service.GetKhoaHocOptionsAsync(new MotoSyncKhoaHocOptionsQuery
        {
            Direction = MotoSyncDirection.V1_TO_V2,
            SourceProfileCode = " csdt_v1 ",
            TargetProfileCode = " csdt_v2 ",
            Search = "  AK15 ",
            Take = 500,
        });

        Assert.Equal(1, repo.KhoaHocOptionCalls);
        Assert.NotNull(repo.LastKhoaHocOptionsQuery);
        Assert.Equal("CSDT_V1", repo.LastKhoaHocOptionsQuery!.SourceProfileCode);
        Assert.Equal("CSDT_V2", repo.LastKhoaHocOptionsQuery.TargetProfileCode);
        Assert.Equal("AK15", repo.LastKhoaHocOptionsQuery.Search);
        Assert.Equal(200, repo.LastKhoaHocOptionsQuery.Take);
    }

    [Fact]
    public async Task Center_transfer_plan_normalizes_profiles_and_computes_new_course_code()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan());
        var service = new MotoSyncService(repo);

        var result = await service.GetCenterTransferPlanAsync(new MotoCenterTransferPlanRequest
        {
            SourceProfileCode = " csdt_v1 ",
            TargetProfileCode = " csdt_v2 ",
            MaKhoaHocCu = " 66016K26A1003 ",
            MaCSDTCu = "66016",
            MaCSDTMoi = "75044",
            MaSoGTVTMoi = "GTVT-MOI",
        });

        Assert.True(result.Executable);
        Assert.Equal("CSDT_V1", result.SourceProfileCode);
        Assert.Equal("CSDT_V2", result.TargetProfileCode);
        Assert.Equal("66016K26A1003", result.MaKhoaHocCu);
        Assert.Equal("75044K26A1003", result.MaKhoaHocMoi);
        Assert.Equal(1, repo.CenterTransferPlanCalls);
        Assert.NotNull(repo.LastCenterTransferPlanRequest);
        Assert.Equal("GTVT-MOI", repo.LastCenterTransferPlanRequest!.MaSoGTVTMoi);
    }

    [Fact]
    public async Task Center_transfer_plan_includes_donvigttv_names_when_codes_exist()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            CenterTransferPlan = CleanCenterTransferPlan(
                targetMaCSDTMoiTenDV: "Trung tam moi",
                targetMaSoGTVTMoiTenDV: "So GTVT moi"),
        };
        var service = new MotoSyncService(repo);

        var result = await service.GetCenterTransferPlanAsync(CenterTransferRequest());

        Assert.True(result.TargetMaCSDTMoiExists);
        Assert.Equal("Trung tam moi", result.TargetMaCSDTMoiTenDV);
        Assert.True(result.TargetMaSoGTVTMoiExists);
        Assert.Equal("So GTVT moi", result.TargetMaSoGTVTMoiTenDV);
    }

    [Fact]
    public async Task Center_transfer_plan_blocks_invalid_profile_without_repo_call()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan());
        var service = new MotoSyncService(repo);

        var result = await service.GetCenterTransferPlanAsync(new MotoCenterTransferPlanRequest
        {
            SourceProfileCode = "CSDT_V2",
            TargetProfileCode = "CSDT_V1",
            MaKhoaHocCu = "66016K26A1003",
            MaCSDTCu = "66016",
            MaCSDTMoi = "75044",
            MaSoGTVTMoi = "GTVT-MOI",
        });

        Assert.False(result.Executable);
        Assert.Contains(result.Blockers, blocker => blocker.Contains("CSDT_V1 sang CSDT_V2", StringComparison.Ordinal));
        Assert.Equal(0, repo.CenterTransferPlanCalls);
        Assert.Equal(0, repo.CenterTransferExecuteCalls);
    }

    [Fact]
    public async Task Center_transfer_plan_blocks_when_ma_csdt_moi_missing_in_target_dm_donvigttv()
    {
        const string blocker = "MaCSDTMoi không tồn tại trong DM_DonViGTVT của target: 99099.";
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            CenterTransferPlan = CleanCenterTransferPlan(
                targetMaCSDTMoiExists: false,
                blockers: new[] { blocker }),
        };
        var service = new MotoSyncService(repo);

        var result = await service.GetCenterTransferPlanAsync(CenterTransferRequest());

        Assert.False(result.Executable);
        Assert.False(result.TargetMaCSDTMoiExists);
        Assert.Contains(blocker, result.Blockers);
        Assert.Equal(1, repo.CenterTransferPlanCalls);
    }

    [Fact]
    public async Task Center_transfer_plan_blocks_when_ma_so_gtvt_moi_missing_in_target_dm_donvigttv()
    {
        const string blocker = "MaSoGTVTMoi không tồn tại trong DM_DonViGTVT của target: 99.";
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            CenterTransferPlan = CleanCenterTransferPlan(
                targetMaSoGTVTMoiExists: false,
                blockers: new[] { blocker }),
        };
        var service = new MotoSyncService(repo);

        var result = await service.GetCenterTransferPlanAsync(CenterTransferRequest());

        Assert.False(result.Executable);
        Assert.False(result.TargetMaSoGTVTMoiExists);
        Assert.Contains(blocker, result.Blockers);
        Assert.Equal(1, repo.CenterTransferPlanCalls);
    }

    [Fact]
    public async Task Center_transfer_execute_requires_exact_confirm_text()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan());
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteCenterTransferTestAsync(CenterTransferRequest(confirmText: "WRONG"));

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains(MotoSyncService.CenterTransferConfirmationText, result.Message);
        Assert.Equal(0, repo.CenterTransferPlanCalls);
        Assert.Equal(0, repo.CenterTransferExecuteCalls);
    }

    [Fact]
    public async Task Center_transfer_execute_refuses_donvigttv_blocker_before_write()
    {
        const string blocker = "MaCSDTMoi không tồn tại trong DM_DonViGTVT của target: 99099.";
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            CenterTransferPlan = CleanCenterTransferPlan(
                targetMaCSDTMoiExists: false,
                blockers: new[] { blocker }),
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteCenterTransferTestAsync(CenterTransferRequest());

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains(blocker, result.Plan!.Blockers);
        Assert.Equal(1, repo.CenterTransferPlanCalls);
        Assert.Equal(0, repo.CenterTransferExecuteCalls);
    }

    [Fact]
    public async Task Center_transfer_execute_refuses_missing_source_course()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            CenterTransferPlan = CleanCenterTransferPlan(
                sourceKhoaHocCount: 0,
                blockers: new[] { "Source thieu KhoaHoc theo MaKhoaHocCu." }),
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteCenterTransferTestAsync(CenterTransferRequest());

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains(result.Plan!.Blockers, blocker => blocker.Contains("Source thieu KhoaHoc", StringComparison.Ordinal));
        Assert.Equal(1, repo.CenterTransferPlanCalls);
        Assert.Equal(0, repo.CenterTransferExecuteCalls);
    }

    [Fact]
    public async Task Center_transfer_plan_blocks_when_course_code_does_not_contain_old_center_code()
    {
        const string blocker = "MaKhoaHocCu không chứa MaCSDTCu nên không thể tính MaKhoaHocMoi chính xác.";
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            CenterTransferPlan = CleanCenterTransferPlan(blockers: new[] { blocker }),
        };
        var service = new MotoSyncService(repo);

        var result = await service.GetCenterTransferPlanAsync(CenterTransferRequest());

        Assert.False(result.Executable);
        Assert.Contains(blocker, result.Blockers);
        Assert.Equal(1, repo.CenterTransferPlanCalls);
    }

    [Fact]
    public async Task Center_transfer_plan_blocks_when_source_khoahoc_ma_csdt_mismatches_request()
    {
        const string blocker = "MaCSDTCu không khớp MaCSDT của khóa nguồn.";
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            CenterTransferPlan = CleanCenterTransferPlan(blockers: new[] { blocker }),
        };
        var service = new MotoSyncService(repo);

        var result = await service.GetCenterTransferPlanAsync(CenterTransferRequest());

        Assert.False(result.Executable);
        Assert.Contains(blocker, result.Blockers);
        Assert.Equal(1, repo.CenterTransferPlanCalls);
    }

    [Fact]
    public async Task Center_transfer_execute_refuses_when_target_new_course_exists()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            CenterTransferPlan = CleanCenterTransferPlan(
                targetKhoaHocMoiCount: 1,
                blockers: new[] { "Target da co MaKhoaHocMoi, khong the chuyen tiep an toan." }),
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteCenterTransferTestAsync(CenterTransferRequest());

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains(result.Plan!.Blockers, blocker => blocker.Contains("MaKhoaHocMoi", StringComparison.Ordinal));
        Assert.Equal(1, repo.CenterTransferPlanCalls);
        Assert.Equal(0, repo.CenterTransferExecuteCalls);
    }

    [Fact]
    public async Task Center_transfer_plan_blocks_when_target_old_course_exists()
    {
        const string blocker = "Target đã có MaKhoaHocCu; không thể phân biệt dữ liệu có sẵn với dữ liệu vừa copy. Hãy chọn target sạch hoặc khóa chưa tồn tại.";
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            CenterTransferPlan = CleanCenterTransferPlan(
                targetKhoaHocCuCount: 1,
                blockers: new[] { blocker }),
        };
        var service = new MotoSyncService(repo);

        var result = await service.GetCenterTransferPlanAsync(CenterTransferRequest());

        Assert.False(result.Executable);
        Assert.Equal(1, result.TargetKhoaHocCuCount);
        Assert.Contains(blocker, result.Blockers);
        Assert.Equal(1, repo.CenterTransferPlanCalls);
    }

    [Fact]
    public async Task Center_transfer_plan_blocks_when_target_old_hoso_exists()
    {
        const string blocker = "Target đã có MaKhoaHocCu; không thể phân biệt dữ liệu có sẵn với dữ liệu vừa copy. Hãy chọn target sạch hoặc khóa chưa tồn tại.";
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            CenterTransferPlan = CleanCenterTransferPlan(
                targetNguoiLXHoSoCuCount: 216,
                blockers: new[] { blocker }),
        };
        var service = new MotoSyncService(repo);

        var result = await service.GetCenterTransferPlanAsync(CenterTransferRequest());

        Assert.False(result.Executable);
        Assert.Equal(216, result.TargetNguoiLXHoSoCuCount);
        Assert.Contains(blocker, result.Blockers);
        Assert.Equal(1, repo.CenterTransferPlanCalls);
    }

    [Fact]
    public async Task Center_transfer_plan_blocks_when_target_old_giay_to_exists()
    {
        const string blocker = "Target đã có MaKhoaHocCu; không thể phân biệt dữ liệu có sẵn với dữ liệu vừa copy. Hãy chọn target sạch hoặc khóa chưa tồn tại.";
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            CenterTransferPlan = CleanCenterTransferPlan(
                targetGiayToCuCount: 4,
                blockers: new[] { blocker }),
        };
        var service = new MotoSyncService(repo);

        var result = await service.GetCenterTransferPlanAsync(CenterTransferRequest());

        Assert.False(result.Executable);
        Assert.Equal(4, result.TargetNguoiLXHSGiayToCuCount);
        Assert.Contains(blocker, result.Blockers);
        Assert.Equal(1, repo.CenterTransferPlanCalls);
    }

    [Fact]
    public async Task Center_transfer_execute_refuses_when_target_old_hoso_exists_before_write()
    {
        const string blocker = "Target đã có MaKhoaHocCu; không thể phân biệt dữ liệu có sẵn với dữ liệu vừa copy. Hãy chọn target sạch hoặc khóa chưa tồn tại.";
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            CenterTransferPlan = CleanCenterTransferPlan(
                targetNguoiLXHoSoCuCount: 216,
                blockers: new[] { blocker }),
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteCenterTransferTestAsync(CenterTransferRequest());

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains(blocker, result.Plan!.Blockers);
        Assert.Equal(1, repo.CenterTransferPlanCalls);
        Assert.Equal(0, repo.CenterTransferExecuteCalls);
    }

    [Fact]
    public async Task Center_transfer_execute_success_calls_repository_once()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan());
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteCenterTransferTestAsync(CenterTransferRequest());

        Assert.True(result.Executed);
        Assert.Equal("ThanhCong", result.Status);
        Assert.NotNull(result.Summary);
        Assert.Equal("75044K26A1003", result.Summary!.MaKhoaHocMoi);
        Assert.Equal(1, repo.CenterTransferPlanCalls);
        Assert.Equal(1, repo.CenterTransferExecuteCalls);
    }

    [Fact]
    public async Task Center_transfer_plan_returns_giay_to_counts_and_identity_warning()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            CenterTransferPlan = CleanCenterTransferPlan(
                sourceGiayToCount: 7,
                targetGiayToCuCount: 1,
                targetGiayToMoiCount: 2,
                plannedCopyGiayTo: 6),
        };
        var service = new MotoSyncService(repo);

        var result = await service.GetCenterTransferPlanAsync(CenterTransferRequest());

        Assert.True(result.Executable);
        Assert.Equal(7, result.SourceNguoiLXHSGiayToCount);
        Assert.Equal(1, result.TargetNguoiLXHSGiayToCuCount);
        Assert.Equal(2, result.TargetNguoiLXHSGiayToMoiCount);
        Assert.Equal(6, result.PlannedCopyNguoiLXHSGiayTo);
        Assert.Contains(result.Warnings, warning => warning.Contains("Cột identity không được copy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Center_transfer_plan_missing_giay_to_table_warns_without_blocking()
    {
        const string warning = "Source thieu bang dbo.NguoiLXHS_GiayTo; bo qua copy giay to.";
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            CenterTransferPlan = CleanCenterTransferPlan(
                sourceGiayToCount: 0,
                targetGiayToCuCount: 0,
                targetGiayToMoiCount: 0,
                plannedCopyGiayTo: 0,
                warnings: new[] { warning }),
        };
        var service = new MotoSyncService(repo);

        var result = await service.GetCenterTransferPlanAsync(CenterTransferRequest());

        Assert.True(result.Executable);
        Assert.Equal(0, result.PlannedCopyNguoiLXHSGiayTo);
        Assert.Contains(warning, result.Warnings);
    }

    [Fact]
    public void Center_transfer_code_does_not_hardcode_owner_sample_values_or_destructive_keywords()
    {
        var source = File.ReadAllText(FindRepositoryFile());
        var service = File.ReadAllText(FindServiceFile());
        var combined = source + service;

        Assert.DoesNotContain("70014K26A1023", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("70014", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("75044", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE ", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE TABLE", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Center_transfer_repository_blocks_target_old_course_data_and_excludes_identity_insert()
    {
        var source = File.ReadAllText(FindRepositoryFile());

        Assert.Contains("targetKhoaHocCu > 0 || targetBaoCaoICu > 0 || targetHoSoCu > 0", source, StringComparison.Ordinal);
        Assert.Contains("targetGiayToCu > 0", source, StringComparison.Ordinal);
        Assert.Contains("Target đã có MaKhoaHocCu; không thể phân biệt dữ liệu có sẵn với dữ liệu vừa copy. Hãy chọn target sạch hoặc khóa chưa tồn tại.", source, StringComparison.Ordinal);
        Assert.Contains(".Where(c => !c.IsIdentity && !c.IsComputed && !c.IsRowVersion && !IsBinaryType(c.DataType))", source, StringComparison.Ordinal);
        Assert.Contains(".Where(c => !c.IsIdentity && !c.IsComputed && !c.IsRowVersion && !IsBinaryType(c.DataType) && sourceNames.Contains(c.Name))", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SET IDENTITY_INSERT", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Center_transfer_repository_copies_giay_to_before_update_and_scopes_update_to_selected_old_madks()
    {
        var source = File.ReadAllText(FindRepositoryFile());

        var copyIndex = source.IndexOf("CopyMissingGiayToForCenterTransferAsync(source, target, transaction, oldMaDks", StringComparison.Ordinal);
        var updateIndex = source.IndexOf("UpdateCenterTransferGiayToAsync(target, transaction, oldMaDks", StringComparison.Ordinal);
        Assert.True(copyIndex >= 0, "Expected GiayTo copy call in center-transfer execute path.");
        Assert.True(updateIndex >= 0, "Expected GiayTo update call in center-transfer execute path.");
        Assert.True(copyIndex < updateIndex, "GiayTo must be copied before MaDK is changed.");
        Assert.Contains("WHERE MaDK LIKE @MaDkPrefixLike", source, StringComparison.Ordinal);
        Assert.Contains("AND MaDK IN ({string.Join(\", \", parameters.Select(p => p.ParameterName))});", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Center_transfer_repository_has_giay_to_plan_counts_and_missing_table_warnings()
    {
        var source = File.ReadAllText(FindRepositoryFile());

        Assert.Contains("SourceNguoiLXHSGiayToCount = sourceGiayTo", source, StringComparison.Ordinal);
        Assert.Contains("TargetNguoiLXHSGiayToCuCount = targetGiayToCu", source, StringComparison.Ordinal);
        Assert.Contains("TargetNguoiLXHSGiayToMoiCount = targetGiayToMoi", source, StringComparison.Ordinal);
        Assert.Contains("PlannedCopyNguoiLXHSGiayTo = plannedCopyGiayTo", source, StringComparison.Ordinal);
        Assert.Contains("Source thieu bang dbo.NguoiLXHS_GiayTo; bo qua copy giay to.", source, StringComparison.Ordinal);
        Assert.Contains("Target thieu bang dbo.NguoiLXHS_GiayTo; bo qua copy/cap nhat giay to.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Center_transfer_repository_has_donvigttv_and_course_preflight_guards()
    {
        var source = File.ReadAllText(FindRepositoryFile());

        Assert.Contains("ReadDonViGTVTAsync(target, maCSDTMoi", source, StringComparison.Ordinal);
        Assert.Contains("ReadDonViGTVTAsync(target, maSoGTVTMoi", source, StringComparison.Ordinal);
        Assert.Contains("MaCSDTMoi không tồn tại trong DM_DonViGTVT của target", source, StringComparison.Ordinal);
        Assert.Contains("MaSoGTVTMoi không tồn tại trong DM_DonViGTVT của target", source, StringComparison.Ordinal);
        Assert.Contains("MaKhoaHocCu không chứa MaCSDTCu nên không thể tính MaKhoaHocMoi chính xác.", source, StringComparison.Ordinal);
        Assert.Contains("MaCSDTCu không khớp MaCSDT của khóa nguồn.", source, StringComparison.Ordinal);
        Assert.Contains("SELECT TOP (1) MaCSDT FROM dbo.KhoaHoc WHERE MaKH = @MaKhoaHoc;", source, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.DM_DonViGTVT WHERE LTRIM(RTRIM(MaDV)) = @MaDV", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Execute_rejects_when_plan_has_short_full_madk_blocker()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(shortFullPairs: 1));
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains(result.Plan!.Blockers, blocker => blocker.Contains("MaDK ngan", StringComparison.Ordinal));
        Assert.Equal(1, repo.PlanCalls);
        Assert.Equal(0, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Execute_rejects_when_plan_has_missing_khoa_hoc_dependency()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(missingKhoaHoc: 1));
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains(result.Plan!.Blockers, blocker => blocker.Contains("KhoaHoc", StringComparison.Ordinal));
        Assert.Equal(1, repo.PlanCalls);
        Assert.Equal(0, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Execute_allows_missing_khoa_hoc_dependency_when_plan_can_insert_khoa_hoc()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(
            missingKhoaHoc: 1,
            plannedInsertKhoaHoc: 1,
            missingKhoaHocIsBlocker: false));
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.Equal("ThanhCong", result.Status);
        Assert.NotNull(result.Plan);
        Assert.Equal(1L, result.Plan.PlannedInsertKhoaHoc);
        Assert.Equal(1L, result.Summary!.InsertedKhoaHoc);
        Assert.Empty(result.Plan.Blockers);
        Assert.Equal(2, repo.PlanCalls);
        Assert.Equal(1, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Execute_success_returns_before_plan_after_plan_and_keeps_old_plan_as_before_plan()
    {
        var beforePlan = CleanPlan(plannedInsertKhoaHoc: 1);
        var afterPlan = CleanPlan(
            plannedInsertNguoiLx: 0,
            plannedInsertHoSo: 0,
            plannedInsertGiayTo: 0);
        var repo = new FakeMotoSyncRepository(beforePlan)
        {
            AfterPlan = afterPlan,
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.Same(result.BeforePlan, result.Plan);
        Assert.NotNull(result.BeforePlan);
        Assert.NotNull(result.AfterPlan);
        Assert.Equal(1L, result.BeforePlan!.PlannedInsertKhoaHoc);
        Assert.Equal(0L, result.AfterPlan!.PlannedInsertKhoaHoc);
        Assert.False(result.HasRemainingWork);
        Assert.Equal(2, repo.PlanCalls);
    }

    [Fact]
    public async Task Execute_success_sets_has_remaining_work_when_after_plan_still_has_planned_work()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            AfterPlan = CleanPlan(
                plannedInsertBaoCaoI: 0,
                plannedInsertNguoiLx: 0,
                plannedInsertHoSo: 0,
                plannedInsertGiayTo: 0,
                plannedUpdateNguoiLx: 1),
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.True(result.HasRemainingWork);
        Assert.Equal(1L, result.AfterPlan!.PlannedUpdate);
        Assert.Equal(2, repo.PlanCalls);
    }

    [Fact]
    public async Task Execute_success_sets_has_remaining_work_when_after_plan_still_has_baocao_i_insert()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            AfterPlan = CleanPlan(
                plannedInsertBaoCaoI: 1,
                plannedInsertNguoiLx: 0,
                plannedInsertHoSo: 0,
                plannedInsertGiayTo: 0),
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.True(result.HasRemainingWork);
        Assert.Equal(1L, result.AfterPlan!.PlannedInsertBaoCaoI);
        Assert.Equal(2, repo.PlanCalls);
    }

    [Fact]
    public async Task Execute_success_sets_has_remaining_work_when_after_plan_still_has_nguoilx_gplx_insert()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            AfterPlan = CleanPlan(
                plannedInsertNguoiLXGplx: 1,
                plannedInsertNguoiLx: 0,
                plannedInsertHoSo: 0,
                plannedInsertGiayTo: 0),
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.True(result.HasRemainingWork);
        Assert.Equal(1L, result.AfterPlan!.PlannedInsertNguoiLXGPLX);
        Assert.Equal(2, repo.PlanCalls);
    }

    [Fact]
    public async Task Execute_success_sets_has_remaining_work_when_after_plan_has_blockers()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            AfterPlan = CleanPlan(
                shortFullPairs: 1,
                plannedInsertNguoiLx: 0,
                plannedInsertHoSo: 0,
                plannedInsertGiayTo: 0),
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.True(result.HasRemainingWork);
        Assert.NotEmpty(result.AfterPlan!.Blockers);
        Assert.Equal(2, repo.PlanCalls);
    }

    [Fact]
    public async Task Successful_insert_only_writes_run_history()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(plannedInsertKhoaHoc: 1));
        var history = new FakeMotoSyncRunHistoryRepository();
        var service = new MotoSyncService(repo, history);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        var entry = Assert.Single(history.Entries);
        Assert.Equal(MotoSyncMode.INSERT_ONLY, entry.SyncMode);
        Assert.True(entry.ConfirmTextMatched);
        Assert.True(entry.Executed);
        Assert.Equal("ThanhCong", entry.Status);
        Assert.Equal(1, entry.InsertedKhoaHoc);
        Assert.NotNull(entry.BeforePlanJson);
        Assert.NotNull(entry.AfterPlanJson);
    }

    [Fact]
    public async Task Successful_insert_and_update_writes_run_history()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(plannedUpdateNguoiLx: 1, plannedUpdateHoSo: 1));
        var history = new FakeMotoSyncRunHistoryRepository();
        var service = new MotoSyncService(repo, history);

        var request = ConfirmedRequest();
        request.SyncMode = MotoSyncMode.INSERT_AND_UPDATE;
        request.ConfirmText = MotoSyncService.UpdateConfirmationText;
        var result = await service.ExecuteTestAsync(request);

        Assert.True(result.Executed);
        var entry = Assert.Single(history.Entries);
        Assert.Equal(MotoSyncMode.INSERT_AND_UPDATE, entry.SyncMode);
        Assert.True(entry.ConfirmTextMatched);
        Assert.Equal(2, entry.UpdatedRows);
        Assert.NotNull(entry.BeforePlanJson);
        Assert.NotNull(entry.AfterPlanJson);
    }

    [Fact]
    public async Task Refused_blocker_writes_run_history_without_after_plan()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(shortFullPairs: 1));
        var history = new FakeMotoSyncRunHistoryRepository();
        var service = new MotoSyncService(repo, history);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.False(result.Executed);
        var entry = Assert.Single(history.Entries);
        Assert.Equal("BiChan", entry.Status);
        Assert.True(entry.ConfirmTextMatched);
        Assert.NotNull(entry.BeforePlanJson);
        Assert.Null(entry.AfterPlanJson);
    }

    [Fact]
    public async Task Wrong_confirm_writes_run_history_when_possible()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan());
        var history = new FakeMotoSyncRunHistoryRepository();
        var service = new MotoSyncService(repo, history);

        var result = await service.ExecuteTestAsync(new MotoSyncTestExecuteRequest
        {
            Direction = MotoSyncDirection.V1_TO_V2,
            SourceProfileCode = "CSDT_V1",
            TargetProfileCode = "CSDT_V2",
            ConfirmText = "WRONG",
        });

        Assert.False(result.Executed);
        var entry = Assert.Single(history.Entries);
        Assert.Equal("BiChan", entry.Status);
        Assert.False(entry.ConfirmTextMatched);
        Assert.NotNull(entry.BeforePlanJson);
        Assert.Null(entry.AfterPlanJson);
        Assert.Equal(0, repo.PlanCalls);
    }

    [Fact]
    public async Task Logging_failure_does_not_hide_successful_sync_result()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan());
        var history = new FakeMotoSyncRunHistoryRepository { ThrowOnCreate = true };
        var service = new MotoSyncService(repo, history);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.Equal("ThanhCong", result.Status);
        Assert.Contains("khong ghi duoc lich su", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_insert_only_plan_is_executable()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan());
        var service = new MotoSyncService(repo);

        var result = await service.GetPlanAsync(new MotoSyncPlanRequest
        {
            Direction = MotoSyncDirection.V1_TO_V2,
            SourceProfileCode = "csdt_v1",
            TargetProfileCode = "csdt_v2",
            MaKhoaHoc = " 66016K26A0001 ",
        });

        Assert.True(result.Executable);
        Assert.Equal("CSDT_V1", result.SourceProfileCode);
        Assert.Equal("CSDT_V2", result.TargetProfileCode);
        Assert.Equal("66016K26A0001", result.MaKhoaHoc);
        Assert.Equal(2, result.PlannedInsertNguoiLX);
        Assert.Equal(2, result.PlannedInsertNguoiLXHoSo);
        Assert.Equal(3, result.PlannedInsertGiayTo);
        Assert.Equal(0, result.PlannedUpdate);
        Assert.Empty(result.Blockers);
        Assert.Equal(1, repo.PlanCalls);
    }

    [Fact]
    public async Task Execute_runs_insert_only_summary_without_update_or_delete()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            ExecuteSummary = new MotoSyncExecuteSummaryDto
            {
                Direction = MotoSyncDirection.V1_TO_V2,
                SourceProfileCode = "CSDT_V1",
                TargetProfileCode = "CSDT_V2",
                InsertedBaoCaoI = 1,
                InsertedNguoiLX = 2,
                InsertedNguoiLXGPLX = 1,
                InsertedNguoiLXHoSo = 2,
                InsertedGiayTo = 3,
                UpdatedRows = 0,
                DeletedRows = 0,
            },
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.Equal("ThanhCong", result.Status);
        Assert.NotNull(result.Summary);
        Assert.Equal(1, result.Summary.InsertedBaoCaoI);
        Assert.Equal(2, result.Summary.InsertedNguoiLX);
        Assert.Equal(1, result.Summary.InsertedNguoiLXGPLX);
        Assert.Equal(2, result.Summary.InsertedNguoiLXHoSo);
        Assert.Equal(3, result.Summary.InsertedGiayTo);
        Assert.Equal(0, result.Summary.UpdatedRows);
        Assert.Equal(0, result.Summary.DeletedRows);
        Assert.Equal(2, repo.PlanCalls);
        Assert.Equal(1, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Insert_only_mode_does_not_call_update_path_even_when_plan_has_updates()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(plannedUpdateNguoiLx: 1, plannedUpdateHoSo: 1));
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.Equal(1, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Insert_and_update_mode_uses_update_path_with_update_confirm_text()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(plannedUpdateNguoiLx: 1, plannedUpdateHoSo: 1))
        {
            UpdateSummary = new MotoSyncExecuteSummaryDto
            {
                Direction = MotoSyncDirection.V1_TO_V2,
                SyncMode = MotoSyncMode.INSERT_AND_UPDATE,
                SourceProfileCode = "CSDT_V1",
                TargetProfileCode = "CSDT_V2",
                InsertedNguoiLX = 2,
                InsertedNguoiLXHoSo = 2,
                InsertedGiayTo = 3,
                UpdatedNguoiLX = 1,
                UpdatedNguoiLXHoSo = 1,
                UpdatedRows = 2,
                DeletedRows = 0,
            },
        };
        var service = new MotoSyncService(repo);

        var request = ConfirmedRequest();
        request.SyncMode = MotoSyncMode.INSERT_AND_UPDATE;
        request.ConfirmText = MotoSyncService.UpdateConfirmationText;
        var result = await service.ExecuteTestAsync(request);

        Assert.True(result.Executed);
        Assert.Equal("ThanhCong", result.Status);
        Assert.Equal(MotoSyncMode.INSERT_AND_UPDATE, result.Summary!.SyncMode);
        Assert.Equal(1, result.Summary.UpdatedNguoiLX);
        Assert.Equal(1, result.Summary.UpdatedNguoiLXHoSo);
        Assert.Equal(2, result.Summary.UpdatedRows);
        Assert.Equal(0, result.Summary.DeletedRows);
        Assert.Equal(2, repo.PlanCalls);
        Assert.Equal(0, repo.ExecuteCalls);
        Assert.Equal(1, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Insert_and_update_refuses_blockers()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(shortFullPairs: 1, plannedUpdateNguoiLx: 1));
        var service = new MotoSyncService(repo);

        var request = ConfirmedRequest();
        request.SyncMode = MotoSyncMode.INSERT_AND_UPDATE;
        request.ConfirmText = MotoSyncService.UpdateConfirmationText;
        var result = await service.ExecuteTestAsync(request);

        Assert.False(result.Executed);
        Assert.Contains(result.Plan!.Blockers, blocker => blocker.Contains("MaDK ngan", StringComparison.Ordinal));
        Assert.Equal(1, repo.PlanCalls);
        Assert.Equal(0, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    private static MotoSyncTestExecuteRequest ConfirmedRequest() => new()
    {
        Direction = MotoSyncDirection.V1_TO_V2,
        SourceProfileCode = "CSDT_V1",
        TargetProfileCode = "CSDT_V2",
        ConfirmText = MotoSyncService.ConfirmationText,
    };

    private static MotoCenterTransferTestRequest CenterTransferRequest(string confirmText = MotoSyncService.CenterTransferConfirmationText) => new()
    {
        SourceProfileCode = "CSDT_V1",
        TargetProfileCode = "CSDT_V2",
        MaKhoaHocCu = "66016K26A1003",
        MaCSDTCu = "66016",
        MaCSDTMoi = "75044",
        MaSoGTVTMoi = "GTVT-MOI",
        ConfirmText = confirmText,
    };

    private static MotoSyncPlanDto CleanPlan(
        long shortFullPairs = 0,
        long missingKhoaHoc = 0,
        long plannedInsertKhoaHoc = 0,
        long plannedInsertBaoCaoI = 0,
        long plannedInsertNguoiLXGplx = 0,
        bool missingKhoaHocIsBlocker = true,
        long plannedInsertNguoiLx = 2,
        long plannedInsertHoSo = 2,
        long plannedInsertGiayTo = 3,
        long plannedUpdateNguoiLx = 0,
        long plannedUpdateHoSo = 0)
    {
        var blockers = new List<string>();
        if (shortFullPairs > 0)
        {
            blockers.Add($"Nguon co {shortFullPairs} cap MaDK ngan/day du nghi duplicate.");
        }

        if (missingKhoaHoc > 0 && missingKhoaHocIsBlocker)
        {
            blockers.Add($"Target thieu {missingKhoaHoc} MaKhoaHoc dependency. Task nay khong tu tao KhoaHoc.");
        }

        return new MotoSyncPlanDto
        {
            Direction = MotoSyncDirection.V1_TO_V2,
            SourceProfileCode = "CSDT_V1",
            TargetProfileCode = "CSDT_V2",
            SourceRows = 10,
            TargetRows = 8,
            ExactMaDkOverlap = 8,
            SourceOnly = 2,
            TargetOnly = 0,
            DuplicateBusinessKeyGroups = 0,
            ShortFullMaDkPairs = shortFullPairs,
            MissingKhoaHocDependencies = missingKhoaHoc,
            PlannedInsertKhoaHoc = plannedInsertKhoaHoc,
            PlannedInsertBaoCaoI = plannedInsertBaoCaoI,
            PlannedInsertNguoiLX = plannedInsertNguoiLx,
            PlannedInsertNguoiLXGPLX = plannedInsertNguoiLXGplx,
            PlannedInsertNguoiLXHoSo = missingKhoaHoc > 0 ? 1 : plannedInsertHoSo,
            PlannedInsertGiayTo = plannedInsertGiayTo,
            PlannedUpdate = plannedUpdateNguoiLx + plannedUpdateHoSo,
            PlannedUpdateNguoiLX = plannedUpdateNguoiLx,
            PlannedUpdateNguoiLXHoSo = plannedUpdateHoSo,
            Executable = blockers.Count == 0,
            Blockers = blockers,
        };
    }

    private static MotoCenterTransferPlanDto CleanCenterTransferPlan(
        long sourceKhoaHocCount = 1,
        long targetKhoaHocMoiCount = 0,
        long targetKhoaHocCuCount = 0,
        long targetBaoCaoICuCount = 0,
        long targetNguoiLXHoSoCuCount = 0,
        long sourceGiayToCount = 3,
        long targetGiayToCuCount = 0,
        long targetGiayToMoiCount = 0,
        long plannedCopyGiayTo = 3,
        bool targetMaCSDTMoiExists = true,
        string? targetMaCSDTMoiTenDV = "Trung tam dao tao moi",
        bool targetMaSoGTVTMoiExists = true,
        string? targetMaSoGTVTMoiTenDV = "So GTVT moi",
        IReadOnlyList<string>? blockers = null,
        IReadOnlyList<string>? warnings = null)
    {
        blockers ??= Array.Empty<string>();
        warnings ??= new[] { "Cột identity không được copy; SQL Server tự sinh giá trị mới." };
        return new MotoCenterTransferPlanDto
        {
            SourceProfileCode = "CSDT_V1",
            TargetProfileCode = "CSDT_V2",
            MaKhoaHocCu = "66016K26A1003",
            MaKhoaHocMoi = "75044K26A1003",
            MaCSDTCu = "66016",
            MaCSDTMoi = "75044",
            MaSoGTVTMoi = "GTVT-MOI",
            TargetMaCSDTMoiExists = targetMaCSDTMoiExists,
            TargetMaCSDTMoiTenDV = targetMaCSDTMoiTenDV,
            TargetMaSoGTVTMoiExists = targetMaSoGTVTMoiExists,
            TargetMaSoGTVTMoiTenDV = targetMaSoGTVTMoiTenDV,
            SourceKhoaHocCount = sourceKhoaHocCount,
            SourceBaoCaoICount = 1,
            SourceNguoiLXCount = 10,
            SourceNguoiLXHoSoCount = 10,
            SourceNguoiLXHSGiayToCount = sourceGiayToCount,
            TargetKhoaHocCuCount = targetKhoaHocCuCount,
            TargetKhoaHocMoiCount = targetKhoaHocMoiCount,
            TargetBaoCaoICuCount = targetBaoCaoICuCount,
            TargetNguoiLXHoSoCuCount = targetNguoiLXHoSoCuCount,
            TargetNguoiLXHSGiayToCuCount = targetGiayToCuCount,
            TargetNguoiLXHSGiayToMoiCount = targetGiayToMoiCount,
            PlannedCopyNguoiLXHSGiayTo = plannedCopyGiayTo,
            Executable = blockers.Count == 0,
            Blockers = blockers,
            Warnings = warnings,
        };
    }

    private static string FindRepositoryFile([CallerFilePath] string testFile = "")
        => FindWorkspaceFile(testFile, "server", "QLHV.Infrastructure", "Sync", "MotoSyncRepository.cs");

    private static string FindServiceFile([CallerFilePath] string testFile = "")
        => FindWorkspaceFile(testFile, "server", "QLHV.Application", "Sync", "MotoSyncService.cs");

    private static string FindWorkspaceFile(string testFile, params string[] pathParts)
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

    private sealed class FakeMotoSyncRepository : IMotoSyncRepository
    {
        private readonly MotoSyncPlanDto _plan;

        public FakeMotoSyncRepository(MotoSyncPlanDto plan)
        {
            _plan = plan;
            ExecuteSummary = new MotoSyncExecuteSummaryDto
            {
                Direction = plan.Direction,
                SyncMode = MotoSyncMode.INSERT_ONLY,
                SourceProfileCode = plan.SourceProfileCode,
                TargetProfileCode = plan.TargetProfileCode,
                InsertedKhoaHoc = plan.PlannedInsertKhoaHoc,
                InsertedBaoCaoI = plan.PlannedInsertBaoCaoI,
                InsertedNguoiLX = plan.PlannedInsertNguoiLX,
                InsertedNguoiLXGPLX = plan.PlannedInsertNguoiLXGPLX,
                InsertedNguoiLXHoSo = plan.PlannedInsertNguoiLXHoSo,
                InsertedGiayTo = plan.PlannedInsertGiayTo,
            };
            UpdateSummary = new MotoSyncExecuteSummaryDto
            {
                Direction = plan.Direction,
                SyncMode = MotoSyncMode.INSERT_AND_UPDATE,
                SourceProfileCode = plan.SourceProfileCode,
                TargetProfileCode = plan.TargetProfileCode,
                InsertedKhoaHoc = plan.PlannedInsertKhoaHoc,
                InsertedBaoCaoI = plan.PlannedInsertBaoCaoI,
                InsertedNguoiLX = plan.PlannedInsertNguoiLX,
                InsertedNguoiLXGPLX = plan.PlannedInsertNguoiLXGPLX,
                InsertedNguoiLXHoSo = plan.PlannedInsertNguoiLXHoSo,
                InsertedGiayTo = plan.PlannedInsertGiayTo,
                UpdatedNguoiLX = plan.PlannedUpdateNguoiLX,
                UpdatedNguoiLXHoSo = plan.PlannedUpdateNguoiLXHoSo,
                UpdatedRows = plan.PlannedUpdate,
                DeletedRows = 0,
            };
        }

        public int PlanCalls { get; private set; }
        public int KhoaHocOptionCalls { get; private set; }
        public int CenterTransferPlanCalls { get; private set; }
        public int CenterTransferExecuteCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        public int UpdateExecuteCalls { get; private set; }
        public MotoSyncExecuteSummaryDto ExecuteSummary { get; init; }
        public MotoSyncExecuteSummaryDto UpdateSummary { get; init; }
        public MotoCenterTransferPlanDto CenterTransferPlan { get; init; } = CleanCenterTransferPlan();
        public MotoCenterTransferSummaryDto? CenterTransferSummary { get; init; }
        public MotoSyncPlanDto? AfterPlan { get; init; }
        public MotoSyncKhoaHocOptionsQuery? LastKhoaHocOptionsQuery { get; private set; }
        public MotoCenterTransferPlanRequest? LastCenterTransferPlanRequest { get; private set; }
        public MotoCenterTransferPlanRequest? LastCenterTransferExecuteRequest { get; private set; }

        public Task<MotoSyncPlanDto> BuildPlanAsync(
            MotoSyncPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            PlanCalls++;
            var plan = PlanCalls > 1 && AfterPlan is not null ? AfterPlan : _plan;
            return Task.FromResult(new MotoSyncPlanDto
            {
                Direction = request.Direction,
                SourceProfileCode = request.SourceProfileCode,
                TargetProfileCode = request.TargetProfileCode,
                MaKhoaHoc = request.MaKhoaHoc,
                AllowDirtyData = request.AllowDirtyData,
                SourceRows = plan.SourceRows,
                TargetRows = plan.TargetRows,
                ExactMaDkOverlap = plan.ExactMaDkOverlap,
                SourceOnly = plan.SourceOnly,
                TargetOnly = plan.TargetOnly,
                DuplicateBusinessKeyGroups = plan.DuplicateBusinessKeyGroups,
                ShortFullMaDkPairs = plan.ShortFullMaDkPairs,
                MissingKhoaHocDependencies = plan.MissingKhoaHocDependencies,
                PlannedInsertKhoaHoc = plan.PlannedInsertKhoaHoc,
                PlannedInsertBaoCaoI = plan.PlannedInsertBaoCaoI,
                PlannedInsertNguoiLX = plan.PlannedInsertNguoiLX,
                PlannedInsertNguoiLXGPLX = plan.PlannedInsertNguoiLXGPLX,
                PlannedInsertNguoiLXHoSo = plan.PlannedInsertNguoiLXHoSo,
                PlannedInsertGiayTo = plan.PlannedInsertGiayTo,
                PlannedUpdate = plan.PlannedUpdate,
                PlannedUpdateNguoiLX = plan.PlannedUpdateNguoiLX,
                PlannedUpdateNguoiLXHoSo = plan.PlannedUpdateNguoiLXHoSo,
                UpdateSamples = plan.UpdateSamples,
                Executable = plan.Executable,
                Blockers = plan.Blockers,
                Warnings = plan.Warnings,
                Errors = plan.Errors,
            });
        }

        public Task<IReadOnlyList<MotoSyncKhoaHocOptionDto>> GetKhoaHocOptionsAsync(
            MotoSyncKhoaHocOptionsQuery query,
            CancellationToken cancellationToken = default)
        {
            KhoaHocOptionCalls++;
            LastKhoaHocOptionsQuery = query;
            return Task.FromResult<IReadOnlyList<MotoSyncKhoaHocOptionDto>>(Array.Empty<MotoSyncKhoaHocOptionDto>());
        }

        public Task<MotoCenterTransferPlanDto> BuildCenterTransferPlanAsync(
            MotoCenterTransferPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            CenterTransferPlanCalls++;
            LastCenterTransferPlanRequest = request;
            return Task.FromResult(new MotoCenterTransferPlanDto
            {
                SourceProfileCode = request.SourceProfileCode,
                TargetProfileCode = request.TargetProfileCode,
                MaKhoaHocCu = request.MaKhoaHocCu ?? string.Empty,
                MaKhoaHocMoi = (request.MaKhoaHocCu ?? string.Empty).Replace(
                    request.MaCSDTCu ?? string.Empty,
                    request.MaCSDTMoi ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase),
                MaCSDTCu = request.MaCSDTCu ?? string.Empty,
                MaCSDTMoi = request.MaCSDTMoi ?? string.Empty,
                MaSoGTVTMoi = request.MaSoGTVTMoi ?? string.Empty,
                TargetMaCSDTMoiExists = CenterTransferPlan.TargetMaCSDTMoiExists,
                TargetMaCSDTMoiTenDV = CenterTransferPlan.TargetMaCSDTMoiTenDV,
                TargetMaSoGTVTMoiExists = CenterTransferPlan.TargetMaSoGTVTMoiExists,
                TargetMaSoGTVTMoiTenDV = CenterTransferPlan.TargetMaSoGTVTMoiTenDV,
                SourceKhoaHocCount = CenterTransferPlan.SourceKhoaHocCount,
                SourceBaoCaoICount = CenterTransferPlan.SourceBaoCaoICount,
                SourceNguoiLXCount = CenterTransferPlan.SourceNguoiLXCount,
                SourceNguoiLXHoSoCount = CenterTransferPlan.SourceNguoiLXHoSoCount,
                SourceNguoiLXHSGiayToCount = CenterTransferPlan.SourceNguoiLXHSGiayToCount,
                TargetKhoaHocCuCount = CenterTransferPlan.TargetKhoaHocCuCount,
                TargetKhoaHocMoiCount = CenterTransferPlan.TargetKhoaHocMoiCount,
                TargetBaoCaoICuCount = CenterTransferPlan.TargetBaoCaoICuCount,
                TargetBaoCaoIMoiCount = CenterTransferPlan.TargetBaoCaoIMoiCount,
                TargetNguoiLXHoSoCuCount = CenterTransferPlan.TargetNguoiLXHoSoCuCount,
                TargetNguoiLXHoSoMoiCount = CenterTransferPlan.TargetNguoiLXHoSoMoiCount,
                TargetNguoiLXHSGiayToCuCount = CenterTransferPlan.TargetNguoiLXHSGiayToCuCount,
                TargetNguoiLXHSGiayToMoiCount = CenterTransferPlan.TargetNguoiLXHSGiayToMoiCount,
                PlannedCopyNguoiLXHSGiayTo = CenterTransferPlan.PlannedCopyNguoiLXHSGiayTo,
                Executable = CenterTransferPlan.Blockers.Count == 0 && CenterTransferPlan.Executable,
                Blockers = CenterTransferPlan.Blockers,
                Warnings = CenterTransferPlan.Warnings,
            });
        }

        public Task<MotoCenterTransferSummaryDto> ExecuteCenterTransferAsync(
            MotoCenterTransferPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            CenterTransferExecuteCalls++;
            LastCenterTransferExecuteRequest = request;
            return Task.FromResult(CenterTransferSummary ?? new MotoCenterTransferSummaryDto
            {
                SourceProfileCode = request.SourceProfileCode,
                TargetProfileCode = request.TargetProfileCode,
                MaKhoaHocCu = request.MaKhoaHocCu ?? string.Empty,
                MaKhoaHocMoi = (request.MaKhoaHocCu ?? string.Empty).Replace(
                    request.MaCSDTCu ?? string.Empty,
                    request.MaCSDTMoi ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase),
                CopiedKhoaHoc = CenterTransferPlan.SourceKhoaHocCount,
                CopiedBaoCaoI = CenterTransferPlan.SourceBaoCaoICount,
                CopiedNguoiLX = CenterTransferPlan.SourceNguoiLXCount,
                CopiedNguoiLXHoSo = CenterTransferPlan.SourceNguoiLXHoSoCount,
                CopiedNguoiLXHSGiayTo = CenterTransferPlan.PlannedCopyNguoiLXHSGiayTo,
                UpdatedKhoaHoc = 1,
                TargetKhoaHocMoiCountAfter = 1,
                TargetBaoCaoIMoiCountAfter = CenterTransferPlan.SourceBaoCaoICount,
                TargetNguoiLXHoSoMoiCountAfter = CenterTransferPlan.SourceNguoiLXHoSoCount,
                TargetNguoiLXHSGiayToMoiCountAfter = CenterTransferPlan.PlannedCopyNguoiLXHSGiayTo,
                TargetNguoiLXMoiCountAfter = CenterTransferPlan.SourceNguoiLXCount,
                StartedAt = DateTime.UtcNow,
                EndedAt = DateTime.UtcNow,
            });
        }

        public Task<MotoSyncExecuteSummaryDto> ExecuteInsertOnlyAsync(
            MotoSyncPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            return Task.FromResult(ExecuteSummary);
        }

        public Task<MotoSyncExecuteSummaryDto> ExecuteInsertAndUpdateAsync(
            MotoSyncPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            UpdateExecuteCalls++;
            return Task.FromResult(UpdateSummary);
        }
    }

    private sealed class FakeMotoSyncRunHistoryRepository : IMotoSyncRunHistoryRepository
    {
        public List<MotoSyncRunHistoryCreateDto> Entries { get; } = new();
        public bool ThrowOnCreate { get; init; }

        public Task<long> CreateAsync(
            MotoSyncRunHistoryCreateDto entry,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnCreate)
            {
                throw new InvalidOperationException("history failed");
            }

            Entries.Add(entry);
            return Task.FromResult((long)Entries.Count);
        }

        public Task<IReadOnlyList<MotoSyncRunHistoryListItemDto>> SearchAsync(
            MotoSyncRunHistoryQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MotoSyncRunHistoryListItemDto>>(Array.Empty<MotoSyncRunHistoryListItemDto>());

        public Task<MotoSyncRunHistoryDetailDto?> GetByIdAsync(
            long id,
            CancellationToken cancellationToken = default)
            => Task.FromResult<MotoSyncRunHistoryDetailDto?>(null);
    }
}

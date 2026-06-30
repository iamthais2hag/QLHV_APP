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
                InsertedNguoiLX = 2,
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
        Assert.Equal(2, result.Summary.InsertedNguoiLX);
        Assert.Equal(2, result.Summary.InsertedNguoiLXHoSo);
        Assert.Equal(3, result.Summary.InsertedGiayTo);
        Assert.Equal(0, result.Summary.UpdatedRows);
        Assert.Equal(0, result.Summary.DeletedRows);
        Assert.Equal(1, repo.PlanCalls);
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
        Assert.Equal(1, repo.PlanCalls);
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

    private static MotoSyncPlanDto CleanPlan(
        long shortFullPairs = 0,
        long missingKhoaHoc = 0,
        long plannedUpdateNguoiLx = 0,
        long plannedUpdateHoSo = 0)
    {
        var blockers = new List<string>();
        if (shortFullPairs > 0)
        {
            blockers.Add($"Nguon co {shortFullPairs} cap MaDK ngan/day du nghi duplicate.");
        }

        if (missingKhoaHoc > 0)
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
            PlannedInsertNguoiLX = 2,
            PlannedInsertNguoiLXHoSo = missingKhoaHoc > 0 ? 1 : 2,
            PlannedInsertGiayTo = 3,
            PlannedUpdate = plannedUpdateNguoiLx + plannedUpdateHoSo,
            PlannedUpdateNguoiLX = plannedUpdateNguoiLx,
            PlannedUpdateNguoiLXHoSo = plannedUpdateHoSo,
            Executable = blockers.Count == 0,
            Blockers = blockers,
        };
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
                InsertedNguoiLX = plan.PlannedInsertNguoiLX,
                InsertedNguoiLXHoSo = plan.PlannedInsertNguoiLXHoSo,
                InsertedGiayTo = plan.PlannedInsertGiayTo,
            };
            UpdateSummary = new MotoSyncExecuteSummaryDto
            {
                Direction = plan.Direction,
                SyncMode = MotoSyncMode.INSERT_AND_UPDATE,
                SourceProfileCode = plan.SourceProfileCode,
                TargetProfileCode = plan.TargetProfileCode,
                InsertedNguoiLX = plan.PlannedInsertNguoiLX,
                InsertedNguoiLXHoSo = plan.PlannedInsertNguoiLXHoSo,
                InsertedGiayTo = plan.PlannedInsertGiayTo,
                UpdatedNguoiLX = plan.PlannedUpdateNguoiLX,
                UpdatedNguoiLXHoSo = plan.PlannedUpdateNguoiLXHoSo,
                UpdatedRows = plan.PlannedUpdate,
                DeletedRows = 0,
            };
        }

        public int PlanCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        public int UpdateExecuteCalls { get; private set; }
        public MotoSyncExecuteSummaryDto ExecuteSummary { get; init; }
        public MotoSyncExecuteSummaryDto UpdateSummary { get; init; }

        public Task<MotoSyncPlanDto> BuildPlanAsync(
            MotoSyncPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            PlanCalls++;
            return Task.FromResult(new MotoSyncPlanDto
            {
                Direction = request.Direction,
                SourceProfileCode = request.SourceProfileCode,
                TargetProfileCode = request.TargetProfileCode,
                MaKhoaHoc = request.MaKhoaHoc,
                AllowDirtyData = request.AllowDirtyData,
                SourceRows = _plan.SourceRows,
                TargetRows = _plan.TargetRows,
                ExactMaDkOverlap = _plan.ExactMaDkOverlap,
                SourceOnly = _plan.SourceOnly,
                TargetOnly = _plan.TargetOnly,
                DuplicateBusinessKeyGroups = _plan.DuplicateBusinessKeyGroups,
                ShortFullMaDkPairs = _plan.ShortFullMaDkPairs,
                MissingKhoaHocDependencies = _plan.MissingKhoaHocDependencies,
                PlannedInsertNguoiLX = _plan.PlannedInsertNguoiLX,
                PlannedInsertNguoiLXHoSo = _plan.PlannedInsertNguoiLXHoSo,
                PlannedInsertGiayTo = _plan.PlannedInsertGiayTo,
                PlannedUpdate = _plan.PlannedUpdate,
                PlannedUpdateNguoiLX = _plan.PlannedUpdateNguoiLX,
                PlannedUpdateNguoiLXHoSo = _plan.PlannedUpdateNguoiLXHoSo,
                UpdateSamples = _plan.UpdateSamples,
                Executable = _plan.Executable,
                Blockers = _plan.Blockers,
                Warnings = _plan.Warnings,
                Errors = _plan.Errors,
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
}

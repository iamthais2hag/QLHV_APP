using System.Text.Json;
using Microsoft.Extensions.Options;
using QLHV.Application.HocVien.Photos;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Tests.Sync;

public sealed class QlhvImportServiceTests
{
    [Fact]
    public async Task Plan_uses_actual_profile_identity_and_reports_counts_without_writing()
    {
        var sourceRows = new[] { Source("66029-001"), Source("66029-002") };
        var reads = new FakeReadRepository
        {
            Source = new QlhvImportSourceSnapshot
            {
                HocVienRows = sourceRows,
                KhoaHocRows = 1,
            },
            Target = new QlhvImportTargetSnapshot
            {
                CurrentAppHocVienRows = 1,
                AppKhoaHocRows = 4,
                HocVienRows = new[]
                {
                    new QlhvFullSyncTargetRow("66029-002", "old-hash", IsDeleted: false),
                },
            },
        };
        var target = new FakeTargetRepository();
        var service = CreateService(reads, target);

        var plan = await service.GetPlanAsync(new QlhvImportRequest
        {
            SourceProfileCode = " csdt_oto ",
            MaCSDT = "66029",
        });

        Assert.True(plan.IsReadOnly);
        Assert.True(plan.Executable);
        Assert.Equal("CSDT_OTO", plan.SourceProfileCode);
        Assert.Equal(2, plan.SourceHocVienRows);
        Assert.Equal(1, plan.SourceKhoaHocRows);
        Assert.Equal(1, plan.CurrentAppHocVienRows);
        Assert.Equal(4, plan.CurrentAppKhoaHocRows);
        Assert.Equal(1, plan.PlannedInsertHocVienRows);
        Assert.Equal(1, plan.PlannedUpdateHocVienRows);
        Assert.Equal(2, plan.PlannedUpsertHocVienRows);
        Assert.Equal(1, plan.PlannedUpsertKhoaHocRows);
        Assert.DoesNotContain(QlhvImportService.AppKhoaHocNotSupportedWarning, plan.Warnings);
        Assert.Equal(2, plan.HocVien.SourceRows);
        Assert.Equal(1, plan.KhoaHoc.SourceRows);
        Assert.Equal(1, plan.KhoaHoc.Insert);
        Assert.Equal(0, plan.GiaoVien.SourceRows);
        Assert.Equal(0, plan.DuplicateSourceKeys);
        Assert.Equal(0, plan.RelationConflicts);
        Assert.Equal(0, target.UpsertCalls);
    }

    [Fact]
    public async Task Plan_requires_ma_csdt_before_any_repository_read()
    {
        var reads = new FakeReadRepository();
        var target = new FakeTargetRepository();
        var service = CreateService(reads, target);

        var plan = await service.GetPlanAsync(new QlhvImportRequest
        {
            SourceProfileCode = "CSDT_OTO",
            MaCSDT = " ",
        });

        Assert.False(plan.Executable);
        Assert.Contains("MaCSDT la bat buoc.", plan.Blockers);
        Assert.Equal(0, reads.SourceReads);
        Assert.Equal(0, target.DiagnosticsReads);
    }

    [Fact]
    public async Task Plan_rejects_course_filter_before_any_repository_read()
    {
        var reads = new FakeReadRepository();
        var target = new FakeTargetRepository();
        var service = CreateService(reads, target);

        var plan = await service.GetPlanAsync(new QlhvImportRequest
        {
            SourceProfileCode = "CSDT_OTO",
            MaCSDT = "66029",
            MaKhoaHoc = "66029K01",
        });

        Assert.False(plan.Executable);
        Assert.Contains(plan.Blockers, blocker => blocker.Contains("maKhoaHoc phai de trong", StringComparison.Ordinal));
        Assert.Equal(0, reads.SourceReads);
        Assert.Equal(0, target.DiagnosticsReads);
    }

    [Fact]
    public async Task Plan_blocks_source_database_that_is_not_expected_bak_database()
    {
        var reads = new FakeReadRepository
        {
            Source = new QlhvImportSourceSnapshot
            {
                SourceDatabaseName = "CSDL_OTO",
                HocVienRows = new[] { Source("66029-001") },
            },
        };
        var service = CreateService(reads, new FakeTargetRepository());

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.False(plan.Executable);
        Assert.Contains(plan.Blockers, blocker => blocker.Contains("bat buoc phai la CSDL_OTO_BAK", StringComparison.Ordinal));
        Assert.Empty(plan.ExecutableDomains);
    }

    [Fact]
    public async Task Plan_blocks_empty_moto_source()
    {
        var reads = new FakeReadRepository
        {
            Source = new QlhvImportSourceSnapshot(),
            Target = new QlhvImportTargetSnapshot(),
        };
        var target = new FakeTargetRepository();
        var service = CreateService(reads, target);

        var plan = await service.GetPlanAsync(new QlhvImportRequest
        {
            SourceProfileCode = "CSDT_MOTO",
            MaCSDT = "66030",
        });

        Assert.False(plan.Executable);
        Assert.Equal(0, plan.SourceHocVienRows);
        Assert.Equal(0, plan.PlannedUpsertHocVienRows);
        Assert.Contains(
            plan.HocVienBlockers,
            blocker => blocker.Contains("0 hoc vien", StringComparison.Ordinal));
        Assert.Equal(0, target.UpsertCalls);
    }

    [Fact]
    public async Task Plan_does_not_open_bak_when_source_operation_lock_is_busy()
    {
        var reads = OneOtoRow(new QlhvImportTargetSnapshot());
        var operationLock = new FakeOperationLock { ShouldAcquire = false };
        var service = CreateService(
            reads,
            new FakeTargetRepository(),
            operationLock: operationLock);

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.False(plan.Executable);
        Assert.Contains(plan.Blockers, blocker => blocker.Contains("dang refresh BAK hoac full sync", StringComparison.Ordinal));
        Assert.Equal(1, operationLock.AcquireCalls);
        Assert.Equal(0, reads.SourceReads);
        Assert.Equal(0, reads.TargetReads);
    }

    [Fact]
    public async Task Execute_rejects_unknown_source_before_any_read_or_write()
    {
        var reads = new FakeReadRepository();
        var target = new FakeTargetRepository();
        var service = CreateService(reads, target, dryRun: false, enableWrites: true);

        var result = await service.ExecuteAsync(new QlhvImportExecuteRequest
        {
            SourceProfileCode = "CUSTOM",
            MaCSDT = "66029",
        });

        Assert.False(result.Executed);
        Assert.Equal(0, reads.SourceReads);
        Assert.Equal(0, target.DiagnosticsReads);
        Assert.Equal(0, target.UpsertCalls);
    }

    [Fact]
    public async Task Execute_does_not_write_when_target_compatibility_is_blocked()
    {
        var reads = new FakeReadRepository
        {
            Source = new QlhvImportSourceSnapshot
            {
                HocVienRows = new[] { Source("66029-001") },
            },
            Target = new QlhvImportTargetSnapshot
            {
                SourceProfileConstraintExists = true,
                SourceProfileAllowedByConstraint = false,
            },
        };
        var target = new FakeTargetRepository();
        var service = CreateService(reads, target, dryRun: false, enableWrites: true);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.False(result.Executed);
        Assert.Contains(
            result.Plan.HocVienBlockers,
            blocker => blocker.Contains("CHECK constraint", StringComparison.Ordinal));
        Assert.Equal(0, target.UpsertCalls);
    }

    [Fact]
    public async Task Diagnostics_oto_reports_read_only_source_and_target_safety_counts()
    {
        var reads = new FakeReadRepository
        {
            Source = new QlhvImportSourceSnapshot
            {
                HocVienRows = new[] { Source("66029-001"), Source("66029-002") },
                KhoaHocRows = 1,
            },
            Target = new QlhvImportTargetSnapshot
            {
                CurrentAppHocVienRows = 1,
                TargetRowsForSourceProfile = 5,
                TargetExactIdentityMatches = 1,
                SourceProfileConstraintExists = true,
                SourceProfileAllowedByConstraint = true,
            },
        };
        var target = new FakeTargetRepository();
        var service = CreateService(reads, target);

        var diagnostics = await service.GetDiagnosticsAsync(new QlhvImportRequest
        {
            SourceProfileCode = "CSDT_OTO",
            MaCSDT = "66029",
        });

        Assert.True(diagnostics.IsReadOnly);
        Assert.Equal("CSDT_OTO", diagnostics.SourceProfileCode);
        Assert.Equal(2, diagnostics.SourceHocVienRows);
        Assert.Equal(2, diagnostics.SourceDistinctMaDkRows);
        Assert.Equal(0, diagnostics.DuplicateSourceMaDkRows);
        Assert.Equal(1, diagnostics.CurrentAppHocVienRows);
        Assert.Equal(5, diagnostics.TargetRowsForSourceProfile);
        Assert.Equal(1, diagnostics.TargetExactIdentityMatches);
        Assert.True(diagnostics.SourceProfileAllowedByConstraint);
        Assert.Equal(2, diagnostics.HocVien.SourceRows);
        Assert.Equal(1, diagnostics.KhoaHoc.SourceRows);
        Assert.Equal(0, diagnostics.GiaoVien.SourceRows);
        Assert.Equal(0, diagnostics.DuplicateSourceKeys);
        Assert.Equal(0, diagnostics.RelationConflicts);
        Assert.Empty(diagnostics.Blockers);
        Assert.Equal(0, target.UpsertCalls);
    }

    [Fact]
    public async Task Diagnostics_moto_reports_empty_source_as_blocker()
    {
        var reads = new FakeReadRepository();
        var target = new FakeTargetRepository();
        var service = CreateService(reads, target);

        var diagnostics = await service.GetDiagnosticsAsync(new QlhvImportRequest
        {
            SourceProfileCode = "CSDT_MOTO",
            MaCSDT = "66030",
        });

        Assert.True(diagnostics.IsReadOnly);
        Assert.Equal("CSDT_MOTO", diagnostics.SourceProfileCode);
        Assert.Equal(0, diagnostics.SourceHocVienRows);
        Assert.Equal(0, diagnostics.SourceDistinctMaDkRows);
        Assert.Equal(0, diagnostics.DuplicateSourceMaDkRows);
        Assert.False(diagnostics.Executable);
        Assert.Contains(
            diagnostics.HocVienBlockers,
            blocker => blocker.Contains("0 hoc vien", StringComparison.Ordinal));
        Assert.Equal(0, target.UpsertCalls);
    }

    [Fact]
    public async Task Duplicate_source_ma_dk_is_a_plan_and_diagnostics_blocker()
    {
        var reads = new FakeReadRepository
        {
            Source = new QlhvImportSourceSnapshot
            {
                HocVienRows = new[]
                {
                    Source("66029-001"),
                    Source("66029-001"),
                    Source("66029-001"),
                },
            },
        };
        var target = new FakeTargetRepository();
        var service = CreateService(reads, target);

        var diagnostics = await service.GetDiagnosticsAsync(new QlhvImportRequest
        {
            SourceProfileCode = "CSDT_OTO",
            MaCSDT = "66029",
        });

        Assert.Equal(3, diagnostics.SourceHocVienRows);
        Assert.Equal(1, diagnostics.SourceDistinctMaDkRows);
        Assert.Equal(1, diagnostics.DuplicateSourceMaDkRows);
        Assert.Contains(
            diagnostics.HocVienBlockers,
            blocker => blocker.Contains("MaDK bi trung", StringComparison.Ordinal));
        Assert.Equal(0, target.UpsertCalls);
    }

    [Fact]
    public async Task Other_profile_ma_dk_collision_is_informational_for_composite_identity()
    {
        var reads = OneOtoRow(new QlhvImportTargetSnapshot
        {
            TargetMaDkConflictsOtherProfiles = 1,
        });
        var service = CreateService(reads, new FakeTargetRepository());

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.Empty(plan.Blockers);
        Assert.Contains(plan.Warnings, warning => warning.Contains("profile khac", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Soft_deleted_identity_is_planned_for_reactivation()
    {
        var reads = OneOtoRow(new QlhvImportTargetSnapshot
        {
            SoftDeletedIdentityConflicts = 1,
            HocVienRows = new[]
            {
                new QlhvFullSyncTargetRow("66029-001", "same-or-old-hash", IsDeleted: true),
            },
        });
        var service = CreateService(reads, new FakeTargetRepository());

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.Empty(plan.Blockers);
        Assert.Equal(1, plan.PlannedReactivateHocVienRows);
    }

    [Fact]
    public async Task Constraint_that_disallows_oto_is_a_blocker()
    {
        var reads = OneOtoRow(new QlhvImportTargetSnapshot
        {
            SourceProfileConstraintExists = true,
            SourceProfileAllowedByConstraint = false,
        });
        var service = CreateService(reads, new FakeTargetRepository());

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.Contains(
            plan.HocVienBlockers,
            blocker => blocker.Contains("khong cho phep CSDT_OTO", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Constraint_that_allows_oto_has_no_constraint_blocker()
    {
        var reads = OneOtoRow(new QlhvImportTargetSnapshot
        {
            SourceProfileConstraintExists = true,
            SourceProfileAllowedByConstraint = true,
        });
        var service = CreateService(reads, new FakeTargetRepository());

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.DoesNotContain(plan.Blockers, blocker => blocker.Contains("CHECK constraint", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Plan_skips_optional_assignment_that_does_not_reference_teacher_in_same_snapshot()
    {
        var reads = new FakeReadRepository
        {
            Source = new QlhvImportSourceSnapshot
            {
                HocVienRows = new[] { Source("66029-001") },
                KhoaHocGiaoVienRows = new[]
                {
                    new QlhvKhoaHocGiaoVienSourceRow
                    {
                        MaLichLV = 1, MaKH = "66029K01", MaGV = "GV-MISSING",
                        TenGV = "Khong co", TrangThai = true, IsKhoaHocGiaoVien = true,
                    },
                },
            },
        };
        var service = CreateService(reads, new FakeTargetRepository());

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.True(plan.Executable);
        Assert.Equal(1, plan.RelationConflicts);
        Assert.Empty(plan.Blockers);
        Assert.Empty(plan.HocVienBlockers);
        Assert.Contains(
            plan.RelationBlockers,
            blocker => blocker.Contains("quan he", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(QlhvImportDomains.HocVien, plan.ExecutableDomains);
        Assert.Contains(QlhvImportDomains.Relation, plan.SkippedDomains);
    }

    [Fact]
    public async Task Plan_keeps_hoc_vien_executable_when_all_optional_target_schemas_are_missing()
    {
        var reads = OneOtoRow(new QlhvImportTargetSnapshot
        {
            KhoaHocBlockers = ["Target thieu bang dbo.App_KhoaHoc."],
            GiaoVienBlockers = ["Target thieu bang dbo.App_GiaoVien."],
            RelationBlockers = ["Target thieu bang dbo.App_KhoaHoc_GiaoVien."],
        });
        var service = CreateService(reads, new FakeTargetRepository());

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.True(plan.Executable);
        Assert.Empty(plan.Blockers);
        Assert.Empty(plan.HocVienBlockers);
        Assert.Equal(QlhvImportDomainStatuses.Executable, plan.HocVienStatus);
        Assert.Equal(QlhvImportDomainStatuses.SkippedSchemaNotReady, plan.KhoaHocStatus);
        Assert.Equal(QlhvImportDomainStatuses.SkippedSchemaNotReady, plan.GiaoVienStatus);
        Assert.Equal(QlhvImportDomainStatuses.SkippedSchemaNotReady, plan.RelationStatus);
        Assert.Equal(new[] { QlhvImportDomains.HocVien }, plan.ExecutableDomains);
        Assert.Equal(
            new[]
            {
                QlhvImportDomains.KhoaHoc,
                QlhvImportDomains.GiaoVien,
                QlhvImportDomains.Relation,
            },
            plan.SkippedDomains);
        Assert.Contains(
            plan.OptionalWarnings,
            warning => warning.Contains("App_KhoaHoc", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Plan_reports_missing_target_control_schema_as_global_blocker()
    {
        var reads = OneOtoRow(new QlhvImportTargetSnapshot());
        reads.TargetException = new QlhvImportGlobalBlockerException(
            "Target QLHV_APP thieu schema control bat buoc cho full sync.");
        var service = CreateService(reads, new FakeTargetRepository());

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.False(plan.Executable);
        Assert.Contains(
            plan.Blockers,
            blocker => blocker.Contains("schema control", StringComparison.Ordinal));
        Assert.Empty(plan.HocVienBlockers);
        Assert.Empty(plan.ExecutableDomains);
        Assert.Equal(QlhvImportDomainStatuses.Blocked, plan.HocVienStatus);
    }

    [Fact]
    public async Task Plan_marks_zero_row_optional_domain_as_skipped_source_not_ready()
    {
        var reads = new FakeReadRepository
        {
            Source = new QlhvImportSourceSnapshot
            {
                HocVienRows =
                [
                    new V2HocVienSourceRow
                    {
                        MaDK = "66029-001",
                        HangDaoTao = "B2",
                        TenHangDT = "Hang B2",
                        HoVaTen = "Nguyen Van A",
                        NgaySinh = new DateTime(1990, 1, 2),
                        SoCMT = "001234567890",
                        GioiTinh = "M",
                    },
                ],
            },
        };
        var service = CreateService(reads, new FakeTargetRepository());

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.True(plan.Executable);
        Assert.Equal(QlhvImportDomainStatuses.SkippedSourceNotReady, plan.KhoaHocStatus);
        Assert.Contains(
            plan.KhoaHocBlockers,
            blocker => blocker.Contains("0 dong", StringComparison.Ordinal));
        Assert.DoesNotContain(QlhvImportDomains.KhoaHoc, plan.ExecutableDomains);
    }

    [Fact]
    public async Task Plan_marks_optional_mapping_failure_as_skipped_source_not_ready()
    {
        var reads = new FakeReadRepository
        {
            Source = new QlhvImportSourceSnapshot
            {
                HocVienRows = [Source("66029-001")],
                KhoaHocSourceRows =
                [
                    new QlhvKhoaHocSourceRow
                    {
                        MaKH = " ",
                        MaCSDT = "66029",
                        TrangThai = true,
                    },
                ],
            },
        };
        var service = CreateService(reads, new FakeTargetRepository());

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.True(plan.Executable);
        Assert.Equal(QlhvImportDomainStatuses.SkippedSourceNotReady, plan.KhoaHocStatus);
        Assert.Contains(
            plan.KhoaHocBlockers,
            blocker => blocker.Contains("thieu MaKH", StringComparison.Ordinal));
        Assert.DoesNotContain(QlhvImportDomains.KhoaHoc, plan.ExecutableDomains);
    }

    [Fact]
    public async Task Plan_marks_optional_duplicate_source_keys_as_skipped_source_not_ready()
    {
        var course = new QlhvKhoaHocSourceRow
        {
            MaKH = "66029K01",
            MaCSDT = "66029",
            TenKH = "Khoa OTO",
            TrangThai = true,
        };
        var reads = new FakeReadRepository
        {
            Source = new QlhvImportSourceSnapshot
            {
                HocVienRows = [Source("66029-001")],
                KhoaHocSourceRows = [course, course],
            },
        };
        var service = CreateService(reads, new FakeTargetRepository());

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.True(plan.Executable);
        Assert.Equal(QlhvImportDomainStatuses.SkippedSourceNotReady, plan.KhoaHocStatus);
        Assert.Contains(
            plan.KhoaHocBlockers,
            blocker => blocker.Contains("SourceMaKhoaHoc bi trung", StringComparison.Ordinal));
        Assert.DoesNotContain(QlhvImportDomains.KhoaHoc, plan.ExecutableDomains);
    }

    [Fact]
    public async Task Plan_keeps_target_hoc_vien_identity_conflict_as_required_blocker()
    {
        var reads = OneOtoRow(new QlhvImportTargetSnapshot
        {
            DuplicateHocVienTargetIdentityRows = 1,
        });
        var target = new FakeTargetRepository();
        var service = CreateService(reads, target);

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.False(plan.Executable);
        Assert.Empty(plan.Blockers);
        Assert.Contains(
            plan.HocVienBlockers,
            blocker => blocker.Contains("HocVien", StringComparison.Ordinal) &&
                       blocker.Contains("identity bi trung", StringComparison.Ordinal));
        Assert.DoesNotContain(QlhvImportDomains.HocVien, plan.ExecutableDomains);
        Assert.Equal(0, target.UpsertCalls);
    }

    [Fact]
    public async Task Execute_upserts_with_actual_source_profile_and_source_ma_dk()
    {
        var reads = new FakeReadRepository
        {
            Source = new QlhvImportSourceSnapshot
            {
                HocVienRows = new[] { Source("66029-001") },
                KhoaHocRows = 1,
            },
            Target = new QlhvImportTargetSnapshot(),
        };
        var target = new FakeTargetRepository
        {
            FullSyncResult = new QlhvImportFullSyncWriteResult(1, 0, 0, 0, 0, 0, 0, 0),
        };
        var service = CreateService(reads, target, dryRun: false, enableWrites: true);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.True(result.Executed);
        Assert.Equal(1, result.InsertedHocVienRows);
        Assert.Equal(1, target.UpsertCalls);
        var model = Assert.Single(target.WrittenRows);
        Assert.Equal("CSDT_OTO", model.SourceProfileCode);
        Assert.Equal("66029-001", model.SourceMaDK);
        Assert.Equal("66029-001", model.MaDK);
    }

    [Fact]
    public async Task Execute_reports_partial_success_when_optional_domain_fails_after_hoc_vien_commits()
    {
        var history = new FakeOperationHistoryRepository();
        var hocVienCounts = new QlhvEntityWriteCounts(1, 1, 0, 0, 0, 0);
        var target = new FakeTargetRepository
        {
            FullSyncResult = new QlhvImportFullSyncWriteResult(
                QlhvEntityWriteCounts.Empty,
                QlhvEntityWriteCounts.Empty,
                QlhvEntityWriteCounts.Empty,
                hocVienCounts,
                0, 0, 0, 0, 0, 0)
            {
                DomainResults =
                [
                    new QlhvDomainWriteResult(
                        QlhvImportDomains.KhoaHoc,
                        QlhvImportDomainStatuses.Failed,
                        "fixture optional failure",
                        QlhvEntityWriteCounts.Empty),
                    new QlhvDomainWriteResult(
                        QlhvImportDomains.HocVien,
                        QlhvImportDomainStatuses.Succeeded,
                        null,
                        hocVienCounts),
                ],
            },
        };
        var service = CreateService(
            OneOtoRow(new QlhvImportTargetSnapshot()),
            target,
            dryRun: false,
            enableWrites: true,
            operationHistory: history);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.True(result.Executed);
        Assert.Equal(QlhvImportOverallStatuses.PartialSuccess, result.Status);
        Assert.Equal(1, result.InsertedHocVienRows);
        Assert.Equal(1, result.HocVien.Insert);
        Assert.Contains(
            result.Plan.Warnings,
            warning => warning.Contains("fixture optional failure", StringComparison.Ordinal));
        Assert.Contains(
            result.DomainResults,
            domain => domain.Domain == QlhvImportDomains.KhoaHoc &&
                      domain.Status == QlhvImportDomainStatuses.Failed);
        Assert.Contains(
            result.DomainResults,
            domain => domain.Domain == QlhvImportDomains.HocVien &&
                      domain.Status == QlhvImportDomainStatuses.Succeeded);
        Assert.Equal(QlhvOperationTypes.PartialSuccess, Assert.Single(history.Completed).Value.Status);
    }

    [Fact]
    public async Task Execute_does_not_report_partial_when_optional_domains_were_not_requested()
    {
        var history = new FakeOperationHistoryRepository();
        var khoaHocCounts = new QlhvEntityWriteCounts(1, 0, 0, 0, 0, 1);
        var hocVienCounts = new QlhvEntityWriteCounts(1, 0, 0, 0, 0, 1);
        var target = new FakeTargetRepository
        {
            FullSyncResult = new QlhvImportFullSyncWriteResult(
                khoaHocCounts,
                QlhvEntityWriteCounts.Empty,
                QlhvEntityWriteCounts.Empty,
                hocVienCounts,
                0, 0, 0, 0, 0, 0)
            {
                DomainResults =
                [
                    new QlhvDomainWriteResult(
                        QlhvImportDomains.KhoaHoc,
                        QlhvImportDomainStatuses.NoOp,
                        "fixture no change",
                        khoaHocCounts),
                    new QlhvDomainWriteResult(
                        QlhvImportDomains.GiaoVien,
                        QlhvImportDomainStatuses.SkippedNotRequested,
                        "fixture empty optional source",
                        QlhvEntityWriteCounts.Empty),
                    new QlhvDomainWriteResult(
                        QlhvImportDomains.Relation,
                        QlhvImportDomainStatuses.SkippedNotRequested,
                        "fixture dependency not requested",
                        QlhvEntityWriteCounts.Empty),
                    new QlhvDomainWriteResult(
                        QlhvImportDomains.HocVien,
                        QlhvImportDomainStatuses.NoOp,
                        "fixture no change",
                        hocVienCounts),
                ],
            },
        };
        var service = CreateService(
            OneOtoRow(new QlhvImportTargetSnapshot()),
            target,
            dryRun: false,
            enableWrites: true,
            operationHistory: history);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.True(result.Executed);
        Assert.Equal(QlhvImportOverallStatuses.NoOp, result.Status);
        Assert.Equal(QlhvOperationTypes.Succeeded, Assert.Single(history.Completed).Value.Status);
        Assert.Equal(2, result.SkippedReasons.NoChange);
        Assert.Equal(0, result.SkippedReasons.NotRequested);
        Assert.All(
            result.DomainResults.Where(domain =>
                domain.Status == QlhvImportDomainStatuses.SkippedNotRequested),
            domain =>
            {
                Assert.False(domain.Requested);
                Assert.False(domain.ContributesToPartial);
                Assert.Null(domain.FailureCode);
            });
    }

    [Fact]
    public async Task Execute_reports_and_audits_failed_hoc_vien_after_optional_domain_commits()
    {
        var history = new FakeOperationHistoryRepository();
        var khoaHocCounts = new QlhvEntityWriteCounts(1, 1, 0, 0, 0, 0);
        var hocVienCounts = new QlhvEntityWriteCounts(1, 0, 0, 0, 0, 1);
        var target = new FakeTargetRepository
        {
            FullSyncResult = new QlhvImportFullSyncWriteResult(
                khoaHocCounts,
                QlhvEntityWriteCounts.Empty,
                QlhvEntityWriteCounts.Empty,
                hocVienCounts,
                0, 0, 0, 0, 0, 0)
            {
                DomainResults =
                [
                    new QlhvDomainWriteResult(
                        QlhvImportDomains.KhoaHoc,
                        QlhvImportDomainStatuses.Succeeded,
                        null,
                        khoaHocCounts),
                    new QlhvDomainWriteResult(
                        QlhvImportDomains.HocVien,
                        QlhvImportDomainStatuses.Failed,
                        "fixture hoc vien transaction failed",
                        hocVienCounts),
                ],
            },
        };
        var service = CreateService(
            OneOtoRow(new QlhvImportTargetSnapshot()),
            target,
            dryRun: false,
            enableWrites: true,
            operationHistory: history);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.True(result.Executed);
        Assert.Equal(QlhvImportOverallStatuses.Failed, result.Status);
        Assert.Equal(1, result.KhoaHoc.SourceRows);
        Assert.Equal(1, result.KhoaHoc.Insert);
        Assert.Equal(1, result.HocVien.SourceRows);
        Assert.Equal(1, result.HocVien.Skip);
        Assert.Equal(2, result.DomainResults.Count);
        Assert.Contains(
            result.DomainResults,
            domain => domain.Domain == QlhvImportDomains.KhoaHoc &&
                      domain.Status == QlhvImportDomainStatuses.Succeeded &&
                      domain.Counts.Insert == 1);
        Assert.Contains(
            result.DomainResults,
            domain => domain.Domain == QlhvImportDomains.HocVien &&
                      domain.Status == QlhvImportDomainStatuses.Failed &&
                      domain.Counts.Skip == 1);
        Assert.True(target.FullSyncTokenCaptured);
        Assert.False(target.LastFullSyncCancellationToken.CanBeCanceled);

        var completion = Assert.Single(history.Completed).Value;
        Assert.Equal(QlhvOperationTypes.Failed, completion.Status);
        Assert.Equal(2, completion.SourceRows);
        Assert.Equal(1, completion.InsertedRows);
        Assert.Equal(1, completion.SkippedRows);
        Assert.Equal("fixture hoc vien transaction failed", completion.ErrorMessage);
        Assert.NotNull(completion.DetailJson);
        using var detail = JsonDocument.Parse(completion.DetailJson);
        Assert.Equal(
            QlhvImportOverallStatuses.Failed,
            detail.RootElement.GetProperty("OverallStatus").GetString());
        var auditedDomains = detail.RootElement
            .GetProperty("DomainResults")
            .EnumerateArray()
            .ToArray();
        Assert.Contains(
            auditedDomains,
            domain => domain.GetProperty("Domain").GetString() == QlhvImportDomains.KhoaHoc &&
                      domain.GetProperty("Status").GetString() == QlhvImportDomainStatuses.Succeeded &&
                      domain.GetProperty("Counts").GetProperty("Inserted").GetInt32() == 1);
        Assert.Contains(
            auditedDomains,
            domain => domain.GetProperty("Domain").GetString() == QlhvImportDomains.HocVien &&
                      domain.GetProperty("Status").GetString() == QlhvImportDomainStatuses.Failed &&
                      domain.GetProperty("Counts").GetProperty("Skipped").GetInt32() == 1);
    }

    [Fact]
    public async Task Execute_writes_the_complete_import_in_one_repository_transaction()
    {
        var reads = new FakeReadRepository
        {
            Source = new QlhvImportSourceSnapshot
            {
                HocVienRows = new[] { Source("66029-001"), Source("66029-002") },
            },
        };
        var target = new FakeTargetRepository
        {
            FullSyncResult = new QlhvImportFullSyncWriteResult(2, 0, 0, 0, 0, 0, 0, 0),
        };
        var service = CreateService(reads, target, dryRun: false, enableWrites: true, batchSize: 1);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.True(result.Executed);
        Assert.Equal(2, result.InsertedHocVienRows);
        Assert.Equal(1, target.UpsertCalls);
        Assert.Equal(2, target.WrittenRows.Count);
    }

    [Fact]
    public async Task Execute_sends_course_teacher_relation_and_student_as_one_payload_and_history_has_group_counts()
    {
        var history = new FakeOperationHistoryRepository();
        var reads = new FakeReadRepository
        {
            Source = new QlhvImportSourceSnapshot
            {
                HocVienRows = new[] { Source("66029-001") },
                KhoaHocSourceRows = new[]
                {
                    new QlhvKhoaHocSourceRow
                    {
                        MaKH = "66029K01", MaCSDT = "66029", MaSoGTVT = "66029",
                        TenKH = "Khoa OTO", TrangThai = true,
                    },
                },
                GiaoVienRows = new[]
                {
                    new QlhvGiaoVienSourceRow
                    {
                        MaGV = "GV000001", MaCSDT = "66029", MaSoGTVT = "66029",
                        HoTenDem = "Nguyen Van", TenGV = "An", NgaySinh = "19800102",
                        SoCMT = "001234567890", NgayCapGPLX = new DateTime(2020, 1, 1),
                        TrangThai = true,
                    },
                },
                KhoaHocGiaoVienRows = new[]
                {
                    new QlhvKhoaHocGiaoVienSourceRow
                    {
                        MaLichLV = 7, MaKH = "66029K01", MaGV = "GV000001",
                        TenGV = "Nguyen Van An", TrangThai = true,
                        IsKhoaHocGiaoVien = true,
                    },
                },
            },
        };
        var target = new FakeTargetRepository
        {
            FullSyncResult = new QlhvImportFullSyncWriteResult(
                new QlhvEntityWriteCounts(1, 1, 0, 0, 0, 0),
                new QlhvEntityWriteCounts(1, 1, 0, 0, 0, 0),
                new QlhvEntityWriteCounts(1, 1, 0, 0, 0, 0),
                new QlhvEntityWriteCounts(1, 1, 0, 0, 0, 0),
                0, 0, 0, 0, 0, 0),
        };
        var service = CreateService(
            reads,
            target,
            dryRun: false,
            enableWrites: true,
            operationHistory: history);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.True(result.Executed);
        var payload = Assert.IsType<QlhvImportFullSyncPayload>(target.LastPayload);
        Assert.Single(payload.KhoaHocRows);
        Assert.Single(payload.GiaoVienRows);
        var relation = Assert.Single(payload.RelationRows);
        Assert.Single(payload.HocVienRows);
        Assert.Equal("66029K01", relation.SourceMaKhoaHoc);
        Assert.Equal("CSDT_OTO:GV000001", relation.MaGV);
        Assert.Equal(1, result.KhoaHoc.Insert);
        Assert.Equal(1, result.GiaoVien.Insert);

        var completion = Assert.Single(history.Completed).Value;
        Assert.NotNull(completion.DetailJson);
        Assert.Contains("KhoaHocGiaoVien", completion.DetailJson, StringComparison.Ordinal);
        Assert.Equal(4, completion.SourceRows);
        Assert.Equal(4, completion.InsertedRows);
    }

    [Fact]
    public async Task Execute_moto_stages_only_the_moto_logical_partition()
    {
        var reads = new FakeReadRepository
        {
            Source = new QlhvImportSourceSnapshot
            {
                HocVienRows = new[] { Source("66030-001") },
            },
        };
        var target = new FakeTargetRepository
        {
            FullSyncResult = new QlhvImportFullSyncWriteResult(1, 0, 0, 0, 0, 0, 0, 0),
        };
        var service = CreateService(reads, target, dryRun: false, enableWrites: true);

        var result = await service.ExecuteAsync(new QlhvImportExecuteRequest
        {
            SourceProfileCode = "CSDT_MOTO",
            MaCSDT = "66030",
            ExpectedSnapshotToken = "snapshot-token",
        });

        Assert.True(result.Executed);
        Assert.Equal("CSDT_MOTO", target.LastFullSyncProfile);
        var model = Assert.Single(target.WrittenRows);
        Assert.Equal("CSDT_MOTO", model.SourceProfileCode);
        Assert.Equal("66030-001", model.SourceMaDK);
        Assert.DoesNotContain(target.WrittenRows, row => row.SourceProfileCode == "CSDT_OTO");
    }

    [Fact]
    public async Task Execute_rejects_duplicate_target_identity_rechecked_inside_write_transaction()
    {
        var reads = OneOtoRow(new QlhvImportTargetSnapshot());
        var target = new FakeTargetRepository
        {
            FullSyncResult = new QlhvImportFullSyncWriteResult(0, 0, 0, 0, 0, 0, 0, 1),
        };
        var service = CreateService(reads, target, dryRun: false, enableWrites: true);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.False(result.Executed);
        Assert.Contains(
            result.Plan.Blockers,
            blocker => blocker.Contains("identity target bi trung", StringComparison.Ordinal));
        Assert.Equal(1, target.UpsertCalls);
        Assert.Empty(target.WrittenRows);
    }

    [Fact]
    public async Task Execute_acquires_cross_process_lock_before_creating_running_history()
    {
        var events = new List<string>();
        var operationLock = new FakeOperationLock { OnAcquire = () => events.Add("lock") };
        var history = new FakeOperationHistoryRepository { OnTryCreate = () => events.Add("history") };
        var target = new FakeTargetRepository
        {
            FullSyncResult = new QlhvImportFullSyncWriteResult(1, 0, 0, 0, 0, 0, 0, 0),
        };
        var service = CreateService(
            OneOtoRow(new QlhvImportTargetSnapshot()),
            target,
            dryRun: false,
            enableWrites: true,
            operationLock: operationLock,
            operationHistory: history);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.True(result.Executed);
        Assert.Equal(new[] { "lock", "history" }, events.Take(2));
        Assert.Equal(1, operationLock.AcquireCalls);
        var created = Assert.Single(history.Created);
        Assert.Equal(QlhvOperationTypes.Running, created.Status);
        Assert.NotNull(created.StartedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("stale-token")]
    public async Task Execute_rejects_missing_or_stale_snapshot_and_terminalizes_history(string? expectedToken)
    {
        var history = new FakeOperationHistoryRepository();
        var target = new FakeTargetRepository();
        var service = CreateService(
            OneOtoRow(new QlhvImportTargetSnapshot()),
            target,
            dryRun: false,
            enableWrites: true,
            operationHistory: history);
        var request = ExecuteRequest();
        request.ExpectedSnapshotToken = expectedToken;

        var result = await service.ExecuteAsync(request);

        Assert.False(result.Executed);
        Assert.NotNull(result.OperationId);
        Assert.Equal(0, target.UpsertCalls);
        var completion = Assert.Single(history.Completed);
        Assert.Equal(QlhvOperationTypes.Failed, completion.Value.Status);
        Assert.False(completion.Token.CanBeCanceled);
        Assert.Contains(
            result.Plan.Blockers,
            blocker => blocker.Contains(
                expectedToken is null ? "ExpectedSnapshotToken" : "Plan da cu",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Execute_configuration_guards_do_not_lock_or_create_history(
        bool dryRun,
        bool enableWrites)
    {
        var operationLock = new FakeOperationLock();
        var history = new FakeOperationHistoryRepository();
        var service = CreateService(
            OneOtoRow(new QlhvImportTargetSnapshot()),
            new FakeTargetRepository(),
            dryRun,
            enableWrites,
            operationLock: operationLock,
            operationHistory: history);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.False(result.Executed);
        Assert.Equal(0, operationLock.AcquireCalls);
        Assert.Empty(history.Created);
        Assert.Empty(history.Completed);
    }

    [Fact]
    public async Task Execute_lock_conflict_does_not_create_phantom_history_operation()
    {
        var operationLock = new FakeOperationLock { ShouldAcquire = false };
        var history = new FakeOperationHistoryRepository();
        var service = CreateService(
            OneOtoRow(new QlhvImportTargetSnapshot()),
            new FakeTargetRepository(),
            dryRun: false,
            enableWrites: true,
            operationLock: operationLock,
            operationHistory: history);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.False(result.Executed);
        Assert.Null(result.OperationId);
        Assert.Empty(history.Created);
        Assert.Empty(history.Completed);
    }

    [Fact]
    public async Task Execute_reports_committed_data_when_success_history_cannot_be_persisted()
    {
        var history = new FakeOperationHistoryRepository
        {
            CompleteException = new InvalidOperationException("history unavailable"),
        };
        var target = new FakeTargetRepository
        {
            FullSyncResult = new QlhvImportFullSyncWriteResult(1, 0, 0, 0, 0, 0, 0, 0),
        };
        var service = CreateService(
            OneOtoRow(new QlhvImportTargetSnapshot()),
            target,
            dryRun: false,
            enableWrites: true,
            operationHistory: history);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.True(result.Executed);
        Assert.Equal(1, result.InsertedHocVienRows);
        Assert.Contains("da commit", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, target.UpsertCalls);
        Assert.True(history.CompleteCalls >= 2);
    }

    [Fact]
    public async Task Plan_exposes_advisory_photo_counts_without_writing()
    {
        var target = new FakeTargetRepository();
        var photos = new FakePhotoProcessingService
        {
            Plan = new HocVienPhotoPlanDto(
                Found: 1,
                Missing: 2,
                Pending: 3,
                ToReprocess: 4,
                ReviewRequired: 5),
        };
        var service = CreateService(
            OneOtoRow(new QlhvImportTargetSnapshot()),
            target,
            photoProcessing: photos);

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.Equal(photos.Plan, plan.Photo);
        Assert.Equal(1, photos.BuildPlanCalls);
        Assert.Equal(0, photos.QueueCalls);
        Assert.Equal(0, target.UpsertCalls);
    }

    [Fact]
    public async Task Execute_queues_photo_work_only_after_database_full_sync_returns()
    {
        var target = new FakeTargetRepository
        {
            FullSyncResult = new QlhvImportFullSyncWriteResult(1, 0, 0, 0, 0, 0, 0, 0),
        };
        var photos = new FakePhotoProcessingService
        {
            OnQueue = () => Assert.Equal(1, target.UpsertCalls),
            QueueResult = new HocVienPhotoQueueBatchResult(1, 1, 0, 0),
        };
        var service = CreateService(
            OneOtoRow(new QlhvImportTargetSnapshot()),
            target,
            dryRun: false,
            enableWrites: true,
            photoProcessing: photos);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.True(result.Executed);
        Assert.Equal(1, photos.QueueCalls);
        Assert.Equal(photos.QueueResult, result.PhotoQueue);
        var queuedSource = Assert.Single(photos.LastQueuedSources);
        Assert.Equal("CSDT_OTO", queuedSource.SourceProfileCode);
        Assert.Equal("66029-001", queuedSource.SourceMaDK);
    }

    [Fact]
    public async Task Photo_queue_failure_never_changes_the_committed_database_result()
    {
        var target = new FakeTargetRepository
        {
            FullSyncResult = new QlhvImportFullSyncWriteResult(1, 0, 0, 0, 0, 0, 0, 0),
        };
        var photos = new FakePhotoProcessingService
        {
            QueueException = new IOException("fixture"),
        };
        var service = CreateService(
            OneOtoRow(new QlhvImportTargetSnapshot()),
            target,
            dryRun: false,
            enableWrites: true,
            photoProcessing: photos);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.True(result.Executed);
        Assert.Equal(1, target.UpsertCalls);
        Assert.Contains("da commit", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            result.Plan.Warnings,
            warning => warning.Contains("hang doi anh", StringComparison.OrdinalIgnoreCase));
    }

    private static QlhvImportService CreateService(
        FakeReadRepository reads,
        FakeTargetRepository target,
        bool dryRun = true,
        bool enableWrites = false,
        int batchSize = 1000,
        IQlhvSourceOperationLock? operationLock = null,
        IQlhvOperationHistoryRepository? operationHistory = null,
        IHocVienPhotoProcessingService? photoProcessing = null)
        => new(
            reads,
            target,
            target,
            Options.Create(new AppSyncOptions { DryRun = dryRun, BatchSize = batchSize }),
            Options.Create(new SyncExecutionOptions { EnableTargetWrites = enableWrites }),
            operationLock ?? new FakeOperationLock(),
            operationHistory ?? new FakeOperationHistoryRepository(),
            photoProcessing);

    private static QlhvImportExecuteRequest ExecuteRequest() => new()
    {
        SourceProfileCode = "CSDT_OTO",
        MaCSDT = "66029",
        ExpectedSnapshotToken = "snapshot-token",
    };

    private static QlhvImportRequest OtoRequest() => new()
    {
        SourceProfileCode = "CSDT_OTO",
        MaCSDT = "66029",
    };

    private static FakeReadRepository OneOtoRow(QlhvImportTargetSnapshot target) => new()
    {
        Source = new QlhvImportSourceSnapshot
        {
            HocVienRows = new[] { Source("66029-001") },
        },
        Target = target,
    };

    private static V2HocVienSourceRow Source(string maDk) => new()
    {
        MaDK = maDk,
        MaKhoaHoc = "66029K01",
        TenKH = "Khoa OTO",
        HangDaoTao = "B2",
        TenHangDT = "Hang B2",
        HoVaTen = "Nguyen Van A",
        NgaySinh = new DateTime(1990, 1, 2),
        SoCMT = "001234567890",
        GioiTinh = "M",
    };

    private sealed class FakeReadRepository : IQlhvImportReadRepository
    {
        public QlhvImportSourceSnapshot Source { get; init; } = new();
        public QlhvImportTargetSnapshot Target { get; init; } = new();
        public Exception? TargetException { get; set; }
        public int SourceReads { get; private set; }
        public int TargetReads { get; private set; }

        public Task<QlhvImportSourceSnapshot> ReadSourceAsync(
            QlhvImportRequest request,
            CancellationToken cancellationToken = default)
        {
            SourceReads++;
            var courseRows = Source.KhoaHocSourceRows.Count > 0
                ? Source.KhoaHocSourceRows
                : Source.HocVienRows
                    .Where(row => !string.IsNullOrWhiteSpace(row.MaKhoaHoc))
                    .GroupBy(row => row.MaKhoaHoc!.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(group => new QlhvKhoaHocSourceRow
                    {
                        MaKH = group.Key,
                        MaCSDT = request.MaCSDT,
                        MaSoGTVT = request.MaCSDT,
                        TenKH = group.First().TenKH,
                        HangDT = group.First().HangDaoTao,
                        TrangThai = true,
                    })
                    .ToArray();
            return Task.FromResult(new QlhvImportSourceSnapshot
            {
                SourceDatabaseName = string.IsNullOrWhiteSpace(Source.SourceDatabaseName)
                    ? request.SourceProfileCode == "CSDT_MOTO" ? "CSDL_MOTO_BAK" : "CSDL_OTO_BAK"
                    : Source.SourceDatabaseName,
                BackupSnapshotToken = string.IsNullOrWhiteSpace(Source.BackupSnapshotToken)
                    ? "snapshot-token"
                    : Source.BackupSnapshotToken,
                GeneratedAtUtc = Source.GeneratedAtUtc == default ? DateTime.UtcNow : Source.GeneratedAtUtc,
                HocVienRows = Source.HocVienRows,
                KhoaHocRows = courseRows.Count > 0 ? courseRows.Count : Source.KhoaHocRows,
                KhoaHocSourceRows = courseRows,
                GiaoVienRows = Source.GiaoVienRows,
                KhoaHocGiaoVienRows = Source.KhoaHocGiaoVienRows,
                HocVienWarnings = Source.HocVienWarnings,
                KhoaHocBlockers = Source.KhoaHocBlockers,
                GiaoVienBlockers = Source.GiaoVienBlockers,
                RelationBlockers = Source.RelationBlockers,
            });
        }

        public Task<QlhvImportTargetSnapshot> ReadTargetAsync(
            QlhvImportRequest request,
            IReadOnlyCollection<string> sourceMaDks,
            CancellationToken cancellationToken = default)
        {
            TargetReads++;
            if (TargetException is not null)
            {
                throw TargetException;
            }

            return Task.FromResult(Target);
        }
    }

    private sealed class FakeTargetRepository :
        IQlhvHocVienTargetRepository,
        IQlhvImportWriteRepository
    {
        public int DiagnosticsReads { get; private set; }
        public int UpsertCalls { get; private set; }
        public string? LastFullSyncProfile { get; private set; }
        public QlhvImportFullSyncPayload? LastPayload { get; private set; }
        public bool FullSyncTokenCaptured { get; private set; }
        public CancellationToken LastFullSyncCancellationToken { get; private set; }
        public List<QlhvImportHocVienWriteModel> WrittenRows { get; } = new();
        public UpsertCounts UpsertResult { get; init; } = UpsertCounts.Empty;
        public QlhvImportFullSyncWriteResult FullSyncResult { get; init; } =
            new(0, 0, 0, 0, 0, 0, 0, 0);
        public int AtomicTargetMaDkConflictsOtherProfiles { get; init; }
        public int AtomicSoftDeletedIdentityConflicts { get; init; }

        public Task<int> CountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<IReadOnlyCollection<string>> GetExistingSourceKeysAsync(
            string sourceProfileCode,
            IReadOnlyCollection<string> sourceMaDks,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());

        public Task<IReadOnlyDictionary<string, string>> GetExistingSourceHashesAsync(
            string sourceProfileCode,
            IReadOnlyCollection<string> sourceMaDks,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task<QlhvHocVienTargetDiagnosticsDto> GetDiagnosticsAsync(
            CancellationToken cancellationToken = default)
        {
            DiagnosticsReads++;
            return Task.FromResult(new QlhvHocVienTargetDiagnosticsDto
            {
                AppHocVienExists = true,
                RequiredColumns = Array.Empty<RequiredColumnCheckDto>(),
            });
        }

        public Task<UpsertCounts> UpsertBatchAsync(
            IReadOnlyList<HocVienTargetWriteModel> rows,
            CancellationToken cancellationToken = default)
            => Task.FromResult(UpsertResult);

        public Task<QlhvImportGuardedUpsertResult> UpsertWithGuardsAsync(
            IReadOnlyList<HocVienTargetWriteModel> rows,
            CancellationToken cancellationToken = default)
        {
            UpsertCalls++;
            if (AtomicTargetMaDkConflictsOtherProfiles > 0 ||
                AtomicSoftDeletedIdentityConflicts > 0)
            {
                return Task.FromResult(new QlhvImportGuardedUpsertResult(
                    UpsertCounts.Empty,
                    AtomicTargetMaDkConflictsOtherProfiles,
                    AtomicSoftDeletedIdentityConflicts));
            }

            return Task.FromResult(new QlhvImportGuardedUpsertResult(
                UpsertResult,
                0,
                0));
        }

        public Task<QlhvImportFullSyncWriteResult> FullSyncAsync(
            string sourceProfileCode,
            QlhvImportFullSyncPayload payload,
            CancellationToken cancellationToken = default)
        {
            LastPayload = payload;
            return FullSyncAsync(sourceProfileCode, payload.HocVienRows, cancellationToken);
        }

        public Task<QlhvImportFullSyncWriteResult> FullSyncAsync(
            string sourceProfileCode,
            IReadOnlyList<QlhvImportHocVienWriteModel> rows,
            CancellationToken cancellationToken = default)
        {
            UpsertCalls++;
            LastFullSyncProfile = sourceProfileCode;
            FullSyncTokenCaptured = true;
            LastFullSyncCancellationToken = cancellationToken;
            if (!FullSyncResult.HasConflicts)
            {
                WrittenRows.AddRange(rows);
            }

            return Task.FromResult(FullSyncResult);
        }
    }

    private sealed class FakePhotoProcessingService : IHocVienPhotoProcessingService
    {
        public HocVienPhotoPlanDto Plan { get; init; } = new(0, 0, 0, 0, 0);
        public HocVienPhotoQueueBatchResult QueueResult { get; init; } = new(0, 0, 0, 0);
        public Exception? QueueException { get; init; }
        public Action? OnQueue { get; init; }
        public int BuildPlanCalls { get; private set; }
        public int QueueCalls { get; private set; }
        public IReadOnlyList<HocVienPhotoProcessingSource> LastQueuedSources { get; private set; } =
            Array.Empty<HocVienPhotoProcessingSource>();

        public Task<BackgroundRemovalEngineReadiness> GetReadinessAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new BackgroundRemovalEngineReadiness(
                false,
                "FIXTURE",
                "fixture",
                null,
                "fixture"));

        public Task<HocVienPhotoPlanDto> BuildPlanAsync(
            IReadOnlyList<HocVienPhotoProcessingSource> sources,
            CancellationToken cancellationToken = default)
        {
            BuildPlanCalls++;
            return Task.FromResult(Plan);
        }

        public Task<HocVienPhotoQueueBatchResult> QueueAfterSyncAsync(
            IReadOnlyList<HocVienPhotoProcessingSource> sources,
            string actor,
            CancellationToken cancellationToken = default)
        {
            QueueCalls++;
            LastQueuedSources = sources;
            OnQueue?.Invoke();
            return QueueException is null
                ? Task.FromResult(QueueResult)
                : Task.FromException<HocVienPhotoQueueBatchResult>(QueueException);
        }

        public Task ProcessAsync(
            HocVienPhotoProcessingWorkItem item,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HocVienPhotoProcessingPageDto> SearchAsync(
            HocVienPhotoSearchRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HocVienPhotoRecordDto?> ApproveAsync(
            long id,
            long userId,
            string actor,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HocVienPhotoRecordDto?> ReprocessAsync(
            long id,
            string actor,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HocVienPhotoContent?> GetSourceImageAsync(
            long id,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HocVienPhotoContent?> GetDerivedImageAsync(
            long id,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HocVienPhotoPrintSelection> GetPrintSelectionAsync(
            string sourceProfileCode,
            string sourceMaDk,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeOperationLock : IQlhvSourceOperationLock
    {
        public bool ShouldAcquire { get; init; } = true;
        public int AcquireCalls { get; private set; }
        public Action? OnAcquire { get; init; }

        public Task<IAsyncDisposable?> TryAcquireAsync(
            QlhvOperationSourceDefinition source,
            CancellationToken cancellationToken = default)
        {
            AcquireCalls++;
            OnAcquire?.Invoke();
            return Task.FromResult<IAsyncDisposable?>(ShouldAcquire ? new Lease() : null);
        }

        private sealed class Lease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeOperationHistoryRepository : IQlhvOperationHistoryRepository
    {
        public bool TryCreateResult { get; init; } = true;
        public int CompleteCalls { get; private set; }
        public Action? OnTryCreate { get; init; }
        public Exception? CompleteException { get; init; }
        public List<QlhvOperationHistoryCreate> Created { get; } = new();
        public List<(QlhvOperationHistoryCompletion Value, CancellationToken Token)> Completed { get; } = new();

        public Task<bool> TryCreateAsync(
            QlhvOperationHistoryCreate entry,
            CancellationToken cancellationToken = default)
        {
            OnTryCreate?.Invoke();
            Created.Add(entry);
            return Task.FromResult(TryCreateResult);
        }

        public Task MarkRunningAsync(Guid operationId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CompleteAsync(
            QlhvOperationHistoryCompletion completion,
            CancellationToken cancellationToken = default)
        {
            CompleteCalls++;
            if (CompleteException is not null)
            {
                return Task.FromException(CompleteException);
            }

            Completed.Add((completion, cancellationToken));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<QlhvOperationHistoryDto>> SearchAsync(
            string sourceType,
            int take,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<QlhvOperationHistoryDto>>(
                Array.Empty<QlhvOperationHistoryDto>());

        public Task<QlhvOperationHistoryDto?> GetActiveAsync(
            string sourceType,
            CancellationToken cancellationToken = default)
            => Task.FromResult<QlhvOperationHistoryDto?>(null);

        public Task<QlhvOperationHistoryDto?> GetLatestCompletedAsync(
            string sourceType,
            string operationType,
            CancellationToken cancellationToken = default)
            => Task.FromResult<QlhvOperationHistoryDto?>(null);
    }
}

using Microsoft.Extensions.Options;
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
        var existingKey = HocVienSourceIdentityKey.Create("CSDT_OTO", "66029-002");
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
                ExistingHocVienHashes = new Dictionary<string, string>
                {
                    [existingKey] = "old-hash",
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
        Assert.Equal(0, plan.PlannedUpsertKhoaHocRows);
        Assert.Contains(QlhvImportService.AppKhoaHocNotSupportedWarning, plan.Warnings);
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
    public async Task Plan_supports_empty_moto_source()
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

        Assert.True(plan.Executable);
        Assert.Equal(0, plan.SourceHocVienRows);
        Assert.Equal(0, plan.PlannedUpsertHocVienRows);
        Assert.Contains(plan.Warnings, warning => warning.Contains("Khong co hoc vien nguon", StringComparison.Ordinal));
        Assert.Equal(0, target.UpsertCalls);
    }

    [Fact]
    public async Task Execute_rejects_wrong_confirmation_before_any_read_or_write()
    {
        var reads = new FakeReadRepository();
        var target = new FakeTargetRepository();
        var service = CreateService(reads, target, dryRun: false, enableWrites: true);

        var result = await service.ExecuteAsync(new QlhvImportExecuteRequest
        {
            SourceProfileCode = "CSDT_OTO",
            MaCSDT = "66029",
            ConfirmText = "wrong",
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
        Assert.Contains(result.Plan.Blockers, blocker => blocker.Contains("CHECK constraint", StringComparison.Ordinal));
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
        Assert.Empty(diagnostics.Blockers);
        Assert.Equal(0, target.UpsertCalls);
    }

    [Fact]
    public async Task Diagnostics_moto_supports_an_empty_source_read_only()
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
        Assert.Empty(diagnostics.Blockers);
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
        Assert.Contains(diagnostics.Blockers, blocker => blocker.Contains("MaDK bi trung", StringComparison.Ordinal));
        Assert.Equal(0, target.UpsertCalls);
    }

    [Fact]
    public async Task Other_profile_ma_dk_conflict_is_a_blocker()
    {
        var reads = OneOtoRow(new QlhvImportTargetSnapshot
        {
            TargetMaDkConflictsOtherProfiles = 1,
        });
        var service = CreateService(reads, new FakeTargetRepository());

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.Contains(plan.Blockers, blocker => blocker.Contains("MaDK trung voi profile khac", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Soft_deleted_identity_conflict_is_a_blocker()
    {
        var reads = OneOtoRow(new QlhvImportTargetSnapshot
        {
            SoftDeletedIdentityConflicts = 1,
        });
        var service = CreateService(reads, new FakeTargetRepository());

        var plan = await service.GetPlanAsync(OtoRequest());

        Assert.Contains(plan.Blockers, blocker => blocker.Contains("da xoa mem", StringComparison.Ordinal));
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

        Assert.Contains(plan.Blockers, blocker => blocker.Contains("khong cho phep CSDT_OTO", StringComparison.Ordinal));
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
            UpsertResult = new UpsertCounts(1, 0, 0),
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
            UpsertResult = new UpsertCounts(2, 0, 0),
        };
        var service = CreateService(reads, target, dryRun: false, enableWrites: true, batchSize: 1);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.True(result.Executed);
        Assert.Equal(2, result.InsertedHocVienRows);
        Assert.Equal(1, target.UpsertCalls);
        Assert.Equal(2, target.WrittenRows.Count);
    }

    [Fact]
    public async Task Execute_rejects_a_soft_delete_conflict_rechecked_inside_write_transaction()
    {
        var reads = OneOtoRow(new QlhvImportTargetSnapshot());
        var target = new FakeTargetRepository
        {
            AtomicSoftDeletedIdentityConflicts = 1,
        };
        var service = CreateService(reads, target, dryRun: false, enableWrites: true);

        var result = await service.ExecuteAsync(ExecuteRequest());

        Assert.False(result.Executed);
        Assert.Contains(
            result.Plan.Blockers,
            blocker => blocker.Contains("da xoa mem tai thoi diem ghi", StringComparison.Ordinal));
        Assert.Equal(1, target.UpsertCalls);
        Assert.Empty(target.WrittenRows);
    }

    private static QlhvImportService CreateService(
        FakeReadRepository reads,
        FakeTargetRepository target,
        bool dryRun = true,
        bool enableWrites = false,
        int batchSize = 1000)
        => new(
            reads,
            target,
            target,
            Options.Create(new AppSyncOptions { DryRun = dryRun, BatchSize = batchSize }),
            Options.Create(new SyncExecutionOptions { EnableTargetWrites = enableWrites }));

    private static QlhvImportExecuteRequest ExecuteRequest() => new()
    {
        SourceProfileCode = "CSDT_OTO",
        MaCSDT = "66029",
        ConfirmText = QlhvImportService.ConfirmationText,
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
        public int SourceReads { get; private set; }
        public int TargetReads { get; private set; }

        public Task<QlhvImportSourceSnapshot> ReadSourceAsync(
            QlhvImportRequest request,
            CancellationToken cancellationToken = default)
        {
            SourceReads++;
            return Task.FromResult(Source);
        }

        public Task<QlhvImportTargetSnapshot> ReadTargetAsync(
            QlhvImportRequest request,
            IReadOnlyCollection<string> sourceMaDks,
            CancellationToken cancellationToken = default)
        {
            TargetReads++;
            return Task.FromResult(Target);
        }
    }

    private sealed class FakeTargetRepository :
        IQlhvHocVienTargetRepository,
        IQlhvImportWriteRepository
    {
        public int DiagnosticsReads { get; private set; }
        public int UpsertCalls { get; private set; }
        public List<HocVienTargetWriteModel> WrittenRows { get; } = new();
        public UpsertCounts UpsertResult { get; init; } = UpsertCounts.Empty;
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

            WrittenRows.AddRange(rows);
            return Task.FromResult(new QlhvImportGuardedUpsertResult(
                UpsertResult,
                0,
                0));
        }
    }
}

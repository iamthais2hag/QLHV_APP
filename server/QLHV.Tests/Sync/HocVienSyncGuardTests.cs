using Microsoft.Extensions.Options;
using System.Text.Json;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Tests.Sync;

public sealed class HocVienSyncGuardTests
{
    [Fact]
    public async Task Execute_rejects_when_target_writes_disabled()
    {
        var fakes = TestFakes.Create(enableWrites: false, dryRun: false);

        var result = await fakes.Service.ExecuteHocVienAsync(new SyncExecuteRequest
        {
            Confirm = true,
            ConfirmationText = fakes.Execution.ConfirmationPhrase,
        });

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains("EnableTargetWrites", result.Message);
        Assert.Equal(0, fakes.Source.ReadPageCalls);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
    }

    [Fact]
    public async Task Execute_rejects_when_manual_confirm_is_false()
    {
        var fakes = TestFakes.Create(enableWrites: true, dryRun: false);

        var result = await fakes.Service.ExecuteHocVienAsync(new SyncExecuteRequest
        {
            Confirm = false,
            ConfirmationText = fakes.Execution.ConfirmationPhrase,
        });

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains("Confirm", result.Message);
        Assert.Equal(0, fakes.Source.ReadPageCalls);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
    }

    [Fact]
    public async Task Execute_rejects_when_confirmation_phrase_is_not_exact()
    {
        var fakes = TestFakes.Create(enableWrites: true, dryRun: false);

        var result = await fakes.Service.ExecuteHocVienAsync(new SyncExecuteRequest
        {
            Confirm = true,
            ConfirmationText = "execute_dong_bo_v2_hoc_vien",
        });

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains("xac nhan", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fakes.Source.ReadPageCalls);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
    }

    [Fact]
    public async Task Execute_rejects_when_sync_dry_run_is_true_even_with_write_guard_and_confirmation()
    {
        var fakes = TestFakes.Create(enableWrites: true, dryRun: true);

        var result = await fakes.Service.ExecuteHocVienAsync(new SyncExecuteRequest
        {
            Confirm = true,
            ConfirmationText = fakes.Execution.ConfirmationPhrase,
        });

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains("Sync:DryRun", result.Message);
        Assert.Equal(0, fakes.Source.ReadPageCalls);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
    }

    [Fact]
    public async Task Dry_run_remains_no_write()
    {
        var fakes = TestFakes.Create(enableWrites: true, sourceCount: 7);

        var result = await fakes.Service.DryRunHocVienAsync();

        Assert.Equal(7, result.SourceRecordCount);
        Assert.Equal(1, fakes.Source.CountCalls);
        Assert.Equal(0, fakes.Source.ReadPageCalls);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
    }

    [Fact]
    public async Task Dry_run_mapping_uses_confirmed_v2_sources()
    {
        var fakes = TestFakes.Create(enableWrites: false);

        var result = await fakes.Service.DryRunHocVienAsync();

        Assert.Contains(result.Mapping, m =>
            m.TargetColumn == "SoGPLXDaCo" &&
            m.SourceFieldPlanned.Contains("NguoiLX_HoSo.SoGPLXDaCo", StringComparison.Ordinal));
        Assert.Contains(result.Mapping, m =>
            m.TargetColumn == "DiaChiThuongTru" &&
            m.SourceFieldPlanned.Contains("DM_DVHC.TenDayDu", StringComparison.Ordinal) &&
            m.SourceFieldPlanned.Contains("NoiTT_MaDVQL", StringComparison.Ordinal) &&
            m.SourceFieldPlanned.Contains("NoiTT_MaDVHC", StringComparison.Ordinal));
        Assert.Contains(result.Mapping, m =>
            m.TargetColumn == "HangGPLXHoc" &&
            m.SourceFieldPlanned.Contains("NguoiLX_HoSo.HangDaoTao", StringComparison.Ordinal) &&
            m.SourceFieldPlanned.Contains("DM_HangDT.TenHangDT", StringComparison.Ordinal));
        Assert.Contains(result.Mapping, m =>
            m.TargetColumn == "MaHangDT" &&
            m.SourceFieldPlanned.Contains("NguoiLX_HoSo.HangDaoTao", StringComparison.Ordinal));
        Assert.Contains(result.Mapping, m =>
            m.TargetColumn == "NguoiNhanHoSo" &&
            m.SourceFieldPlanned.Contains("NguoiLX_HoSo.NguoiNhanHSo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dry_run_mapping_does_not_include_retired_v2_sources()
    {
        var fakes = TestFakes.Create(enableWrites: false);

        var result = await fakes.Service.DryRunHocVienAsync();
        var reportText = string.Join(
            "\n",
            result.Mapping.Select(m => $"{m.TargetColumn}|{m.SourceFieldPlanned}|{m.Note}"));

        Assert.DoesNotContain("NguoiLX_GPLX", reportText, StringComparison.Ordinal);
        Assert.DoesNotContain("NguoiLX.DiaChiThuongTru", reportText, StringComparison.Ordinal);
        Assert.DoesNotContain("NguoiLX_HoSo.NguoiNhanHoSo", reportText, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Mapping, m =>
            m.TargetColumn == "HangGPLXHoc" &&
            m.SourceFieldPlanned.Contains("HangGPLX", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Config_check_returns_safe_flags_without_read_or_write()
    {
        var fakes = TestFakes.Create(enableWrites: false, sourceCount: 7);

        var result = await fakes.Service.ConfigCheckHocVienAsync();

        Assert.True(result.QlhvAppConfigured);
        Assert.True(result.CsdtV2Configured);
        Assert.False(result.DryRun);
        Assert.False(result.EnableTargetWrites);
        Assert.True(result.RequireManualConfirmation);
        Assert.False(result.AllowHangfireSchedule);
        Assert.Equal(0, fakes.Source.CountCalls);
        Assert.Equal(0, fakes.Source.ReadPageCalls);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
    }

    [Fact]
    public async Task Source_diagnostics_reads_source_only_and_does_not_write()
    {
        var diagnostics = new V2HocVienSourceDiagnosticsDto
        {
            SourceRows = 7,
            MissingHoTenCount = 1,
            SoCmtLength = new SoCmtLengthDiagnosticsDto
            {
                NineDigits = 2,
                TwelveDigits = 4,
                Other = 1,
            },
        };
        var fakes = TestFakes.Create(enableWrites: false, sourceCount: 7, diagnostics: diagnostics);

        var result = await fakes.Service.GetHocVienSourceDiagnosticsAsync();

        Assert.True(result.IsReadOnly);
        Assert.True(result.CanRead);
        Assert.Equal("SanSang", result.Status);
        Assert.NotNull(result.Diagnostics);
        Assert.Equal(7, result.Diagnostics.SourceRows);
        Assert.Equal(1, result.Diagnostics.MissingHoTenCount);
        Assert.Equal(1, fakes.Source.DiagnosticsCalls);
        Assert.Equal(0, fakes.Source.CountCalls);
        Assert.Equal(0, fakes.Source.ReadPageCalls);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
    }

    [Fact]
    public async Task Target_diagnostics_reads_target_only_and_does_not_write_or_expose_secrets()
    {
        var diagnostics = new QlhvHocVienTargetDiagnosticsDto
        {
            AppHocVienExists = true,
            AppDongBoLogExists = true,
            TargetRows = 1970,
            RequiredColumns = new[]
            {
                new RequiredColumnCheckDto { ColumnName = "MaDK", Exists = true },
                new RequiredColumnCheckDto { ColumnName = "V2RowHash", Exists = true },
            },
            SoCccdLength = new SoCmtLengthDiagnosticsDto
            {
                TwelveDigits = 1969,
                Other = 1,
            },
        };
        var fakes = TestFakes.Create(enableWrites: false, dryRun: true, targetDiagnostics: diagnostics);

        var result = await fakes.Service.GetHocVienTargetDiagnosticsAsync();
        var json = JsonSerializer.Serialize(result);

        Assert.True(result.IsReadOnly);
        Assert.True(result.CanRead);
        Assert.Equal("SanSang", result.Status);
        Assert.NotNull(result.Diagnostics);
        Assert.Equal(1970, result.Diagnostics.TargetRows);
        Assert.Equal(1, fakes.Target.DiagnosticsCalls);
        Assert.Equal(0, fakes.Source.CountCalls);
        Assert.Equal(0, fakes.Source.ReadPageCalls);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
        Assert.DoesNotContain("ConnectionString", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("User Id", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pre_execute_plan_reads_source_and_target_only_without_writes_or_secrets()
    {
        var sourceRows = new[] { SourceRow("DK001") };
        var targetSnapshot = new[]
        {
            TargetSnapshot("DK001", HashFor(sourceRows[0])),
        };
        var fakes = TestFakes.Create(
            enableWrites: false,
            dryRun: true,
            sourceRows: sourceRows,
            targetSnapshot: targetSnapshot);

        var result = await fakes.Service.GetHocVienPreExecutePlanAsync();
        var json = JsonSerializer.Serialize(result);

        Assert.True(result.IsReadOnly);
        Assert.True(result.CanPlan);
        Assert.Equal("SanSang", result.Status);
        Assert.NotNull(result.Plan);
        Assert.Equal(1, result.Plan.SourceRows);
        Assert.Equal(1, result.Plan.TargetRows);
        Assert.Equal(0, result.Plan.WouldInsert);
        Assert.Equal(0, result.Plan.WouldUpdate);
        Assert.Equal(1, result.Plan.WouldSkip);
        Assert.Equal(0, result.Plan.TargetOnlyRows);
        Assert.Equal(1, fakes.Source.ReadPageCalls);
        Assert.Equal(1, fakes.Target.SnapshotCalls);
        Assert.Equal(0, fakes.Source.CountCalls);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
        Assert.DoesNotContain("ConnectionString", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("User Id", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123456789012", json, StringComparison.Ordinal);
        Assert.DoesNotContain("GPLX001", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pre_execute_plan_classifies_insert_update_skip_and_target_only_rows()
    {
        var insert = SourceRow("DK_INSERT");
        var update = SourceRow("DK_UPDATE", hoTen: "Nguyen Van Update");
        var skip = SourceRow("DK_SKIP");
        var restore = SourceRow("DK_RESTORE");
        var sourceRows = new[] { insert, update, skip, restore };
        var targetSnapshot = new[]
        {
            TargetSnapshot("DK_UPDATE", "OLD_HASH"),
            TargetSnapshot("DK_SKIP", HashFor(skip)),
            TargetSnapshot("DK_RESTORE", HashFor(restore), isDeleted: true),
            TargetSnapshot("DK_TARGET_ONLY", "TARGET_ONLY_HASH"),
        };
        var fakes = TestFakes.Create(
            enableWrites: false,
            dryRun: true,
            sourceRows: sourceRows,
            targetSnapshot: targetSnapshot);

        var result = await fakes.Service.GetHocVienPreExecutePlanAsync();

        Assert.True(result.CanPlan);
        Assert.NotNull(result.Plan);
        Assert.Equal(4, result.Plan.SourceRows);
        Assert.Equal(3, result.Plan.TargetRows);
        Assert.Equal(2, result.Plan.WouldInsert);
        Assert.Equal(1, result.Plan.WouldUpdate);
        Assert.Equal(1, result.Plan.WouldSkip);
        Assert.Equal(1, result.Plan.TargetOnlyRows);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
    }

    [Fact]
    public async Task Pre_execute_plan_aggregates_mapping_warnings_without_raw_identity_values()
    {
        var sourceRows = new[]
        {
            SourceRow("DK_WARN", soCmt: "12345678901"),
        };
        var targetSnapshot = new[]
        {
            TargetSnapshot("DK_WARN", HashFor(sourceRows[0])),
        };
        var fakes = TestFakes.Create(
            enableWrites: false,
            dryRun: true,
            sourceRows: sourceRows,
            targetSnapshot: targetSnapshot);

        var result = await fakes.Service.GetHocVienPreExecutePlanAsync();
        var json = JsonSerializer.Serialize(result);

        Assert.True(result.CanPlan);
        Assert.NotNull(result.Plan);
        Assert.Equal(1, result.Plan.WarningCount);
        var warning = Assert.Single(result.Plan.Warnings);
        Assert.Equal("CCCD_LENGTH", warning.Code);
        Assert.Equal("SoCCCD", warning.Field);
        Assert.Equal(1, warning.Count);
        Assert.DoesNotContain("12345678901", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DK_WARN", json, StringComparison.Ordinal);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
    }

    private static V2HocVienSourceRow SourceRow(
        string maDk,
        string? hoTen = "Nguyen Van A",
        string? soCmt = "123456789012") => new()
    {
        MaDK = maDk,
        MaKhoaHoc = "KHOA001",
        TenKH = "AK01",
        HangDaoTao = "Am",
        TenHangDT = "Hang Am",
        HoVaTen = hoTen,
        NgaySinh = new DateTime(2000, 1, 2),
        GioiTinh = "M",
        SoCMT = soCmt,
        NoiTTTenDayDu = "Dia chi test",
        SoGPLXDaCo = "GPLX001",
        HangGPLXDaCo = "A1",
        NguoiNhanHoSo = "Can bo test",
    };

    private static string HashFor(V2HocVienSourceRow source)
        => HocVienSyncMapper.MapAndValidate(source).Model?.V2RowHash ?? string.Empty;

    private static HocVienTargetSyncSnapshotDto TargetSnapshot(
        string maDk,
        string? v2RowHash,
        bool isDeleted = false) => new()
    {
        MaDK = maDk,
        V2RowHash = v2RowHash,
        IsDeleted = isDeleted,
    };

    private sealed class TestFakes
    {
        private TestFakes(
            HocVienSyncService service,
            SyncExecutionOptions execution,
            FakeV2Source source,
            FakeTarget target,
            FakeRunLog log)
        {
            Service = service;
            Execution = execution;
            Source = source;
            Target = target;
            Log = log;
        }

        public HocVienSyncService Service { get; }
        public SyncExecutionOptions Execution { get; }
        public FakeV2Source Source { get; }
        public FakeTarget Target { get; }
        public FakeRunLog Log { get; }

        public static TestFakes Create(
            bool enableWrites,
            int sourceCount = 0,
            V2HocVienSourceDiagnosticsDto? diagnostics = null,
            QlhvHocVienTargetDiagnosticsDto? targetDiagnostics = null,
            IReadOnlyList<V2HocVienSourceRow>? sourceRows = null,
            IReadOnlyList<HocVienTargetSyncSnapshotDto>? targetSnapshot = null,
            bool dryRun = false)
        {
            var sync = new AppSyncOptions
            {
                BatchSize = 100,
                TimeoutSeconds = 30,
                DryRun = dryRun,
            };
            var execution = new SyncExecutionOptions
            {
                EnableTargetWrites = enableWrites,
                RequireManualConfirmation = true,
                ConfirmationPhrase = "EXECUTE_DONG_BO_V2_HOC_VIEN",
            };
            var connections = new FakeConnectionSettingsProvider();
            var source = new FakeV2Source(sourceCount, diagnostics, sourceRows);
            var target = new FakeTarget(targetDiagnostics, targetSnapshot);
            var log = new FakeRunLog();
            var service = new HocVienSyncService(
                Options.Create(sync),
                Options.Create(execution),
                connections,
                source,
                target,
                log);

            return new TestFakes(service, execution, source, target, log);
        }
    }

    private sealed class FakeConnectionSettingsProvider : IConnectionSettingsProvider
    {
        public Task<ResolvedConnection> GetQlhvAppConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Usable("QLHV_APP"));

        public Task<ResolvedConnection> GetSourceConnectionAsync(
            SourceSystem source,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Usable(source.ToString()));

        public Task<ConnectionSettingsView> GetViewAsync(
            SourceSystem source,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ConnectionSettingsView
            {
                Key = source.ToString(),
                DisplayName = source.ToString(),
                IsConfigured = true,
                IsEnabled = true,
            });

        private static ResolvedConnection Usable(string name) => new()
        {
            Name = name,
            IsConfigured = true,
            IsPlaceholder = false,
            ConnectionString = "Server=(local);Database=TEST_ONLY;",
        };
    }

    private sealed class FakeV2Source : IV2HocVienSourceRepository
    {
        private readonly int _sourceCount;
        private readonly V2HocVienSourceDiagnosticsDto _diagnostics;
        private readonly IReadOnlyList<V2HocVienSourceRow> _sourceRows;

        public FakeV2Source(
            int sourceCount,
            V2HocVienSourceDiagnosticsDto? diagnostics,
            IReadOnlyList<V2HocVienSourceRow>? sourceRows)
        {
            _sourceCount = sourceCount;
            _sourceRows = sourceRows ?? Array.Empty<V2HocVienSourceRow>();
            _diagnostics = diagnostics ?? new V2HocVienSourceDiagnosticsDto
            {
                SourceRows = _sourceRows.Count > 0 ? _sourceRows.Count : sourceCount,
            };
        }

        public int CountCalls { get; private set; }
        public int ReadPageCalls { get; private set; }
        public int DiagnosticsCalls { get; private set; }

        public Task<int> CountAsync(HocVienSourceFilter filter, CancellationToken cancellationToken = default)
        {
            CountCalls++;
            return Task.FromResult(_sourceRows.Count > 0 ? _sourceRows.Count : _sourceCount);
        }

        public Task<IReadOnlyList<V2HocVienSourceRow>> ReadPageAsync(
            HocVienSourceFilter filter,
            int offset,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            ReadPageCalls++;
            var page = _sourceRows
                .Skip(offset)
                .Take(pageSize)
                .ToList();
            return Task.FromResult<IReadOnlyList<V2HocVienSourceRow>>(page);
        }

        public Task<V2HocVienSourceDiagnosticsDto> GetDiagnosticsAsync(
            CancellationToken cancellationToken = default)
        {
            DiagnosticsCalls++;
            return Task.FromResult(_diagnostics);
        }
    }

    private sealed class FakeTarget : IQlhvHocVienTargetRepository
    {
        private readonly QlhvHocVienTargetDiagnosticsDto _diagnostics;
        private readonly IReadOnlyList<HocVienTargetSyncSnapshotDto> _snapshot;

        public FakeTarget(
            QlhvHocVienTargetDiagnosticsDto? diagnostics,
            IReadOnlyList<HocVienTargetSyncSnapshotDto>? snapshot)
        {
            _diagnostics = diagnostics ?? new QlhvHocVienTargetDiagnosticsDto();
            _snapshot = snapshot ?? Array.Empty<HocVienTargetSyncSnapshotDto>();
        }

        public int UpsertCalls { get; private set; }
        public int DiagnosticsCalls { get; private set; }
        public int SnapshotCalls { get; private set; }

        public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IReadOnlyCollection<string>> GetExistingKeysAsync(
            IReadOnlyCollection<string> maDks,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());

        public Task<QlhvHocVienTargetDiagnosticsDto> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        {
            DiagnosticsCalls++;
            return Task.FromResult(_diagnostics);
        }

        public Task<IReadOnlyList<HocVienTargetSyncSnapshotDto>> GetSyncSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            SnapshotCalls++;
            return Task.FromResult(_snapshot);
        }

        public Task<UpsertCounts> UpsertBatchAsync(
            IReadOnlyList<HocVienTargetWriteModel> rows,
            CancellationToken cancellationToken = default)
        {
            UpsertCalls++;
            return Task.FromResult(UpsertCounts.Empty);
        }
    }

    private sealed class FakeRunLog : ISyncRunLogWriter
    {
        public int WriteCalls { get; private set; }

        public Task<long> WriteAsync(SyncRunLogEntry entry, CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            return Task.FromResult(1L);
        }
    }
}

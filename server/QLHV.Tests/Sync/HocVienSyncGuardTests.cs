using Microsoft.Extensions.Options;
using System.Text.Json;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
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
    public async Task Pre_execute_plan_reads_source_and_target_hashes_without_write()
    {
        var sourceRows = new[] { SourceRow("DK001") };
        var mapped = QLHV.Application.Sync.Mapping.HocVienSyncMapper.MapAndValidate(
            sourceRows[0],
            QLHV.Application.Sync.Mapping.HocVienSourceIdentityContext.DataV2);
        var sourceKey = QLHV.Application.Sync.Mapping.HocVienSourceIdentityKey.Create("DATA_V2", "DK001");
        var fakes = TestFakes.Create(
            enableWrites: false,
            dryRun: true,
            sourceRows: sourceRows,
            targetHashes: new Dictionary<string, string>
            {
                [sourceKey] = mapped.Model!.V2RowHash,
            });

        var result = await fakes.Service.GetHocVienPreExecutePlanAsync();

        Assert.True(result.IsDryRun);
        Assert.Equal("DATA_V2", result.SourceProfileCode);
        Assert.Equal("V2", result.SourceSystem);
        Assert.Equal(1, result.SourceRowsRead);
        Assert.Equal(0, result.PlannedInsert);
        Assert.Equal(0, result.PlannedUpdate);
        Assert.Equal(1, result.PlannedSkip);
        Assert.Contains(result.Items, item =>
            item.MaDK == "DK001" &&
            item.Action == PlannedSyncAction.Skip &&
            item.ActionName == nameof(PlannedSyncAction.Skip));
        Assert.Equal(1, fakes.Source.ReadPageCalls);
        Assert.Equal(1, fakes.Target.HashLookupCalls);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
    }

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
            bool dryRun = false,
            IReadOnlyList<V2HocVienSourceRow>? sourceRows = null,
            IReadOnlyDictionary<string, string>? targetHashes = null)
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
            var target = new FakeTarget(targetDiagnostics, targetHashes);
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
                SourceRows = sourceRows?.Count ?? sourceCount,
            };
        }

        public int CountCalls { get; private set; }
        public int ReadPageCalls { get; private set; }
        public int DiagnosticsCalls { get; private set; }

        public Task<int> CountAsync(HocVienSourceFilter filter, CancellationToken cancellationToken = default)
        {
            CountCalls++;
            return Task.FromResult(_sourceCount);
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
                .ToArray();
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
        private readonly IReadOnlyDictionary<string, string> _targetHashes;

        public FakeTarget(
            QlhvHocVienTargetDiagnosticsDto? diagnostics,
            IReadOnlyDictionary<string, string>? targetHashes)
        {
            _diagnostics = diagnostics ?? new QlhvHocVienTargetDiagnosticsDto();
            _targetHashes = targetHashes ?? new Dictionary<string, string>();
        }

        public int UpsertCalls { get; private set; }
        public int DiagnosticsCalls { get; private set; }
        public int HashLookupCalls { get; private set; }

        public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IReadOnlyCollection<string>> GetExistingSourceKeysAsync(
            string sourceProfileCode,
            IReadOnlyCollection<string> sourceMaDks,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());

        public Task<IReadOnlyDictionary<string, string>> GetExistingSourceHashesAsync(
            string sourceProfileCode,
            IReadOnlyCollection<string> sourceMaDks,
            CancellationToken cancellationToken = default)
        {
            HashLookupCalls++;
            return Task.FromResult(_targetHashes);
        }

        public Task<QlhvHocVienTargetDiagnosticsDto> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        {
            DiagnosticsCalls++;
            return Task.FromResult(_diagnostics);
        }

        public Task<UpsertCounts> UpsertBatchAsync(
            IReadOnlyList<QLHV.Application.Sync.Mapping.HocVienTargetWriteModel> rows,
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

    private static V2HocVienSourceRow SourceRow(string maDk) => new()
    {
        MaDK = maDk,
        HoVaTen = "Nguyen Van A",
        NgaySinh = new DateTime(1990, 1, 2),
        SoCMT = "001234567890",
        GioiTinh = "M",
        MaKhoaHoc = "K001",
        TenKH = "Khoa 1",
        HangDaoTao = "B2",
        TenHangDT = "Hang B2",
        NoiTT = "Dia chi",
        NoiTTTenDayDu = "Dia chi day du",
        SoGPLXDaCo = "GPLX1",
        HangGPLXDaCo = "A1",
        NguoiNhanHoSo = "Nhan vien",
    };
}

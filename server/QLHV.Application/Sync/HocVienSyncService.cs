using Microsoft.Extensions.Options;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync;

/// <summary>
/// Application service for one-way HocVien sync from CSDT_V2 to QLHV_APP.
/// Dry-run builds a safe plan only. Execute is guarded by EnableTargetWrites + manual confirmation.
/// </summary>
public sealed class HocVienSyncService : IHocVienSyncService
{
    private readonly SyncOptions _options;
    private readonly SyncExecutionOptions _execution;
    private readonly IConnectionSettingsProvider _connections;
    private readonly IV2HocVienSourceRepository _v2Source;
    private readonly IQlhvHocVienTargetRepository _target;
    private readonly ISyncRunLogWriter _runLog;

    public HocVienSyncService(
        IOptions<SyncOptions> options,
        IOptions<SyncExecutionOptions> execution,
        IConnectionSettingsProvider connections,
        IV2HocVienSourceRepository v2Source,
        IQlhvHocVienTargetRepository target,
        ISyncRunLogWriter runLog)
    {
        _options = options.Value;
        _execution = execution.Value;
        _connections = connections;
        _v2Source = v2Source;
        _target = target;
        _runLog = runLog;
    }

    public async Task<DryRunResultDto> DryRunHocVienAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<SyncErrorDto>();
        var issues = new List<string>();

        AddConfigIssueIf(_options.BatchSize <= 0, "BatchSize phai lon hon 0.", errors, issues);
        AddConfigIssueIf(_options.TimeoutSeconds <= 0, "TimeoutSeconds phai lon hon 0.", errors, issues);
        AddConfigIssueIf(
            string.IsNullOrWhiteSpace(_options.QlhvAppConnectionName),
            "Thieu ten connection string QLHV_APP.",
            errors,
            issues);
        AddConfigIssueIf(
            string.IsNullOrWhiteSpace(_options.V2ConnectionName),
            "Thieu ten cau hinh ket noi CSDT_V2.",
            errors,
            issues);

        var qlhv = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        var v2 = await _connections.GetSourceConnectionAsync(SourceSystem.V2, cancellationToken);

        AddConnectionIssueIfNotUsable("QLHV_APP", qlhv, errors, issues);
        AddConnectionIssueIfNotUsable("CSDT_V2", v2, errors, issues);

        var hasBlockingError = errors.Any(e => e.Code == "CONFIG_INVALID");
        var canRun = !hasBlockingError && qlhv.IsUsable && v2.IsUsable;
        var now = DateTime.UtcNow;

        // Phase B2 (CHỈ ĐỌC): nếu nguồn V2 đã cấu hình dùng được, đọc số lượng học viên ở nguồn
        // bằng SELECT COUNT (không ghi gì). Nếu chưa cấu hình/placeholder thì bỏ qua an toàn.
        int? sourceCount = null;
        if (v2.IsUsable)
        {
            try
            {
                sourceCount = await _v2Source.CountAsync(HocVienSourceFilter.Empty, cancellationToken);
            }
            catch (Exception ex)
            {
                // Không lộ chuỗi kết nối/bí mật: chỉ ghi nhận thông điệp an toàn.
                var issue = "Khong doc duoc so luong tu nguon CSDT_V2.";
                issues.Add(issue);
                errors.Add(new SyncErrorDto
                {
                    Code = "SOURCE_READ_FAILED",
                    Message = $"{issue} Chi tiet: {ex.GetType().Name}.",
                });
                canRun = false;
            }
        }

        return new DryRunResultDto
        {
            CanRun = canRun,
            Status = canRun ? "SanSang" : "ThieuCauHinh",
            Issues = issues,
            Target = ToConnectionCheck(qlhv),
            Source = ToConnectionCheck(v2),
            SourceRecordCount = sourceCount,
            PlannedSummary = new SyncSummaryDto
            {
                JobName = IHocVienSyncJob.JobName,
                EntityType = "HocVien",
                SourceSystem = "V2",
                IsDryRun = true,
                Status = canRun ? "DuKien" : "ThieuCauHinh",
                TotalRead = sourceCount ?? 0,
                TotalInserted = 0,
                TotalUpdated = 0,
                TotalSkipped = 0,
                TotalError = errors.Count(e => e.Code == "CONFIG_INVALID"),
                RetryCount = 0,
                StartedAt = now,
                EndedAt = now,
                DurationMs = 0,
                Errors = errors,
            },
            Mapping = HocVienSyncMapping.Fields,
            BatchSize = _options.BatchSize,
            TimeoutSeconds = _options.TimeoutSeconds,
        };
    }

    public async Task<SyncExecuteResultDto> ExecuteHocVienAsync(
        SyncExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        // GÁC 1: công tắc ghi phải bật.
        if (!_execution.EnableTargetWrites)
        {
            return Blocked("Ghi bi chan: SyncExecution.EnableTargetWrites = false.");
        }

        // GÁC 2: xác nhận thủ công (chống chạy nhầm từ Swagger).
        if (_execution.RequireManualConfirmation)
        {
            if (!request.Confirm)
            {
                return Blocked("Thieu xac nhan: Confirm phai = true.");
            }

            if (!string.Equals(request.ConfirmationPhrase, _execution.ConfirmationPhrase, StringComparison.Ordinal))
            {
                return Blocked("Chuoi xac nhan khong khop.");
            }
        }

        // GÁC 3: kết nối nguồn và đích phải dùng được.
        var qlhv = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        var v2 = await _connections.GetSourceConnectionAsync(SourceSystem.V2, cancellationToken);
        if (!qlhv.IsUsable || !v2.IsUsable)
        {
            return Blocked("Ket noi QLHV_APP hoac CSDT_V2 chua cau hinh dung duoc.");
        }

        var startedAt = DateTime.UtcNow;

        int totalRead = 0, inserted = 0, updated = 0, skipped = 0, warningCount = 0;
        try
        {
            var offset = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Đọc nguồn (CHỈ ĐỌC). Repository tự bọc Polly retry ở tầng Infrastructure.
                var batch = await _v2Source.ReadPageAsync(
                    HocVienSourceFilter.Empty, offset, _options.BatchSize, cancellationToken);

                if (batch.Count == 0)
                {
                    break;
                }

                totalRead += batch.Count;

                var models = new List<HocVienTargetWriteModel>(batch.Count);
                foreach (var sourceRow in batch)
                {
                    var mapped = HocVienSyncMapper.MapAndValidate(sourceRow);
                    warningCount += mapped.Warnings.Count;
                    if (mapped.ShouldSkip || mapped.Model is null)
                    {
                        skipped++;
                        continue;
                    }

                    models.Add(mapped.Model);
                }

                if (models.Count > 0)
                {
                    // Ghi theo lô (staging + MERGE + transaction). Repository tự bọc Polly retry.
                    var counts = await _target.UpsertBatchAsync(models, cancellationToken);
                    inserted += counts.Inserted;
                    updated += counts.Updated;
                    skipped += counts.Skipped;
                }

                if (batch.Count < _options.BatchSize)
                {
                    break;
                }

                offset += _options.BatchSize;
            }

            var endedAt = DateTime.UtcNow;
            var summary = BuildSummary("ThanhCong", totalRead, inserted, updated, skipped, 0, startedAt, endedAt);
            await WriteRunLogSafe(summary, warningCount, errorMessage: null, cancellationToken);

            return new SyncExecuteResultDto
            {
                Executed = true,
                Status = "ThanhCong",
                Message = "Dong bo hoan tat.",
                Summary = summary,
            };
        }
        catch (Exception ex)
        {
            var endedAt = DateTime.UtcNow;
            var summary = BuildSummary("Loi", totalRead, inserted, updated, skipped, 1, startedAt, endedAt);

            // Thông điệp lỗi đã làm sạch: chỉ loại lỗi, không lộ chi tiết nhạy cảm/chuỗi kết nối.
            await WriteRunLogSafe(summary, warningCount, errorMessage: ex.GetType().Name, cancellationToken);

            return new SyncExecuteResultDto
            {
                Executed = true,
                Status = "Loi",
                Message = $"Dong bo that bai: {ex.GetType().Name}. Da rollback cac lo gap loi.",
                Summary = summary,
            };
        }
    }

    private static SyncSummaryDto BuildSummary(
        string status, int read, int inserted, int updated, int skipped, int error,
        DateTime startedAt, DateTime endedAt) => new()
    {
        JobName = IHocVienSyncJob.JobName,
        EntityType = "HocVien",
        SourceSystem = "V2",
        IsDryRun = false,
        Status = status,
        TotalRead = read,
        TotalInserted = inserted,
        TotalUpdated = updated,
        TotalSkipped = skipped,
        TotalError = error,
        RetryCount = 0,
        StartedAt = startedAt,
        EndedAt = endedAt,
        DurationMs = (long)(endedAt - startedAt).TotalMilliseconds,
    };

    private async Task WriteRunLogSafe(
        SyncSummaryDto summary,
        int warningCount,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await _runLog.WriteAsync(new SyncRunLogEntry
            {
                JobName = summary.JobName,
                EntityType = summary.EntityType,
                SourceSystem = summary.SourceSystem,
                StartedAt = summary.StartedAt,
                EndedAt = summary.EndedAt,
                DurationMs = summary.DurationMs,
                Status = summary.Status,
                TotalRead = summary.TotalRead,
                TotalInserted = summary.TotalInserted,
                TotalUpdated = summary.TotalUpdated,
                TotalSkipped = summary.TotalSkipped,
                TotalError = summary.TotalError,
                RetryCount = summary.RetryCount,
                ErrorMessage = errorMessage,
                DetailJson = $"{{\"warningCount\":{warningCount}}}",
                CreatedBy = "SyncV2",
            }, cancellationToken);
        }
        catch
        {
            // Không để lỗi ghi nhật ký làm hỏng kết quả tổng thể; nuốt lỗi an toàn (không log bí mật).
        }
    }

    private static SyncExecuteResultDto Blocked(string message) => new()
    {
        Executed = false,
        Status = "BiChan",
        Message = message,
        Summary = null,
    };

    private static void AddConfigIssueIf(
        bool condition,
        string issue,
        ICollection<SyncErrorDto> errors,
        ICollection<string> issues)
    {
        if (!condition)
        {
            return;
        }

        issues.Add(issue);
        errors.Add(new SyncErrorDto
        {
            Code = "CONFIG_INVALID",
            Message = issue,
        });
    }

    private static void AddConnectionIssueIfNotUsable(
        string name,
        ResolvedConnection connection,
        ICollection<SyncErrorDto> errors,
        ICollection<string> issues)
    {
        if (connection.IsUsable)
        {
            return;
        }

        var issue = $"{name} chua co cau hinh ket noi dung duoc.";
        issues.Add(issue);
        errors.Add(new SyncErrorDto
        {
            Code = "CONNECTION_NOT_CONFIGURED",
            Message = issue,
        });
    }

    private static ConnectionCheckDto ToConnectionCheck(ResolvedConnection connection)
    {
        var message = connection.IsUsable
            ? "Da co cau hinh ket noi dung duoc. Phase A chua mo ket noi that."
            : connection.IsPlaceholder
                ? "Cau hinh ket noi dang la placeholder."
                : "Chua co cau hinh ket noi.";

        return new ConnectionCheckDto
        {
            Name = connection.Name,
            IsConfigured = connection.IsConfigured,
            IsPlaceholder = connection.IsPlaceholder,
            IsUsable = connection.IsUsable,
            Message = message,
        };
    }
}

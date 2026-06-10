using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync;

/// <summary>
/// Application service for one-way HocVien sync from CSDT_V2 to QLHV_APP.
<<<<<<< HEAD
/// Dry-run remains no-write. Manual execution is guarded by explicit server and request switches.
=======
/// Dry-run builds a safe plan only. Execute is guarded by EnableTargetWrites + manual confirmation.
>>>>>>> task5-phase-b3b-guarded-write-path
/// </summary>
public sealed class HocVienSyncService : IHocVienSyncService
{
    private readonly SyncOptions _options;
    private readonly SyncExecutionOptions _execution;
    private readonly IConnectionSettingsProvider _connections;
    private readonly IV2HocVienSourceRepository _v2Source;
    private readonly IQlhvHocVienTargetRepository _target;
<<<<<<< HEAD
    private readonly ISyncRunLogWriter _logWriter;
=======
    private readonly ISyncRunLogWriter _runLog;
>>>>>>> task5-phase-b3b-guarded-write-path

    public HocVienSyncService(
        IOptions<SyncOptions> options,
        IOptions<SyncExecutionOptions> execution,
        IConnectionSettingsProvider connections,
        IV2HocVienSourceRepository v2Source,
        IQlhvHocVienTargetRepository target,
<<<<<<< HEAD
        ISyncRunLogWriter logWriter)
=======
        ISyncRunLogWriter runLog)
>>>>>>> task5-phase-b3b-guarded-write-path
    {
        _options = options.Value;
        _execution = execution.Value;
        _connections = connections;
        _v2Source = v2Source;
        _target = target;
<<<<<<< HEAD
        _logWriter = logWriter;
=======
        _runLog = runLog;
>>>>>>> task5-phase-b3b-guarded-write-path
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

        int? sourceCount = null;
        if (v2.IsUsable)
        {
            try
            {
                sourceCount = await _v2Source.CountAsync(HocVienSourceFilter.Empty, cancellationToken);
            }
            catch (Exception ex)
            {
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

<<<<<<< HEAD
    public async Task<HocVienSyncExecuteResultDto> ExecuteHocVienAsync(
        HocVienSyncExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new HocVienSyncExecuteRequest();
        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var issues = ValidateExecutionGuards(request);

        if (issues.Count > 0)
        {
            return Rejected(startedAt, stopwatch.ElapsedMilliseconds, issues);
        }

        var qlhv = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        var v2 = await _connections.GetSourceConnectionAsync(SourceSystem.V2, cancellationToken);

        if (!qlhv.IsUsable)
        {
            issues.Add("QLHV_APP chua co cau hinh ket noi dung duoc.");
        }

        if (!v2.IsUsable)
        {
            issues.Add("CSDT_V2 chua co cau hinh ket noi dung duoc.");
        }

        if (issues.Count > 0)
        {
            return Rejected(startedAt, stopwatch.ElapsedMilliseconds, issues);
        }

        var filter = (request.Filter ?? HocVienSourceFilter.Empty).Normalized();
        var batchSize = Math.Max(1, _options.BatchSize);
        var maxRows = request.MaxRows is > 0 ? request.MaxRows.Value : (int?)null;
        var totalAvailable = await _v2Source.CountAsync(filter, cancellationToken);
        var remaining = maxRows.HasValue ? Math.Min(maxRows.Value, totalAvailable) : totalAvailable;

        var totalRead = 0;
        var totalInserted = 0;
        var totalUpdated = 0;
        var totalSkipped = 0;
        var totalError = 0;
        var errors = new List<SyncErrorDto>();

        try
        {
            for (var offset = 0; remaining > 0; offset += batchSize)
            {
                var pageSize = Math.Min(batchSize, remaining);
                var rows = await _v2Source.ReadPageAsync(filter, offset, pageSize, cancellationToken);
                if (rows.Count == 0)
=======
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
>>>>>>> task5-phase-b3b-guarded-write-path
                {
                    break;
                }

<<<<<<< HEAD
                var upsert = await _target.UpsertBatchAsync(rows, dryRun: false, cancellationToken);
                totalRead += upsert.TotalRead;
                totalInserted += upsert.Inserted;
                totalUpdated += upsert.Updated;
                totalSkipped += upsert.Skipped;
                remaining -= rows.Count;
            }

            stopwatch.Stop();
            var summary = BuildSummary(
                startedAt,
                stopwatch.ElapsedMilliseconds,
                "ThanhCong",
                totalRead,
                totalInserted,
                totalUpdated,
                totalSkipped,
                totalError,
                errors);

            await _logWriter.WriteAsync(ToLogEntry(summary, errorMessage: null), cancellationToken);

            return new HocVienSyncExecuteResultDto
            {
                Accepted = true,
                Status = "ThanhCong",
                Message = "Dong bo HocVien tu CSDT_V2 sang QLHV_APP da hoan tat.",
=======
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
>>>>>>> task5-phase-b3b-guarded-write-path
                Summary = summary,
            };
        }
        catch (Exception ex)
        {
<<<<<<< HEAD
            stopwatch.Stop();
            totalError++;
            errors.Add(new SyncErrorDto
            {
                Code = "SYNC_EXECUTION_FAILED",
                Message = $"Dong bo that bai. Chi tiet an toan: {ex.GetType().Name}.",
            });

            var summary = BuildSummary(
                startedAt,
                stopwatch.ElapsedMilliseconds,
                "Loi",
                totalRead,
                totalInserted,
                totalUpdated,
                totalSkipped,
                totalError,
                errors);

            await TryWriteFailureLogAsync(summary, errors[0].Message, cancellationToken);

            return new HocVienSyncExecuteResultDto
            {
                Accepted = true,
                Status = "Loi",
                Message = errors[0].Message,
                Summary = summary,
                Issues = new[] { errors[0].Message },
=======
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
>>>>>>> task5-phase-b3b-guarded-write-path
            };
        }
    }

<<<<<<< HEAD
    private List<string> ValidateExecutionGuards(HocVienSyncExecuteRequest request)
    {
        var issues = new List<string>();

        if (!_options.EnableTargetWrites)
        {
            issues.Add("EnableTargetWrites=false. Endpoint execute bi khoa va khong ghi du lieu.");
        }

        if (_options.RequireManualConfirmation)
        {
            if (!request.ConfirmTargetWrites)
            {
                issues.Add("Thieu ConfirmTargetWrites=true.");
            }

            if (!string.Equals(
                    request.ConfirmationText,
                    HocVienSyncExecuteRequest.RequiredConfirmationText,
                    StringComparison.Ordinal))
            {
                issues.Add($"ConfirmationText phai bang '{HocVienSyncExecuteRequest.RequiredConfirmationText}'.");
            }
        }

        if (_options.BatchSize <= 0)
        {
            issues.Add("BatchSize phai lon hon 0.");
        }

        if (_options.TimeoutSeconds <= 0)
        {
            issues.Add("TimeoutSeconds phai lon hon 0.");
        }

        return issues;
    }

    private static HocVienSyncExecuteResultDto Rejected(
        DateTime startedAt,
        long durationMs,
        IReadOnlyList<string> issues)
    {
        return new HocVienSyncExecuteResultDto
        {
            Accepted = false,
            Status = "Rejected",
            Message = "Manual execution rejected by safety guards.",
            Issues = issues,
            Summary = BuildSummary(
                startedAt,
                durationMs,
                "Rejected",
                totalRead: 0,
                totalInserted: 0,
                totalUpdated: 0,
                totalSkipped: 0,
                totalError: issues.Count,
                errors: issues.Select(issue => new SyncErrorDto
                {
                    Code = "EXECUTION_GUARD_REJECTED",
                    Message = issue,
                }).ToList()),
        };
    }

    private static SyncSummaryDto BuildSummary(
        DateTime startedAt,
        long durationMs,
        string status,
        int totalRead,
        int totalInserted,
        int totalUpdated,
        int totalSkipped,
        int totalError,
        IReadOnlyList<SyncErrorDto> errors)
    {
        return new SyncSummaryDto
        {
            JobName = IHocVienSyncJob.JobName,
            EntityType = "HocVien",
            SourceSystem = "V2",
            IsDryRun = false,
            Status = status,
            TotalRead = totalRead,
            TotalInserted = totalInserted,
            TotalUpdated = totalUpdated,
            TotalSkipped = totalSkipped,
            TotalError = totalError,
            RetryCount = 0,
            StartedAt = startedAt,
            EndedAt = startedAt.AddMilliseconds(durationMs),
            DurationMs = durationMs,
            Errors = errors,
        };
    }

    private static SyncRunLogEntry ToLogEntry(SyncSummaryDto summary, string? errorMessage)
    {
        var detail = new
        {
            summary.JobName,
            summary.EntityType,
            summary.SourceSystem,
            summary.Status,
            summary.TotalRead,
            summary.TotalInserted,
            summary.TotalUpdated,
            summary.TotalSkipped,
            summary.TotalError,
            ErrorCodes = summary.Errors.Select(e => e.Code).ToArray(),
        };

        return new SyncRunLogEntry
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
            DetailJson = JsonSerializer.Serialize(detail),
            CreatedBy = "SyncV2",
        };
    }

    private async Task TryWriteFailureLogAsync(
        SyncSummaryDto summary,
        string errorMessage,
=======
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
>>>>>>> task5-phase-b3b-guarded-write-path
        CancellationToken cancellationToken)
    {
        try
        {
<<<<<<< HEAD
            await _logWriter.WriteAsync(ToLogEntry(summary, errorMessage), cancellationToken);
        }
        catch
        {
            // Preserve the original safe failure summary. Do not leak infrastructure details.
        }
    }

=======
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

>>>>>>> task5-phase-b3b-guarded-write-path
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
            ? "Da co cau hinh ket noi dung duoc. Dry-run khong ghi du lieu."
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

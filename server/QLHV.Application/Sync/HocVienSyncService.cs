using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;
using SyncExecutionOptions = QLHV.Application.Sync.Configuration.SyncExecutionOptions;

namespace QLHV.Application.Sync;

/// <summary>
/// Application service for one-way HocVien sync from CSDT_V2 to QLHV_APP.
/// Dry-run remains no-write. Manual execution is guarded by explicit server and request switches.
/// </summary>
public sealed class HocVienSyncService : IHocVienSyncService
{
    private readonly AppSyncOptions _options;
    private readonly SyncExecutionOptions _execution;
    private readonly IConnectionSettingsProvider _connections;
    private readonly IV2HocVienSourceRepository _v2Source;
    private readonly IQlhvHocVienTargetRepository _target;
    private readonly ISyncRunLogWriter _logWriter;

    public HocVienSyncService(
        IOptions<AppSyncOptions> options,
        IOptions<SyncExecutionOptions> execution,
        IConnectionSettingsProvider connections,
        IV2HocVienSourceRepository v2Source,
        IQlhvHocVienTargetRepository target,
        ISyncRunLogWriter logWriter)
    {
        _options = options.Value;
        _execution = execution.Value;
        _connections = connections;
        _v2Source = v2Source;
        _target = target;
        _logWriter = logWriter;
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
                const string issue = "Khong doc duoc so luong tu nguon CSDT_V2.";
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
        var warningCount = 0;
        var errors = new List<SyncErrorDto>();

        try
        {
            for (var offset = 0; remaining > 0; offset += batchSize)
            {
                var pageSize = Math.Min(batchSize, remaining);
                var rows = await _v2Source.ReadPageAsync(filter, offset, pageSize, cancellationToken);
                if (rows.Count == 0)
                {
                    break;
                }

                totalRead += rows.Count;
                remaining -= rows.Count;

                var models = new List<HocVienTargetWriteModel>(rows.Count);
                foreach (var row in rows)
                {
                    var mapped = HocVienSyncMapper.MapAndValidate(row);
                    warningCount += mapped.Warnings.Count;
                    if (mapped.ShouldSkip || mapped.Model is null)
                    {
                        totalSkipped++;
                        continue;
                    }

                    models.Add(mapped.Model);
                }

                if (models.Count == 0)
                {
                    continue;
                }

                var upsert = await _target.UpsertBatchAsync(models, cancellationToken);
                totalInserted += upsert.Inserted;
                totalUpdated += upsert.Updated;
                totalSkipped += upsert.Skipped;
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

            await TryWriteRunLogAsync(summary, warningCount, errorMessage: null, cancellationToken);

            return new HocVienSyncExecuteResultDto
            {
                Accepted = true,
                Status = "ThanhCong",
                Message = "Dong bo HocVien tu CSDT_V2 sang QLHV_APP da hoan tat.",
                Summary = summary,
            };
        }
        catch (Exception ex)
        {
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

            await TryWriteRunLogAsync(summary, warningCount, errors[0].Message, cancellationToken);

            return new HocVienSyncExecuteResultDto
            {
                Accepted = true,
                Status = "Loi",
                Message = errors[0].Message,
                Summary = summary,
                Issues = new[] { errors[0].Message },
            };
        }
    }

    private List<string> ValidateExecutionGuards(HocVienSyncExecuteRequest request)
    {
        var issues = new List<string>();

        if (!_execution.EnableTargetWrites)
        {
            issues.Add("EnableTargetWrites=false. Endpoint execute bi khoa va khong ghi du lieu.");
        }

        if (_execution.RequireManualConfirmation)
        {
            if (!request.ConfirmTargetWrites)
            {
                issues.Add("Thieu ConfirmTargetWrites=true.");
            }

            if (!string.Equals(
                    request.ConfirmationText,
                    _execution.ConfirmationPhrase,
                    StringComparison.Ordinal))
            {
                issues.Add($"ConfirmationText phai bang '{_execution.ConfirmationPhrase}'.");
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

    private static SyncRunLogEntry ToLogEntry(
        SyncSummaryDto summary,
        int warningCount,
        string? errorMessage)
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
            WarningCount = warningCount,
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

    private async Task TryWriteRunLogAsync(
        SyncSummaryDto summary,
        int warningCount,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await _logWriter.WriteAsync(ToLogEntry(summary, warningCount, errorMessage), cancellationToken);
        }
        catch
        {
            // Preserve the safe execution summary. Do not leak infrastructure details.
        }
    }

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

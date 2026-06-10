using Microsoft.Extensions.Options;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync;

/// <summary>
/// Application service for one-way HocVien sync from CSDT_V2 to QLHV_APP.
/// Dry-run remains no-write. Execute is guarded by SyncExecutionOptions and explicit confirmation.
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

    public async Task<SyncConfigCheckDto> ConfigCheckHocVienAsync(CancellationToken cancellationToken = default)
    {
        var qlhv = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        var v2 = await _connections.GetSourceConnectionAsync(SourceSystem.V2, cancellationToken);

        return new SyncConfigCheckDto
        {
            QlhvAppConfigured = qlhv.IsUsable,
            CsdtV2Configured = v2.IsUsable,
            EnableTargetWrites = _execution.EnableTargetWrites,
            RequireManualConfirmation = _execution.RequireManualConfirmation,
            AllowHangfireSchedule = _execution.AllowHangfireSchedule,
        };
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

    public async Task<SyncExecuteResultDto> ExecuteHocVienAsync(
        SyncExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new SyncExecuteRequest();
        var blocked = ValidateExecutionGuard(request);
        if (blocked is not null)
        {
            return blocked;
        }

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

                var batch = await _v2Source.ReadPageAsync(
                    HocVienSourceFilter.Empty,
                    offset,
                    Math.Max(1, _options.BatchSize),
                    cancellationToken);

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
                    var counts = await _target.UpsertBatchAsync(models, cancellationToken);
                    inserted += counts.Inserted;
                    updated += counts.Updated;
                    skipped += counts.Skipped;
                }

                if (batch.Count < Math.Max(1, _options.BatchSize))
                {
                    break;
                }

                offset += Math.Max(1, _options.BatchSize);
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

    private SyncExecuteResultDto? ValidateExecutionGuard(SyncExecuteRequest request)
    {
        if (!_execution.EnableTargetWrites)
        {
            return Blocked("Ghi bi chan: SyncExecution.EnableTargetWrites = false.");
        }

        if (_execution.RequireManualConfirmation)
        {
            if (!request.Confirm)
            {
                return Blocked("Thieu xac nhan: Confirm phai = true.");
            }

            var confirmationText = request.ConfirmationText ?? request.ConfirmationPhrase;
            if (!string.Equals(confirmationText, _execution.ConfirmationPhrase, StringComparison.Ordinal))
            {
                return Blocked("Chuoi xac nhan khong khop.");
            }
        }

        return null;
    }

    private static SyncSummaryDto BuildSummary(
        string status,
        int read,
        int inserted,
        int updated,
        int skipped,
        int error,
        DateTime startedAt,
        DateTime endedAt) => new()
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
            // Preserve the safe execution result. Do not leak infrastructure details.
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

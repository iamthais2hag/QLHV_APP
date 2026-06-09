using Microsoft.Extensions.Options;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync;

/// <summary>
/// Application service for one-way HocVien sync from CSDT_V2 to QLHV_APP.
/// Phase A only builds a safe dry-run plan. It does not open SQL connections or write data.
/// </summary>
public sealed class HocVienSyncService : IHocVienSyncService
{
    private readonly SyncOptions _options;
    private readonly IConnectionSettingsProvider _connections;

    public HocVienSyncService(IOptions<SyncOptions> options, IConnectionSettingsProvider connections)
    {
        _options = options.Value;
        _connections = connections;
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

        return new DryRunResultDto
        {
            CanRun = canRun,
            Status = canRun ? "SanSang" : "ThieuCauHinh",
            Issues = issues,
            Target = ToConnectionCheck(qlhv),
            Source = ToConnectionCheck(v2),
            PlannedSummary = new SyncSummaryDto
            {
                JobName = IHocVienSyncJob.JobName,
                EntityType = "HocVien",
                SourceSystem = "V2",
                IsDryRun = true,
                Status = canRun ? "DuKien" : "ThieuCauHinh",
                TotalRead = 0,
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

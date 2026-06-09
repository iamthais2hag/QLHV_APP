using Microsoft.Extensions.Logging;
using QLHV.Application.Sync;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Hangfire job entry point for HocVien sync. Phase A only calls dry-run and does not schedule this job.
/// </summary>
public sealed class HocVienSyncJob : IHocVienSyncJob
{
    private readonly IHocVienSyncService _syncService;
    private readonly ILogger<HocVienSyncJob> _logger;

    public HocVienSyncJob(IHocVienSyncService syncService, ILogger<HocVienSyncJob> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var result = await _syncService.DryRunHocVienAsync(cancellationToken);

        _logger.LogInformation(
            "Dry-run dong bo hoc vien: can run = {CanRun}, issue count = {IssueCount}, error count = {ErrorCount}.",
            result.CanRun,
            result.Issues.Count,
            result.PlannedSummary.Errors.Count);
    }
}

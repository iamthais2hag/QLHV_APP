namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Safe sync configuration status for local dry-run preparation.
/// Does not expose server, database, user, password, or full connection strings.
/// </summary>
public sealed class SyncConfigCheckDto
{
    public bool QlhvAppConfigured { get; init; }
    public bool CsdtV2Configured { get; init; }
    public bool DryRun { get; init; }
    public bool EnableTargetWrites { get; init; }
    public bool RequireManualConfirmation { get; init; }
    public bool AllowHangfireSchedule { get; init; }
}

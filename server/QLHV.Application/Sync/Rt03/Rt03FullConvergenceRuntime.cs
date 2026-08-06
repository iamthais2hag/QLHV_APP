namespace QLHV.Application.Sync.Rt03;

public sealed record Rt03FullConvergenceRecoveryRequest(
    Guid RecoveryId,
    string SourceProfileCode,
    long ExpectedCheckpoint,
    string ExpectedArtifactSha256);

public sealed record Rt03FullConvergenceRecoveryResult(
    Guid RecoveryId,
    string SourceProfileCode,
    long CheckpointBefore,
    long AnchorVersion,
    string Status,
    IReadOnlyList<Rt03RecoveryDomainResult> Domains,
    string VerificationHash,
    long PostAnchorPendingVersions);

public sealed record Rt03RecoveryDomainResult(
    string Domain,
    int SequenceOrder,
    long SourceRows,
    long InsertedRows,
    long UpdatedRows,
    long InactiveRows,
    long MissingRows,
    long ManualReviewRows,
    long NoChangeRows,
    string VerificationHash);

public sealed record Rt03RecoveryPreflightState(
    long CheckpointVersion,
    Guid SourceDatabaseGuid,
    bool AutoSyncInactive,
    bool FullSyncInactive,
    bool RecoverySchemaReady);

public interface IRt03FullConvergenceStateStore
{
    Task<Rt03RecoveryPreflightState> ReadPreflightAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken = default);

    Task BeginOrResumeAsync(
        Rt03FullConvergenceRecoveryRequest request,
        Guid sourceDatabaseGuid,
        long anchorVersion,
        string mappingFingerprint,
        string sourceSchemaFingerprint,
        CancellationToken cancellationToken = default);

    Task RecordDomainAsync(
        Guid recoveryId,
        Rt03RecoveryDomainResult result,
        CancellationToken cancellationToken = default);

    Task MarkVerifiedAsync(
        Guid recoveryId,
        CancellationToken cancellationToken = default);

    Task FinalizeAsync(
        Guid recoveryId,
        string verificationHash,
        CancellationToken cancellationToken = default);
}

public interface IRt03FullConvergenceRecoveryService
{
    Task<Rt03FullConvergenceRecoveryResult> ExecuteAsync(
        Rt03FullConvergenceRecoveryRequest request,
        CancellationToken cancellationToken = default);
}

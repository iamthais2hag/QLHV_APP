using System.Data.Common;

namespace QLHV.Application.Sync.Realtime.ControlPlane;

public static class CsdtControlPlaneCatalog
{
    public static IReadOnlySet<string> TableNames { get; } =
        new HashSet<string>(
            [
                "DM_DonViGTVT",
                "GiaoVien",
                "KhoaHoc",
                "KhoaHoc_GiaoVien",
                "BaoCaoI",
                "NguoiLX",
                "NguoiLX_HoSo",
                "NguoiLXHS_GiayTo",
            ],
            StringComparer.Ordinal);

    public static void ValidateRoute(MembershipRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var routeAllowed = (
                route.TargetProfile,
                route.SourceProfile,
                route.StreamCode,
                route.MaCsdt) switch
        {
            ("OTO_V1", "OTO_V2", "OTO_V2_TO_V1", "66029") => true,
            ("OTO_V1_BAK", "OTO_V2_BAK", "OTO_V2_TO_V1", "66029") => true,
            ("MOTO_V1", "MOTO_V2", "MOTO_V2_TO_V1", "66030") => true,
            ("MOTO_V1_BAK", "MOTO_V2_BAK", "MOTO_V2_TO_V1", "66030") => true,
            _ => false,
        };
        if (!routeAllowed || !TableNames.Contains(route.TableName))
        {
            throw new ArgumentException(
                "Control-plane route or table is outside the fixed allowlist.",
                nameof(route));
        }
    }
}

public sealed record CreateMembershipRequest(
    MembershipRoute Route,
    CanonicalBusinessKey CanonicalBusinessKey,
    TargetEqualityKey TargetEqualityKey,
    TypedTargetKeyClaim TypedTargetKey,
    DiagnosticKeyHash DiagnosticKeyHash,
    long SourceVersion,
    Guid CycleId,
    ControlPlaneFingerprint MappingFingerprint,
    ControlPlaneFingerprint RouteFingerprint);

public sealed record MembershipTransitionRequest(
    long MembershipId,
    Guid CycleId,
    long SourceVersion,
    SourceMembershipReasonCode ReasonCode,
    SourceMembershipTargetAction TargetAction,
    ControlPlaneFingerprint MappingFingerprint,
    ControlPlaneFingerprint RouteFingerprint);

public sealed record MembershipJournalEntry(
    long MembershipId,
    Guid CycleId,
    SourceMembershipStatus? BeforeStatus,
    SourceMembershipStatus AfterStatus,
    long SourceVersion,
    SourceMembershipReasonCode ReasonCode,
    SourceMembershipTargetAction TargetAction,
    ControlPlaneFingerprint MappingFingerprint,
    ControlPlaneFingerprint RouteFingerprint,
    DiagnosticKeyHash DiagnosticKeyHash);

public sealed record CreateSyncCycleRequest(
    Guid CycleId,
    MembershipRoute Route,
    long StartSourceVersion,
    long EndSourceVersion,
    int EnabledDomainCount,
    ControlPlaneFingerprint MappingFingerprint,
    ControlPlaneFingerprint RouteFingerprint,
    ControlPlaneFingerprint SourceSchemaFingerprint,
    ControlPlaneFingerprint TargetSchemaFingerprint);

public sealed record SyncCycleDomainResult(
    Guid CycleId,
    string DomainName,
    SyncCycleDomainStatus Status,
    long SourceRowCount,
    long InsertCount,
    long UpdateCount,
    long DeleteCount,
    long PreservedExcludedCount,
    long ConflictCount,
    ControlPlaneFingerprint SourceKeySetHash,
    ControlPlaneFingerprint? ResultHash,
    string? ErrorCode,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record CoverageMarkerRequest(
    MembershipRoute Route,
    long BaselineSourceVersion,
    ControlPlaneFingerprint MappingFingerprint,
    ControlPlaneFingerprint RouteFingerprint,
    ControlPlaneFingerprint SourceKeySetHash,
    long MembershipCount,
    bool IsComplete,
    Guid? CompletedCycleId,
    ControlPlaneFingerprint SourceSchemaFingerprint,
    ControlPlaneFingerprint TargetSchemaFingerprint);

public sealed record SourceMembershipRecord(
    long MembershipId,
    SourceMembershipStatus Status,
    bool IsActive,
    bool ClaimsTargetKey,
    bool OwnershipReserved,
    long LastObservedSourceVersion,
    long? AppliedSourceVersion,
    long? DeletedAtSourceVersion,
    long? ReactivatedAtSourceVersion,
    int OwnershipEpoch,
    ushort KeySchemaVersion,
    ControlPlaneFingerprint MappingFingerprint,
    ControlPlaneFingerprint RouteFingerprint);

public interface ICsdtRealtimeTargetControlPlaneRepository
{
    Task<long> CreateInsertPendingAsync(
        DbConnection connection,
        DbTransaction transaction,
        CreateMembershipRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> ApplyActiveAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipTransitionRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> ObserveActiveAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipTransitionRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> CreateDeletePendingAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipTransitionRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> ApplyInactiveAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipTransitionRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> CreateReactivatePendingAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipTransitionRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> ApplyReactivatedAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipTransitionRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> MarkConflictAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipTransitionRequest request,
        CancellationToken cancellationToken = default);

    Task AppendTransitionJournalAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipJournalEntry entry,
        CancellationToken cancellationToken = default);

    Task CreateCycleAsync(
        DbConnection connection,
        DbTransaction transaction,
        CreateSyncCycleRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> MarkCycleStagedAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        ControlPlaneFingerprint stagedKeySetHash,
        CancellationToken cancellationToken = default);

    Task<bool> MarkCycleValidatedAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        CancellationToken cancellationToken = default);

    Task<bool> MarkTargetCommittingAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        CancellationToken cancellationToken = default);

    Task<bool> MarkTargetCommittedAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        CancellationToken cancellationToken = default);

    Task<bool> MarkCheckpointPublishedAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        CancellationToken cancellationToken = default);

    Task<bool> MarkCycleCompleteAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        CancellationToken cancellationToken = default);

    Task<CsdtTargetCycleCommitMarker?> ReadCycleMarkerAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        CancellationToken cancellationToken = default);

    Task<bool> MarkCycleFailedOrConflictAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        SyncCycleStatus terminalStatus,
        string errorCode,
        CancellationToken cancellationToken = default);

    Task UpsertCycleDomainResultAsync(
        DbConnection connection,
        DbTransaction transaction,
        SyncCycleDomainResult result,
        CancellationToken cancellationToken = default);

    Task UpsertCoverageMarkerAsync(
        DbConnection connection,
        DbTransaction transaction,
        CoverageMarkerRequest request,
        CancellationToken cancellationToken = default);

    Task<StreamCoverageState?> ReadCoverageStatusAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipRoute route,
        CancellationToken cancellationToken = default);

    Task<SourceMembershipRecord?> ReadActiveMembershipAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipRoute route,
        CanonicalBusinessKey canonicalBusinessKey,
        CancellationToken cancellationToken = default);

    Task<SourceMembershipRecord?> ReadMembershipAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipRoute route,
        CanonicalBusinessKey canonicalBusinessKey,
        CancellationToken cancellationToken = default);

    Task<OwnershipReservation?> ReadOwnershipReservationAsync(
        DbConnection connection,
        DbTransaction transaction,
        string targetProfile,
        TypedTargetKeyClaim typedTargetKey,
        CancellationToken cancellationToken = default);
}

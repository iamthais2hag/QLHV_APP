using System.Data.Common;
using QLHV.Application.Sync.Realtime.ControlPlane;

namespace QLHV.Infrastructure.Sync.Realtime.ControlPlane;

/// <summary>
/// Resolves a CT technical key through the authoritative typed SQL ownership
/// relation inside a caller-owned transaction. It never mutates target data or
/// advances a checkpoint and is intentionally not production-wired.
/// </summary>
internal sealed class CsdtSqlTombstoneOwnershipResolver
{
    private readonly ICsdtRealtimeTargetControlPlaneRepository _repository;

    internal CsdtSqlTombstoneOwnershipResolver(
        ICsdtRealtimeTargetControlPlaneRepository repository)
    {
        _repository = repository;
    }

    internal async Task<CsdtTombstoneOwnershipResolution> ResolveAsync(
        DbConnection connection,
        DbTransaction transaction,
        CsdtTombstoneOwnershipRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reservation = await _repository.ReadOwnershipReservationAsync(
            connection,
            transaction,
            request.Route.TargetProfile,
            request.TypedTargetKey,
            cancellationToken);
        if (reservation is null)
        {
            return CsdtTombstoneOwnershipResolver.Resolve(request, []);
        }

        var ownerRoute = new MembershipRoute(
            reservation.TargetProfile,
            reservation.SourceProfile,
            reservation.StreamCode,
            reservation.MaCsdt,
            reservation.TableName);
        var canonical = CsdtTypedKeyCanonicalizer.Canonicalize(
            request.TypedTargetKey);
        SourceMembershipRecord? membership;
        try
        {
            membership = await _repository.ReadMembershipAsync(
                connection,
                transaction,
                ownerRoute,
                canonical,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is ArgumentException or CsdtAtomicCycleException)
        {
            return CsdtTombstoneOwnershipResolver.Resolve(
                request,
                [ConflictingEvidence(request, reservation, canonical)]);
        }

        if (membership is null ||
            membership.MembershipId != reservation.MembershipId)
        {
            return CsdtTombstoneOwnershipResolver.Resolve(
                request,
                [ConflictingEvidence(request, reservation, canonical)]);
        }

        var evidence = new CsdtMembershipEvidence(
            membership.MembershipId,
            ownerRoute,
            new CsdtProtectedMembershipKey(canonical),
            request.TypedTargetKey,
            membership.Status,
            membership.AppliedSourceVersion.HasValue &&
            membership.Status is
                SourceMembershipStatus.Active or
                SourceMembershipStatus.Inactive,
            membership.OwnershipReserved,
            membership.LastObservedSourceVersion,
            membership.AppliedSourceVersion,
            membership.DeletedAtSourceVersion,
            membership.ReactivatedAtSourceVersion,
            membership.MappingFingerprint,
            membership.RouteFingerprint);
        return CsdtTombstoneOwnershipResolver.Resolve(request, [evidence]);
    }

    private static CsdtMembershipEvidence ConflictingEvidence(
        CsdtTombstoneOwnershipRequest request,
        OwnershipReservation reservation,
        CanonicalBusinessKey canonical)
        => new(
            reservation.MembershipId,
            new MembershipRoute(
                reservation.TargetProfile,
                reservation.SourceProfile,
                reservation.StreamCode,
                reservation.MaCsdt,
                reservation.TableName),
            new CsdtProtectedMembershipKey(canonical),
            request.TypedTargetKey,
            SourceMembershipStatus.Conflict,
            IsApplied: false,
            reservation.OwnershipReserved,
            request.SourceVersion,
            AppliedSourceVersion: null,
            DeletedAtSourceVersion: null,
            ReactivatedAtSourceVersion: null,
            request.MappingFingerprint,
            request.RouteFingerprint);
}

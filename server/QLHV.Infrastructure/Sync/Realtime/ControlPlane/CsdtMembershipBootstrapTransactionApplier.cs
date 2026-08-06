using System.Data.Common;
using Dapper;
using QLHV.Application.Sync.Realtime.ControlPlane;

namespace QLHV.Infrastructure.Sync.Realtime.ControlPlane;

internal sealed record CsdtMembershipBootstrapDomainCommit(
    string DomainName,
    long ActiveAppliedMembershipCount,
    long TypedOwnershipClaimCount,
    long MembershipCreateCount,
    long ExistingVerifiedCount,
    long ConflictCount,
    long UnclassifiedTargetOnlyCount,
    long AbsenceCandidateCount);

/// <summary>
/// Adds membership and typed ownership claims to the already caller-owned
/// atomic target transaction. This class never opens, commits, or rolls back a
/// transaction and is intentionally absent from production DI.
/// </summary>
internal sealed class CsdtMembershipBootstrapTransactionApplier
{
    private readonly ICsdtRealtimeTargetControlPlaneRepository _repository;
    private readonly HmacSha256DiagnosticKeyHasher _diagnosticHasher;

    internal CsdtMembershipBootstrapTransactionApplier(
        ICsdtRealtimeTargetControlPlaneRepository repository,
        HmacSha256DiagnosticKeyHasher diagnosticHasher)
    {
        _repository = repository;
        _diagnosticHasher = diagnosticHasher;
    }

    internal async Task<CsdtMembershipBootstrapDomainCommit> ApplyDomainAsync(
        DbConnection connection,
        DbTransaction transaction,
        CsdtStagedCycle staged,
        CsdtStagedDomain domain,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(domain);
        if (domain.OperationMode != CsdtAtomicOperationMode.FullSnapshot)
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.SourceStageIncomplete);
        }

        var created = 0L;
        var verified = 0L;
        var claims = 0L;
        var active = 0L;
        foreach (var row in domain.Rows)
        {
            await RequireParentsAsync(
                connection,
                transaction,
                staged,
                domain.DomainName,
                row,
                cancellationToken);
            var route = Route(staged, domain.DomainName);
            var canonical = CanonicalBusinessKey.FromEncoded(
                row.CopyCanonicalKey());
            var typed = CsdtTypedKeyCanonicalizer.FromStagedRow(
                domain.DomainName,
                row);
            typed.ValidateForRoute(route);
            var diagnostic = await _diagnosticHasher.ComputeAsync(
                route,
                canonical,
                cancellationToken);
            var owner = await _repository.ReadOwnershipReservationAsync(
                connection,
                transaction,
                staged.TargetProfile,
                typed,
                cancellationToken);
            var membership = await _repository.ReadMembershipAsync(
                connection,
                transaction,
                route,
                canonical,
                cancellationToken);

            if (owner is not null &&
                (!string.Equals(
                     owner.TargetProfile,
                     route.TargetProfile,
                     StringComparison.Ordinal) ||
                 !string.Equals(
                     owner.SourceProfile,
                     route.SourceProfile,
                     StringComparison.Ordinal) ||
                 !string.Equals(
                     owner.StreamCode,
                     route.StreamCode,
                     StringComparison.Ordinal) ||
                 !string.Equals(
                     owner.TableName,
                     route.TableName,
                     StringComparison.Ordinal) ||
                 !owner.OwnershipReserved ||
                 membership?.MembershipId != owner.MembershipId))
            {
                throw new CsdtAtomicCycleException(
                    CsdtMembershipReasonCodes.OwnershipConflict);
            }

            if (membership is null)
            {
                if (owner is not null)
                {
                    throw new CsdtAtomicCycleException(
                        CsdtMembershipReasonCodes.OwnershipConflict);
                }

                var membershipId = await _repository.CreateInsertPendingAsync(
                    connection,
                    transaction,
                    new CreateMembershipRequest(
                        route,
                        canonical,
                        TargetEqualityKey.ForTypedOwnershipClaim(
                            canonical.ToArray()),
                        typed,
                        diagnostic,
                        staged.EndSourceVersion,
                        staged.CycleId,
                        staged.MappingFingerprint,
                        staged.RouteFingerprint),
                    cancellationToken);
                await _repository.AppendTransitionJournalAsync(
                    connection,
                    transaction,
                    new MembershipJournalEntry(
                        membershipId,
                        staged.CycleId,
                        BeforeStatus: null,
                        SourceMembershipStatus.InsertPending,
                        staged.EndSourceVersion,
                        SourceMembershipReasonCode.BootstrapMembershipCreated,
                        SourceMembershipTargetAction.None,
                        staged.MappingFingerprint,
                        staged.RouteFingerprint,
                        diagnostic),
                    cancellationToken);
                var activated = await _repository.ApplyActiveAsync(
                    connection,
                    transaction,
                    Transition(
                        membershipId,
                        staged,
                        SourceMembershipReasonCode.BootstrapMembershipCreated,
                        SourceMembershipTargetAction.Upserted),
                    cancellationToken);
                if (!activated)
                {
                    throw new CsdtAtomicCycleException(
                        CsdtMembershipReasonCodes.BootstrapIncomplete);
                }

                await _repository.AppendTransitionJournalAsync(
                    connection,
                    transaction,
                    new MembershipJournalEntry(
                        membershipId,
                        staged.CycleId,
                        SourceMembershipStatus.InsertPending,
                        SourceMembershipStatus.Active,
                        staged.EndSourceVersion,
                        SourceMembershipReasonCode.BootstrapMembershipCreated,
                        SourceMembershipTargetAction.Upserted,
                        staged.MappingFingerprint,
                        staged.RouteFingerprint,
                        diagnostic),
                    cancellationToken);
                created++;
                claims++;
                active++;
                continue;
            }

            if (membership.Status == SourceMembershipStatus.Inactive)
            {
                throw new CsdtAtomicCycleException(
                    CsdtMembershipReasonCodes.ReactivationCandidate);
            }

            if (membership.Status != SourceMembershipStatus.Active ||
                !membership.IsActive ||
                !membership.OwnershipReserved ||
                !membership.MappingFingerprint.Equals(staged.MappingFingerprint) ||
                !membership.RouteFingerprint.Equals(staged.RouteFingerprint) ||
                owner is null)
            {
                throw new CsdtAtomicCycleException(
                    CsdtMembershipReasonCodes.OwnershipConflict);
            }

            if (membership.LastObservedSourceVersion < staged.EndSourceVersion)
            {
                var observed = await _repository.ObserveActiveAsync(
                    connection,
                    transaction,
                    Transition(
                        membership.MembershipId,
                        staged,
                        SourceMembershipReasonCode.SourceRowObserved,
                        SourceMembershipTargetAction.ExistingVerified),
                    cancellationToken);
                if (!observed)
                {
                    throw new CsdtAtomicCycleException(
                        CsdtMembershipReasonCodes.BootstrapIncomplete);
                }
            }
            else if (membership.LastObservedSourceVersion > staged.EndSourceVersion)
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.CheckpointStale);
            }

            await _repository.AppendTransitionJournalAsync(
                connection,
                transaction,
                new MembershipJournalEntry(
                    membership.MembershipId,
                    staged.CycleId,
                    SourceMembershipStatus.Active,
                    SourceMembershipStatus.Active,
                    staged.EndSourceVersion,
                    SourceMembershipReasonCode.BootstrapMembershipVerified,
                    SourceMembershipTargetAction.ExistingVerified,
                    staged.MappingFingerprint,
                    staged.RouteFingerprint,
                    diagnostic),
                cancellationToken);
            verified++;
            claims++;
            active++;
        }

        if (active != domain.SourceRowCount ||
            claims != domain.SourceRowCount)
        {
            throw new CsdtAtomicCycleException(
                CsdtMembershipReasonCodes.BootstrapIncomplete);
        }

        var targetOnly = await ReadTargetOnlyCountsAsync(
            connection,
            transaction,
            staged,
            domain.DomainName,
            cancellationToken);
        if (targetOnly.Unclassified != 0 ||
            targetOnly.Conflicts != 0 ||
            targetOnly.ActiveAbsenceCandidates != 0)
        {
            throw new CsdtAtomicCycleException(
                targetOnly.ActiveAbsenceCandidates != 0
                    ? CsdtAtomicCycleErrorCodes.DeleteExecutionNotEnabled
                    : CsdtAtomicCycleErrorCodes.CoverageIncomplete);
        }

        return new CsdtMembershipBootstrapDomainCommit(
            domain.DomainName,
            active,
            claims,
            created,
            verified,
            targetOnly.Conflicts,
            targetOnly.Unclassified,
            targetOnly.ActiveAbsenceCandidates);
    }

    internal Task WriteCoverageAsync(
        DbConnection connection,
        DbTransaction transaction,
        CsdtStagedCycle staged,
        CsdtStagedDomain domain,
        CsdtMembershipBootstrapDomainCommit commit,
        CancellationToken cancellationToken)
    {
        var complete =
            commit.ActiveAppliedMembershipCount == domain.SourceRowCount &&
            commit.TypedOwnershipClaimCount == domain.SourceRowCount &&
            commit.ConflictCount == 0 &&
            commit.UnclassifiedTargetOnlyCount == 0 &&
            commit.AbsenceCandidateCount == 0;
        if (!complete)
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.CoverageIncomplete);
        }

        return _repository.UpsertCoverageMarkerAsync(
            connection,
            transaction,
            new CoverageMarkerRequest(
                Route(staged, domain.DomainName),
                staged.EndSourceVersion,
                staged.MappingFingerprint,
                staged.RouteFingerprint,
                domain.SourceKeySetHash,
                domain.SourceRowCount,
                IsComplete: true,
                staged.CycleId,
                staged.SourceSchemaFingerprint,
                staged.TargetSchemaFingerprint),
            cancellationToken);
    }

    private async Task RequireParentsAsync(
        DbConnection connection,
        DbTransaction transaction,
        CsdtStagedCycle staged,
        string domainName,
        CsdtStagedRow row,
        CancellationToken cancellationToken)
    {
        foreach (var parent in ParentKeys(staged, domainName, row))
        {
            var membership = await _repository.ReadMembershipAsync(
                connection,
                transaction,
                parent.Route,
                parent.Key,
                cancellationToken);
            if (membership is null ||
                membership.Status != SourceMembershipStatus.Active ||
                !membership.IsActive ||
                !membership.OwnershipReserved ||
                membership.AppliedSourceVersion != staged.EndSourceVersion)
            {
                throw new CsdtAtomicCycleException(
                    CsdtMembershipReasonCodes.BootstrapParentMissing);
            }
        }
    }

    private static async Task<TargetOnlyCounts> ReadTargetOnlyCountsAsync(
        DbConnection connection,
        DbTransaction transaction,
        CsdtStagedCycle staged,
        string domainName,
        CancellationToken cancellationToken)
    {
        var query = domainName switch
        {
            "DM_DonViGTVT" => """
                SELECT
                    SUM(CASE WHEN membership.MembershipId IS NULL THEN 1 ELSE 0 END) AS Unclassified,
                    SUM(CASE WHEN membership.MembershipId IS NOT NULL AND
                        (membership.TargetProfile <> @TargetProfile OR
                         membership.SourceProfile <> @SourceProfile OR
                         membership.StreamCode <> @StreamCode OR
                         membership.OwnershipReserved <> 1)
                        THEN 1 ELSE 0 END) AS Conflicts,
                    SUM(CASE WHEN membership.TargetProfile = @TargetProfile AND
                        membership.SourceProfile = @SourceProfile AND
                        membership.StreamCode = @StreamCode AND
                        membership.MembershipStatus = 'ACTIVE' AND
                        membership.LastSeenCycleId <> @CycleId
                        THEN 1 ELSE 0 END) AS ActiveAbsenceCandidates
                FROM dbo.DM_DonViGTVT AS targetRow
                LEFT JOIN dbo.QLHV_CsdtRealtimeOwnershipClaim AS claim
                  ON claim.TargetProfile = @TargetProfile
                 AND claim.TableName = 'DM_DonViGTVT'
                 AND claim.DmDonViGtvtMaDV = targetRow.MaDV
                LEFT JOIN dbo.QLHV_CsdtRealtimeSourceMembership AS membership
                  ON membership.MembershipId = claim.MembershipId
                WHERE targetRow.MaDV = @MaCSDT;
                """,
            "KhoaHoc" => QueryFor(
                "dbo.KhoaHoc AS targetRow",
                "targetRow.MaCSDT = @MaCSDT",
                "KhoaHoc",
                "claim.KhoaHocMaKH = targetRow.MaKH"),
            "BaoCaoI" => QueryFor(
                "dbo.BaoCaoI AS targetRow",
                "targetRow.MaCSDT = @MaCSDT",
                "BaoCaoI",
                "claim.BaoCaoIMaBCI = targetRow.MaBCI"),
            "NguoiLX" => QueryFor(
                "dbo.NguoiLX AS targetRow",
                "targetRow.DonViNhanHSo = @MaCSDT",
                "NguoiLX",
                "claim.NguoiLXMaDK = targetRow.MaDK"),
            "NguoiLX_HoSo" => QueryFor(
                "dbo.NguoiLX_HoSo AS targetRow",
                "targetRow.MaCSDT = @MaCSDT",
                "NguoiLX_HoSo",
                "claim.NguoiLXHoSoMaDK = targetRow.MaDK"),
            "NguoiLXHS_GiayTo" => QueryFor(
                "dbo.NguoiLXHS_GiayTo AS targetRow INNER JOIN dbo.NguoiLX_HoSo AS dossier ON dossier.MaDK = targetRow.MaDK",
                "dossier.MaCSDT = @MaCSDT",
                "NguoiLXHS_GiayTo",
                "claim.GiayToMaGT = targetRow.MaGT AND claim.GiayToMaDK = targetRow.MaDK"),
            _ => throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.AtomicCoreScopeMismatch),
        };
        var row = await connection.QuerySingleAsync<TargetOnlyCounts>(
            new CommandDefinition(
                query,
                new
                {
                    staged.TargetProfile,
                    staged.SourceProfile,
                    staged.StreamCode,
                    staged.MaCsdt,
                    staged.CycleId,
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        return row;
    }

    private static string QueryFor(
        string targetExpression,
        string scopePredicate,
        string tableName,
        string claimJoin)
        => $"""
            SELECT
                SUM(CASE WHEN membership.MembershipId IS NULL THEN 1 ELSE 0 END) AS Unclassified,
                SUM(CASE WHEN membership.MembershipId IS NOT NULL AND
                    (membership.TargetProfile <> @TargetProfile OR
                     membership.SourceProfile <> @SourceProfile OR
                     membership.StreamCode <> @StreamCode OR
                     membership.OwnershipReserved <> 1)
                    THEN 1 ELSE 0 END) AS Conflicts,
                SUM(CASE WHEN membership.TargetProfile = @TargetProfile AND
                    membership.SourceProfile = @SourceProfile AND
                    membership.StreamCode = @StreamCode AND
                    membership.MembershipStatus = 'ACTIVE' AND
                    membership.LastSeenCycleId <> @CycleId
                    THEN 1 ELSE 0 END) AS ActiveAbsenceCandidates
            FROM {targetExpression}
            LEFT JOIN dbo.QLHV_CsdtRealtimeOwnershipClaim AS claim
              ON claim.TargetProfile = @TargetProfile
             AND claim.TableName = '{tableName}'
             AND {claimJoin}
            LEFT JOIN dbo.QLHV_CsdtRealtimeSourceMembership AS membership
              ON membership.MembershipId = claim.MembershipId
            WHERE {scopePredicate};
            """;

    private static IEnumerable<ParentKey> ParentKeys(
        CsdtStagedCycle staged,
        string domainName,
        CsdtStagedRow row)
    {
        CanonicalBusinessKey Text(string value)
            => CanonicalBusinessKeyEncoder.Encode(
                1,
                CanonicalKeyComponent.FromString(value));
        string Read(string column)
            => Convert.ToString(
                   row.ReadValue(column),
                   System.Globalization.CultureInfo.InvariantCulture) ??
               throw new CsdtAtomicCycleException(
                   CsdtMembershipReasonCodes.BootstrapParentMissing);
        ParentKey Parent(string table, string value)
            => new(Route(staged, table), Text(value));

        return domainName switch
        {
            "DM_DonViGTVT" => [],
            "KhoaHoc" => [Parent("DM_DonViGTVT", Read("MaCSDT"))],
            "BaoCaoI" =>
            [
                Parent("DM_DonViGTVT", Read("MaCSDT")),
                Parent("KhoaHoc", Read("MaKH")),
            ],
            "NguoiLX" =>
            [
                Parent("DM_DonViGTVT", Read("DonViNhanHSo")),
            ],
            "NguoiLX_HoSo" => DossierParents(),
            "NguoiLXHS_GiayTo" =>
            [
                Parent("NguoiLX_HoSo", Read("MaDK")),
            ],
            _ => throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.AtomicCoreScopeMismatch),
        };

        IEnumerable<ParentKey> DossierParents()
        {
            yield return Parent("DM_DonViGTVT", Read("MaCSDT"));
            yield return Parent("NguoiLX", Read("MaDK"));
            yield return Parent("KhoaHoc", Read("MaKhoaHoc"));
            var report = Read("MaBC1");
            if (!string.IsNullOrWhiteSpace(report))
            {
                yield return Parent("BaoCaoI", report);
            }
        }
    }

    private static MembershipTransitionRequest Transition(
        long membershipId,
        CsdtStagedCycle staged,
        SourceMembershipReasonCode reason,
        SourceMembershipTargetAction action)
        => new(
            membershipId,
            staged.CycleId,
            staged.EndSourceVersion,
            reason,
            action,
            staged.MappingFingerprint,
            staged.RouteFingerprint);

    private static MembershipRoute Route(
        CsdtStagedCycle staged,
        string tableName)
        => new(
            staged.TargetProfile,
            staged.SourceProfile,
            staged.StreamCode,
            staged.MaCsdt,
            tableName);

    private sealed record ParentKey(
        MembershipRoute Route,
        CanonicalBusinessKey Key);

    private sealed class TargetOnlyCounts
    {
        public long Unclassified { get; init; }
        public long Conflicts { get; init; }
        public long ActiveAbsenceCandidates { get; init; }
    }
}

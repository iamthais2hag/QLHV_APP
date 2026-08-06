using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync.Realtime.ControlPlane;

namespace QLHV.Infrastructure.Sync.Realtime.ControlPlane;

internal sealed class CsdtRealtimeTargetControlPlaneRepository :
    ICsdtRealtimeTargetControlPlaneRepository
{
    public async Task<long> CreateInsertPendingAsync(
        DbConnection connection,
        DbTransaction transaction,
        CreateMembershipRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(request);
        CsdtControlPlaneCatalog.ValidateRoute(request.Route);
        request.TargetEqualityKey.EnsureTypedClaimForMutation();
        request.TypedTargetKey.ValidateForRoute(request.Route);
        ValidateSourceVersion(request.SourceVersion);
        ValidateKeyLengths(request.CanonicalBusinessKey, request.TargetEqualityKey);

        try
        {
            return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                CreateInsertPendingSql,
                new
                {
                    request.Route.TargetProfile,
                    request.Route.SourceProfile,
                    request.Route.StreamCode,
                    MaCSDT = request.Route.MaCsdt,
                    request.Route.TableName,
                    KeySchemaVersion = checked((short)request.CanonicalBusinessKey.SchemaVersion),
                    CanonicalBusinessKey = request.CanonicalBusinessKey.ToArray(),
                    TargetEqualityKey = request.TargetEqualityKey.ToArray(),
                    TargetEqualityProofStatus = TargetEqualityProof.ProofStatus,
                    TargetEqualityProofId = TargetEqualityProof.ProofId,
                    TargetEqualityProofVersion = checked((short)TargetEqualityProof.Version),
                    CanonicalBusinessKeyHash = request.DiagnosticKeyHash.ToArray(),
                    HashKeyVersion = request.DiagnosticKeyHash.KeyVersion,
                    request.TypedTargetKey.DmDonViGtvtMaDv,
                    request.TypedTargetKey.GiaoVienMaGv,
                    request.TypedTargetKey.KhoaHocMaKh,
                    request.TypedTargetKey.KhoaHocGiaoVienMaLichLv,
                    request.TypedTargetKey.BaoCaoIMaBci,
                    request.TypedTargetKey.NguoiLxMaDk,
                    request.TypedTargetKey.NguoiLxHoSoMaDk,
                    request.TypedTargetKey.GiayToMaGt,
                    request.TypedTargetKey.GiayToMaDk,
                    request.SourceVersion,
                    request.CycleId,
                    MappingFingerprint = request.MappingFingerprint.ToArray(),
                    RouteFingerprint = request.RouteFingerprint.ToArray(),
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            throw new ControlPlaneOwnershipConflictException();
        }
    }

    public Task<bool> ApplyActiveAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipTransitionRequest request,
        CancellationToken cancellationToken = default)
        => ExecuteMembershipTransitionAsync(
            connection,
            transaction,
            ApplyActiveSql,
            request,
            cancellationToken);

    public Task<bool> ObserveActiveAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipTransitionRequest request,
        CancellationToken cancellationToken = default)
        => ExecuteMembershipTransitionAsync(
            connection,
            transaction,
            ObserveActiveSql,
            request,
            cancellationToken);

    public Task<bool> CreateDeletePendingAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ReasonCode is not (
                SourceMembershipReasonCode.SourceDelete or
                SourceMembershipReasonCode.FullReconcileAbsent))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        return ExecuteMembershipTransitionAsync(
            connection,
            transaction,
            CreateDeletePendingSql,
            request,
            cancellationToken);
    }

    public Task<bool> ApplyInactiveAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.TargetAction is not (
                SourceMembershipTargetAction.HardDeleted or
                SourceMembershipTargetAction.PreservedExcluded))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        return ExecuteMembershipTransitionAsync(
            connection,
            transaction,
            ApplyInactiveSql,
            request,
            cancellationToken);
    }

    public Task<bool> CreateReactivatePendingAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipTransitionRequest request,
        CancellationToken cancellationToken = default)
        => ExecuteMembershipTransitionAsync(
            connection,
            transaction,
            CreateReactivatePendingSql,
            request,
            cancellationToken);

    public Task<bool> ApplyReactivatedAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipTransitionRequest request,
        CancellationToken cancellationToken = default)
        => ExecuteMembershipTransitionAsync(
            connection,
            transaction,
            ApplyReactivatedSql,
            request,
            cancellationToken);

    public Task<bool> MarkConflictAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        CsdtControlPlaneSqlTokens.RequireConflictReason(request.ReasonCode);
        return ExecuteMembershipTransitionAsync(
            connection,
            transaction,
            MarkConflictSql,
            request,
            cancellationToken);
    }

    public async Task AppendTransitionJournalAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipJournalEntry entry,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(entry);
        ValidateSourceVersion(entry.SourceVersion);
        await connection.ExecuteAsync(new CommandDefinition(
            AppendJournalSql,
            new
            {
                entry.MembershipId,
                entry.CycleId,
                BeforeStatus = entry.BeforeStatus.HasValue
                    ? CsdtControlPlaneSqlTokens.MembershipStatus(entry.BeforeStatus.Value)
                    : "ABSENT",
                AfterStatus = CsdtControlPlaneSqlTokens.MembershipStatus(entry.AfterStatus),
                entry.SourceVersion,
                ReasonCode = CsdtControlPlaneSqlTokens.Reason(entry.ReasonCode),
                TargetAction = CsdtControlPlaneSqlTokens.TargetAction(entry.TargetAction),
                MappingFingerprint = entry.MappingFingerprint.ToArray(),
                RouteFingerprint = entry.RouteFingerprint.ToArray(),
                DiagnosticKeyHash = entry.DiagnosticKeyHash.ToArray(),
                HashKeyVersion = entry.DiagnosticKeyHash.KeyVersion,
            },
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    public async Task CreateCycleAsync(
        DbConnection connection,
        DbTransaction transaction,
        CreateSyncCycleRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(request);
        CsdtControlPlaneCatalog.ValidateRoute(request.Route);
        ValidateSourceVersion(request.StartSourceVersion);
        ValidateSourceVersion(request.EndSourceVersion);
        if (request.EndSourceVersion < request.StartSourceVersion)
        {
            throw new ArgumentException("Cycle end source version cannot regress.", nameof(request));
        }

        if (request.EnabledDomainCount is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            CreateCycleSql,
            new
            {
                request.CycleId,
                request.Route.TargetProfile,
                request.Route.SourceProfile,
                request.Route.StreamCode,
                MaCSDT = request.Route.MaCsdt,
                request.StartSourceVersion,
                request.EndSourceVersion,
                request.EnabledDomainCount,
                MappingFingerprint = request.MappingFingerprint.ToArray(),
                RouteFingerprint = request.RouteFingerprint.ToArray(),
                SourceSchemaFingerprint = request.SourceSchemaFingerprint.ToArray(),
                TargetSchemaFingerprint = request.TargetSchemaFingerprint.ToArray(),
            },
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    public Task<bool> MarkCycleStagedAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        ControlPlaneFingerprint stagedKeySetHash,
        CancellationToken cancellationToken = default)
        => TransitionCycleAsync(
            connection,
            transaction,
            cycleId,
            SyncCycleStatus.Preparing,
            SyncCycleStatus.Staged,
            "StagedAtUtc = SYSUTCDATETIME(), StagedKeySetHash = @StateHash,",
            stagedKeySetHash,
            cancellationToken);

    public Task<bool> MarkCycleValidatedAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        CancellationToken cancellationToken = default)
        => TransitionCycleAsync(
            connection,
            transaction,
            cycleId,
            SyncCycleStatus.Staged,
            SyncCycleStatus.Validated,
            "ValidatedAtUtc = SYSUTCDATETIME(),",
            stateHash: null,
            cancellationToken);

    public Task<bool> MarkTargetCommittingAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        CancellationToken cancellationToken = default)
        => TransitionCycleAsync(
            connection,
            transaction,
            cycleId,
            SyncCycleStatus.Validated,
            SyncCycleStatus.TargetCommitting,
            string.Empty,
            stateHash: null,
            cancellationToken);

    public async Task<bool> MarkTargetCommittedAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            MarkTargetCommittedSql,
            new { CycleId = cycleId },
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        return affected == 1;
    }

    public Task<bool> MarkCheckpointPublishedAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        CancellationToken cancellationToken = default)
        => TransitionCycleAsync(
            connection,
            transaction,
            cycleId,
            SyncCycleStatus.TargetCommitted,
            SyncCycleStatus.CheckpointPublished,
            "CheckpointPublishedAtUtc = SYSUTCDATETIME(),",
            stateHash: null,
            cancellationToken);

    public Task<bool> MarkCycleCompleteAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        CancellationToken cancellationToken = default)
        => TransitionCycleAsync(
            connection,
            transaction,
            cycleId,
            SyncCycleStatus.CheckpointPublished,
            SyncCycleStatus.Complete,
            "CompletedAtUtc = SYSUTCDATETIME(),",
            stateHash: null,
            cancellationToken);

    public async Task<CsdtTargetCycleCommitMarker?> ReadCycleMarkerAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            ReadCycleMarkerSql,
            new { CycleId = cycleId },
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        var cycle = await grid.ReadSingleOrDefaultAsync<CycleMarkerRow>();
        if (cycle is null)
        {
            return null;
        }

        var domains = (await grid.ReadAsync<CycleMarkerDomainRow>())
            .Select(row => new CsdtAtomicDomainCommitResult(
                row.DomainName,
                row.SourceRowCount,
                row.InsertCount,
                row.UpdateCount,
                Math.Max(
                    0,
                    row.SourceRowCount -
                    row.InsertCount -
                    row.UpdateCount -
                    row.DeleteCount),
                new ControlPlaneFingerprint(row.SourceKeySetHash),
                new ControlPlaneFingerprint(row.ResultHash)))
            .ToArray();
        return new CsdtTargetCycleCommitMarker(
            cycle.CycleId,
            cycle.SourceProfile,
            cycle.TargetProfile,
            cycle.StreamCode,
            cycle.MaCSDT,
            cycle.StartSourceVersion,
            cycle.EndSourceVersion,
            CsdtControlPlaneSqlTokens.ParseCycleStatus(cycle.CycleStatus),
            cycle.EnabledDomainCount,
            new ControlPlaneFingerprint(cycle.MappingFingerprint),
            new ControlPlaneFingerprint(cycle.RouteFingerprint),
            cycle.StagedKeySetHash is null
                ? null
                : new ControlPlaneFingerprint(cycle.StagedKeySetHash),
            domains,
            new ControlPlaneFingerprint(cycle.SourceSchemaFingerprint),
            new ControlPlaneFingerprint(cycle.TargetSchemaFingerprint));
    }

    public async Task<bool> MarkCycleFailedOrConflictAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        SyncCycleStatus terminalStatus,
        string errorCode,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        if (terminalStatus is not (SyncCycleStatus.Failed or SyncCycleStatus.Conflict))
        {
            throw new ArgumentOutOfRangeException(nameof(terminalStatus));
        }

        CsdtControlPlaneSqlTokens.RequireErrorCode(errorCode);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.QLHV_CsdtRealtimeCycle
            SET CycleStatus = @CycleStatus,
                ErrorCode = @ErrorCode,
                CompletedAtUtc = SYSUTCDATETIME()
            WHERE CycleId = @CycleId
              AND CycleStatus NOT IN ('COMPLETE', 'FAILED', 'CONFLICT');
            """,
            new
            {
                CycleId = cycleId,
                CycleStatus = CsdtControlPlaneSqlTokens.CycleStatus(terminalStatus),
                ErrorCode = errorCode,
            },
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        return affected == 1;
    }

    public async Task UpsertCycleDomainResultAsync(
        DbConnection connection,
        DbTransaction transaction,
        SyncCycleDomainResult result,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(result);
        RequireDomain(result.DomainName);
        ValidateCounts(result);
        if (result.ErrorCode is not null)
        {
            CsdtControlPlaneSqlTokens.RequireErrorCode(result.ErrorCode);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            UpsertCycleDomainSql,
            new
            {
                result.CycleId,
                result.DomainName,
                DomainStatus = CsdtControlPlaneSqlTokens.DomainStatus(result.Status),
                result.SourceRowCount,
                result.InsertCount,
                result.UpdateCount,
                result.DeleteCount,
                result.PreservedExcludedCount,
                result.ConflictCount,
                SourceKeySetHash = result.SourceKeySetHash.ToArray(),
                ResultHash = result.ResultHash?.ToArray(),
                result.ErrorCode,
                result.StartedAtUtc,
                result.CompletedAtUtc,
            },
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    public async Task UpsertCoverageMarkerAsync(
        DbConnection connection,
        DbTransaction transaction,
        CoverageMarkerRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(request);
        CsdtControlPlaneCatalog.ValidateRoute(request.Route);
        ValidateSourceVersion(request.BaselineSourceVersion);
        if (request.MembershipCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (request.IsComplete != request.CompletedCycleId.HasValue)
        {
            throw new ArgumentException(
                "Complete coverage requires a completed cycle and incomplete coverage must not claim one.",
                nameof(request));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            UpsertCoverageSql,
            new
            {
                request.Route.TargetProfile,
                request.Route.SourceProfile,
                request.Route.StreamCode,
                MaCSDT = request.Route.MaCsdt,
                request.Route.TableName,
                request.BaselineSourceVersion,
                MappingFingerprint = request.MappingFingerprint.ToArray(),
                RouteFingerprint = request.RouteFingerprint.ToArray(),
                SourceKeySetHash = request.SourceKeySetHash.ToArray(),
                request.MembershipCount,
                request.IsComplete,
                request.CompletedCycleId,
                SourceSchemaFingerprint = request.SourceSchemaFingerprint.ToArray(),
                TargetSchemaFingerprint = request.TargetSchemaFingerprint.ToArray(),
            },
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    public async Task<StreamCoverageState?> ReadCoverageStatusAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipRoute route,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        CsdtControlPlaneCatalog.ValidateRoute(route);
        var row = await connection.QuerySingleOrDefaultAsync<CoverageRow>(new CommandDefinition(
            ReadCoverageSql,
            new
            {
                route.TargetProfile,
                route.SourceProfile,
                route.StreamCode,
                MaCSDT = route.MaCsdt,
                route.TableName,
            },
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        return row is null
            ? null
            : new StreamCoverageState(
                route,
                row.BaselineSourceVersion,
                new ControlPlaneFingerprint(row.MappingFingerprint),
                new ControlPlaneFingerprint(row.RouteFingerprint),
                new ControlPlaneFingerprint(row.SourceKeySetHash),
                row.MembershipCount,
                row.IsComplete,
                row.CompletedCycleId,
                row.CompletedAtUtc,
                new ControlPlaneFingerprint(row.SourceSchemaFingerprint),
                new ControlPlaneFingerprint(row.TargetSchemaFingerprint));
    }

    public async Task<SourceMembershipRecord?> ReadActiveMembershipAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipRoute route,
        CanonicalBusinessKey canonicalBusinessKey,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        CsdtControlPlaneCatalog.ValidateRoute(route);
        ArgumentNullException.ThrowIfNull(canonicalBusinessKey);
        var row = await connection.QuerySingleOrDefaultAsync<MembershipRow>(new CommandDefinition(
            ReadActiveMembershipSql,
            new
            {
                route.TargetProfile,
                route.SourceProfile,
                route.StreamCode,
                MaCSDT = route.MaCsdt,
                route.TableName,
                KeySchemaVersion = checked((short)canonicalBusinessKey.SchemaVersion),
                CanonicalBusinessKey = canonicalBusinessKey.ToArray(),
            },
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        return row is null ? null : row.ToRecord();
    }

    public async Task<SourceMembershipRecord?> ReadMembershipAsync(
        DbConnection connection,
        DbTransaction transaction,
        MembershipRoute route,
        CanonicalBusinessKey canonicalBusinessKey,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        CsdtControlPlaneCatalog.ValidateRoute(route);
        ArgumentNullException.ThrowIfNull(canonicalBusinessKey);
        var row = await connection.QuerySingleOrDefaultAsync<MembershipRow>(
            new CommandDefinition(
                ReadMembershipSql,
                new
                {
                    route.TargetProfile,
                    route.SourceProfile,
                    route.StreamCode,
                    MaCSDT = route.MaCsdt,
                    route.TableName,
                    KeySchemaVersion = checked((short)canonicalBusinessKey.SchemaVersion),
                    CanonicalBusinessKey = canonicalBusinessKey.ToArray(),
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        return row is null ? null : row.ToRecord();
    }

    public async Task<OwnershipReservation?> ReadOwnershipReservationAsync(
        DbConnection connection,
        DbTransaction transaction,
        string targetProfile,
        TypedTargetKeyClaim typedTargetKey,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(typedTargetKey);
        typedTargetKey.ValidateForTargetProfile(targetProfile);
        RequireDomain(typedTargetKey.TableName);
        var row = await connection.QuerySingleOrDefaultAsync<OwnershipRow>(new CommandDefinition(
            ReadOwnershipSql,
            new
            {
                TargetProfile = targetProfile,
                typedTargetKey.TableName,
                typedTargetKey.DmDonViGtvtMaDv,
                typedTargetKey.GiaoVienMaGv,
                typedTargetKey.KhoaHocMaKh,
                typedTargetKey.KhoaHocGiaoVienMaLichLv,
                typedTargetKey.BaoCaoIMaBci,
                typedTargetKey.NguoiLxMaDk,
                typedTargetKey.NguoiLxHoSoMaDk,
                typedTargetKey.GiayToMaGt,
                typedTargetKey.GiayToMaDk,
            },
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        return row is null
            ? null
            : new OwnershipReservation(
                row.MembershipId,
                row.TargetProfile,
                row.SourceProfile,
                row.StreamCode,
                row.MaCSDT,
                row.TableName,
                row.OwnershipReserved);
    }

    private static async Task<bool> ExecuteMembershipTransitionAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        MembershipTransitionRequest request,
        CancellationToken cancellationToken)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(request);
        ValidateSourceVersion(request.SourceVersion);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                request.MembershipId,
                request.CycleId,
                request.SourceVersion,
                ReasonCode = CsdtControlPlaneSqlTokens.Reason(request.ReasonCode),
                TargetAction = CsdtControlPlaneSqlTokens.TargetAction(request.TargetAction),
                MappingFingerprint = request.MappingFingerprint.ToArray(),
                RouteFingerprint = request.RouteFingerprint.ToArray(),
            },
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        return affected == 1;
    }

    private static async Task<bool> TransitionCycleAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid cycleId,
        SyncCycleStatus before,
        SyncCycleStatus after,
        string fixedAssignments,
        ControlPlaneFingerprint? stateHash,
        CancellationToken cancellationToken)
    {
        ValidateTransaction(connection, transaction);
        _ = SyncCycleStateMachine.Transition(before, after);
        var sql = $"""
            UPDATE dbo.QLHV_CsdtRealtimeCycle
            SET {fixedAssignments}
                CycleStatus = @AfterStatus
            WHERE CycleId = @CycleId
              AND CycleStatus = @BeforeStatus;
            """;
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                CycleId = cycleId,
                BeforeStatus = CsdtControlPlaneSqlTokens.CycleStatus(before),
                AfterStatus = CsdtControlPlaneSqlTokens.CycleStatus(after),
                StateHash = stateHash?.ToArray(),
            },
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
        return affected == 1;
    }

    private static void ValidateTransaction(
        DbConnection connection,
        DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (connection.State != ConnectionState.Open ||
            transaction.Connection is null ||
            !ReferenceEquals(transaction.Connection, connection))
        {
            throw new ArgumentException(
                "An open connection and its caller-owned transaction are required.");
        }
    }

    private static void ValidateSourceVersion(long sourceVersion)
    {
        if (sourceVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceVersion));
        }
    }

    private static void ValidateKeyLengths(
        CanonicalBusinessKey canonicalBusinessKey,
        TargetEqualityKey targetEqualityKey)
    {
        ArgumentNullException.ThrowIfNull(canonicalBusinessKey);
        ArgumentNullException.ThrowIfNull(targetEqualityKey);
        if (canonicalBusinessKey.Length > 512 || targetEqualityKey.Length > 512)
        {
            throw new ArgumentException("Encoded control-plane keys cannot exceed 512 bytes.");
        }
    }

    private static void RequireDomain(string domain)
    {
        if (!CsdtControlPlaneCatalog.TableNames.Contains(domain))
        {
            throw new ArgumentException(
                "Control-plane domain is outside the fixed allowlist.",
                nameof(domain));
        }
    }

    private static void ValidateCounts(SyncCycleDomainResult result)
    {
        if (result.SourceRowCount < 0 ||
            result.InsertCount < 0 ||
            result.UpdateCount < 0 ||
            result.DeleteCount < 0 ||
            result.PreservedExcludedCount < 0 ||
            result.ConflictCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(result));
        }

        if (result.CompletedAtUtc.HasValue &&
            result.CompletedAtUtc.Value < result.StartedAtUtc)
        {
            throw new ArgumentException(
                "A domain completion time cannot precede its start time.",
                nameof(result));
        }

        var validShape = result.Status switch
        {
            SyncCycleDomainStatus.Pending or
            SyncCycleDomainStatus.Staged or
            SyncCycleDomainStatus.Validated
                => result.ResultHash is null &&
                   result.ErrorCode is null &&
                   !result.CompletedAtUtc.HasValue,
            SyncCycleDomainStatus.Committed
                => result.ResultHash is not null &&
                   result.ErrorCode is null &&
                   result.CompletedAtUtc.HasValue,
            SyncCycleDomainStatus.Failed or
            SyncCycleDomainStatus.Conflict
                => result.ResultHash is null &&
                   result.ErrorCode is not null &&
                   result.CompletedAtUtc.HasValue,
            SyncCycleDomainStatus.Skipped
                => result.ResultHash is null &&
                   result.ErrorCode is null &&
                   result.CompletedAtUtc.HasValue,
            _ => false,
        };
        if (!validShape)
        {
            throw new ArgumentException(
                "Cycle-domain result hash, error, and timestamp shape is invalid for its status.",
                nameof(result));
        }
    }

    private sealed class CoverageRow
    {
        public long BaselineSourceVersion { get; init; }
        public byte[] MappingFingerprint { get; init; } = [];
        public byte[] RouteFingerprint { get; init; } = [];
        public byte[] SourceKeySetHash { get; init; } = [];
        public byte[] SourceSchemaFingerprint { get; init; } = [];
        public byte[] TargetSchemaFingerprint { get; init; } = [];
        public long MembershipCount { get; init; }
        public bool IsComplete { get; init; }
        public Guid? CompletedCycleId { get; init; }
        public DateTimeOffset? CompletedAtUtc { get; init; }
    }

    private sealed class MembershipRow
    {
        public long MembershipId { get; init; }
        public string MembershipStatus { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public bool ClaimsTargetKey { get; init; }
        public bool OwnershipReserved { get; init; }
        public long LastObservedSourceVersion { get; init; }
        public long? AppliedSourceVersion { get; init; }
        public long? DeletedAtSourceVersion { get; init; }
        public long? ReactivatedAtSourceVersion { get; init; }
        public int OwnershipEpoch { get; init; }
        public short KeySchemaVersion { get; init; }
        public byte[] MappingFingerprint { get; init; } = [];
        public byte[] RouteFingerprint { get; init; } = [];

        public SourceMembershipRecord ToRecord()
            => new(
                MembershipId,
                CsdtControlPlaneSqlTokens.ParseMembershipStatus(MembershipStatus),
                IsActive,
                ClaimsTargetKey,
                OwnershipReserved,
                LastObservedSourceVersion,
                AppliedSourceVersion,
                DeletedAtSourceVersion,
                ReactivatedAtSourceVersion,
                OwnershipEpoch,
                checked((ushort)KeySchemaVersion),
                new ControlPlaneFingerprint(MappingFingerprint),
                new ControlPlaneFingerprint(RouteFingerprint));
    }

    private sealed class OwnershipRow
    {
        public long MembershipId { get; init; }
        public string TargetProfile { get; init; } = string.Empty;
        public string SourceProfile { get; init; } = string.Empty;
        public string StreamCode { get; init; } = string.Empty;
        public string MaCSDT { get; init; } = string.Empty;
        public string TableName { get; init; } = string.Empty;
        public bool OwnershipReserved { get; init; }
    }

    private sealed class CycleMarkerRow
    {
        public Guid CycleId { get; init; }
        public string SourceProfile { get; init; } = string.Empty;
        public string TargetProfile { get; init; } = string.Empty;
        public string StreamCode { get; init; } = string.Empty;
        public string MaCSDT { get; init; } = string.Empty;
        public long StartSourceVersion { get; init; }
        public long EndSourceVersion { get; init; }
        public string CycleStatus { get; init; } = string.Empty;
        public int EnabledDomainCount { get; init; }
        public byte[] MappingFingerprint { get; init; } = [];
        public byte[] RouteFingerprint { get; init; } = [];
        public byte[]? StagedKeySetHash { get; init; }
        public byte[] SourceSchemaFingerprint { get; init; } = [];
        public byte[] TargetSchemaFingerprint { get; init; } = [];
    }

    private sealed class CycleMarkerDomainRow
    {
        public string DomainName { get; init; } = string.Empty;
        public long SourceRowCount { get; init; }
        public long InsertCount { get; init; }
        public long UpdateCount { get; init; }
        public long DeleteCount { get; init; }
        public byte[] SourceKeySetHash { get; init; } = [];
        public byte[] ResultHash { get; init; } = [];
    }

    private const string CreateInsertPendingSql = """
        IF
        (
            @DmDonViGtvtMaDv IS NOT NULL
            AND CONVERT(varbinary(max), @DmDonViGtvtMaDv) <>
                CONVERT
                (
                    varbinary(max),
                    CONVERT
                    (
                        nvarchar(max),
                        CONVERT
                        (
                            varchar(6),
                            @DmDonViGtvtMaDv COLLATE SQL_Latin1_General_CP1_CI_AS
                        )
                    )
                )
        )
        OR
        (
            @GiaoVienMaGv IS NOT NULL
            AND CONVERT(varbinary(max), @GiaoVienMaGv) <>
                CONVERT
                (
                    varbinary(max),
                    CONVERT
                    (
                        nvarchar(max),
                        CONVERT
                        (
                            varchar(8),
                            @GiaoVienMaGv COLLATE SQL_Latin1_General_CP1_CI_AS
                        )
                    )
                )
        )
        OR
        (
            @KhoaHocMaKh IS NOT NULL
            AND CONVERT(varbinary(max), @KhoaHocMaKh) <>
                CONVERT
                (
                    varbinary(max),
                    CONVERT
                    (
                        nvarchar(max),
                        CONVERT
                        (
                            varchar(13),
                            @KhoaHocMaKh COLLATE SQL_Latin1_General_CP1_CI_AS
                        )
                    )
                )
        )
        OR
        (
            @BaoCaoIMaBci IS NOT NULL
            AND CONVERT(varbinary(max), @BaoCaoIMaBci) <>
                CONVERT
                (
                    varbinary(max),
                    CONVERT
                    (
                        nvarchar(max),
                        CONVERT
                        (
                            varchar(18),
                            @BaoCaoIMaBci COLLATE SQL_Latin1_General_CP1_CI_AS
                        )
                    )
                )
        )
        OR
        (
            @NguoiLxMaDk IS NOT NULL
            AND CONVERT(varbinary(max), @NguoiLxMaDk) <>
                CONVERT
                (
                    varbinary(max),
                    CONVERT
                    (
                        nvarchar(max),
                        CONVERT
                        (
                            varchar(25),
                            @NguoiLxMaDk COLLATE SQL_Latin1_General_CP1_CI_AS
                        )
                    )
                )
        )
        OR
        (
            @NguoiLxHoSoMaDk IS NOT NULL
            AND CONVERT(varbinary(max), @NguoiLxHoSoMaDk) <>
                CONVERT
                (
                    varbinary(max),
                    CONVERT
                    (
                        nvarchar(max),
                        CONVERT
                        (
                            varchar(25),
                            @NguoiLxHoSoMaDk COLLATE SQL_Latin1_General_CP1_CI_AS
                        )
                    )
                )
        )
        OR
        (
            @GiayToMaDk IS NOT NULL
            AND CONVERT(varbinary(max), @GiayToMaDk) <>
                CONVERT
                (
                    varbinary(max),
                    CONVERT
                    (
                        nvarchar(max),
                        CONVERT
                        (
                            varchar(25),
                            @GiayToMaDk COLLATE SQL_Latin1_General_CP1_CI_AS
                        )
                    )
                )
        )
            THROW 527618, 'Typed target key is not lossless in the fixed target varchar type.', 1;

        DECLARE @ExistingMembershipId bigint;
        SELECT @ExistingMembershipId = MembershipId
        FROM dbo.QLHV_CsdtRealtimeSourceMembership WITH (UPDLOCK, HOLDLOCK)
        WHERE TargetProfile = @TargetProfile
          AND SourceProfile = @SourceProfile
          AND StreamCode = @StreamCode
          AND MaCSDT = @MaCSDT
          AND TableName = @TableName
          AND KeySchemaVersion = @KeySchemaVersion
          AND CanonicalBusinessKey = @CanonicalBusinessKey;

        IF @ExistingMembershipId IS NOT NULL
        BEGIN
            IF EXISTS
            (
                SELECT 1
                FROM dbo.QLHV_CsdtRealtimeSourceMembership
                WHERE MembershipId = @ExistingMembershipId
                  AND
                  (
                      MappingFingerprint <> @MappingFingerprint
                      OR RouteFingerprint <> @RouteFingerprint
                      OR TargetEqualityKey <> @TargetEqualityKey
                      OR MembershipStatus <> 'INSERT_PENDING'
                      OR LastObservedSourceVersion <> @SourceVersion
                  )
            )
                THROW 527611, 'Existing membership is incompatible with the requested control-plane claim.', 1;

            IF NOT EXISTS
            (
                SELECT 1
                FROM dbo.QLHV_CsdtRealtimeOwnershipClaim AS claim
                    WITH (UPDLOCK, HOLDLOCK)
                WHERE claim.MembershipId = @ExistingMembershipId
                  AND claim.TargetProfile = @TargetProfile
                  AND claim.TableName = @TableName
                  AND claim.ProofVersion = @TargetEqualityProofVersion
                  AND claim.ProofId = @TargetEqualityProofId
                  AND
                  (
                      (
                          @TableName = 'DM_DonViGTVT'
                          AND claim.DmDonViGtvtMaDV =
                              CONVERT(varchar(6), @DmDonViGtvtMaDv COLLATE SQL_Latin1_General_CP1_CI_AS)
                      )
                      OR
                      (
                          @TableName = 'GiaoVien'
                          AND claim.GiaoVienMaGV =
                              CONVERT(varchar(8), @GiaoVienMaGv COLLATE SQL_Latin1_General_CP1_CI_AS)
                      )
                      OR
                      (
                          @TableName = 'KhoaHoc'
                          AND claim.KhoaHocMaKH =
                              CONVERT(varchar(13), @KhoaHocMaKh COLLATE SQL_Latin1_General_CP1_CI_AS)
                      )
                      OR
                      (
                          @TableName = 'KhoaHoc_GiaoVien'
                          AND claim.KhoaHocGiaoVienMaLichLV = @KhoaHocGiaoVienMaLichLv
                      )
                      OR
                      (
                          @TableName = 'BaoCaoI'
                          AND claim.BaoCaoIMaBCI =
                              CONVERT(varchar(18), @BaoCaoIMaBci COLLATE SQL_Latin1_General_CP1_CI_AS)
                      )
                      OR
                      (
                          @TableName = 'NguoiLX'
                          AND claim.NguoiLXMaDK =
                              CONVERT(varchar(25), @NguoiLxMaDk COLLATE SQL_Latin1_General_CP1_CI_AS)
                      )
                      OR
                      (
                          @TableName = 'NguoiLX_HoSo'
                          AND claim.NguoiLXHoSoMaDK =
                              CONVERT(varchar(25), @NguoiLxHoSoMaDk COLLATE SQL_Latin1_General_CP1_CI_AS)
                      )
                      OR
                      (
                          @TableName = 'NguoiLXHS_GiayTo'
                          AND claim.GiayToMaGT = @GiayToMaGt
                          AND claim.GiayToMaDK =
                              CONVERT(varchar(25), @GiayToMaDk COLLATE SQL_Latin1_General_CP1_CI_AS)
                      )
                  )
            )
                THROW 527619, 'Existing membership has no matching authoritative typed ownership claim.', 1;

            SELECT @ExistingMembershipId;
            RETURN;
        END;

        INSERT INTO dbo.QLHV_CsdtRealtimeSourceMembership
        (
            TargetProfile, SourceProfile, StreamCode, MaCSDT, TableName,
            KeySchemaVersion, CanonicalBusinessKey, TargetEqualityKey,
            TargetEqualityProofStatus, TargetEqualityProofId,
            CanonicalBusinessKeyHash, HashKeyVersion,
            IsActive, ClaimsTargetKey, OwnershipReserved,
            MembershipStatus, TargetAction,
            LastObservedSourceVersion, AppliedSourceVersion,
            DeletedAtSourceVersion, ReactivatedAtSourceVersion,
            FirstSeenCycleId, LastSeenCycleId, LastAppliedCycleId,
            ReasonCode, MappingFingerprint, RouteFingerprint, OwnershipEpoch
        )
        VALUES
        (
            @TargetProfile, @SourceProfile, @StreamCode, @MaCSDT, @TableName,
            @KeySchemaVersion, @CanonicalBusinessKey, @TargetEqualityKey,
            @TargetEqualityProofStatus, @TargetEqualityProofId,
            @CanonicalBusinessKeyHash, @HashKeyVersion,
            0, 1, 1,
            'INSERT_PENDING', 'NONE',
            @SourceVersion, NULL,
            NULL, NULL,
            @CycleId, @CycleId, NULL,
            'SOURCE_PRESENT', @MappingFingerprint, @RouteFingerprint, 1
        );

        DECLARE @MembershipId bigint = CONVERT(bigint, SCOPE_IDENTITY());
        INSERT INTO dbo.QLHV_CsdtRealtimeOwnershipClaim
        (
            MembershipId, TargetProfile, TableName, ProofVersion, ProofId,
            DmDonViGtvtMaDV, GiaoVienMaGV, KhoaHocMaKH,
            KhoaHocGiaoVienMaLichLV, BaoCaoIMaBCI,
            NguoiLXMaDK, NguoiLXHoSoMaDK, GiayToMaGT, GiayToMaDK
        )
        VALUES
        (
            @MembershipId, @TargetProfile, @TableName,
            @TargetEqualityProofVersion, @TargetEqualityProofId,
            CONVERT(varchar(6), @DmDonViGtvtMaDv COLLATE SQL_Latin1_General_CP1_CI_AS),
            CONVERT(varchar(8), @GiaoVienMaGv COLLATE SQL_Latin1_General_CP1_CI_AS),
            CONVERT(varchar(13), @KhoaHocMaKh COLLATE SQL_Latin1_General_CP1_CI_AS),
            @KhoaHocGiaoVienMaLichLv,
            CONVERT(varchar(18), @BaoCaoIMaBci COLLATE SQL_Latin1_General_CP1_CI_AS),
            CONVERT(varchar(25), @NguoiLxMaDk COLLATE SQL_Latin1_General_CP1_CI_AS),
            CONVERT(varchar(25), @NguoiLxHoSoMaDk COLLATE SQL_Latin1_General_CP1_CI_AS),
            @GiayToMaGt,
            CONVERT(varchar(25), @GiayToMaDk COLLATE SQL_Latin1_General_CP1_CI_AS)
        );

        SELECT @MembershipId;
        """;

    private const string ApplyActiveSql = """
        UPDATE dbo.QLHV_CsdtRealtimeSourceMembership WITH (UPDLOCK, HOLDLOCK)
        SET MembershipStatus = 'ACTIVE',
            IsActive = 1,
            ClaimsTargetKey = 1,
            AppliedSourceVersion = @SourceVersion,
            LastAppliedCycleId = @CycleId,
            TargetAction = 'UPSERTED',
            ReasonCode = 'TARGET_ACTION_APPLIED',
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE MembershipId = @MembershipId
          AND MembershipStatus = 'INSERT_PENDING'
          AND LastObservedSourceVersion = @SourceVersion
          AND MappingFingerprint = @MappingFingerprint
          AND RouteFingerprint = @RouteFingerprint;
        """;

    private const string ObserveActiveSql = """
        UPDATE dbo.QLHV_CsdtRealtimeSourceMembership WITH (UPDLOCK, HOLDLOCK)
        SET LastObservedSourceVersion = @SourceVersion,
            AppliedSourceVersion = @SourceVersion,
            LastSeenCycleId = @CycleId,
            LastAppliedCycleId = @CycleId,
            TargetAction = 'UPSERTED',
            ReasonCode = 'SOURCE_PRESENT',
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE MembershipId = @MembershipId
          AND MembershipStatus = 'ACTIVE'
          AND LastObservedSourceVersion < @SourceVersion
          AND MappingFingerprint = @MappingFingerprint
          AND RouteFingerprint = @RouteFingerprint;
        """;

    private const string CreateDeletePendingSql = """
        UPDATE dbo.QLHV_CsdtRealtimeSourceMembership WITH (UPDLOCK, HOLDLOCK)
        SET MembershipStatus = 'DELETE_PENDING',
            IsActive = 0,
            ClaimsTargetKey = 0,
            LastObservedSourceVersion = @SourceVersion,
            LastSeenCycleId = @CycleId,
            TargetAction = 'NONE',
            ReasonCode = @ReasonCode,
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE MembershipId = @MembershipId
          AND MembershipStatus = 'ACTIVE'
          AND LastObservedSourceVersion < @SourceVersion
          AND ISNULL(ReactivatedAtSourceVersion, -1) < @SourceVersion
          AND MappingFingerprint = @MappingFingerprint
          AND RouteFingerprint = @RouteFingerprint;
        """;

    private const string ApplyInactiveSql = """
        UPDATE dbo.QLHV_CsdtRealtimeSourceMembership WITH (UPDLOCK, HOLDLOCK)
        SET MembershipStatus = 'INACTIVE',
            IsActive = 0,
            ClaimsTargetKey = 0,
            OwnershipReserved = 1,
            AppliedSourceVersion = @SourceVersion,
            DeletedAtSourceVersion = @SourceVersion,
            LastAppliedCycleId = @CycleId,
            TargetAction = @TargetAction,
            ReasonCode = 'TARGET_ACTION_APPLIED',
            DeactivatedAtUtc = SYSUTCDATETIME(),
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE MembershipId = @MembershipId
          AND MembershipStatus = 'DELETE_PENDING'
          AND LastObservedSourceVersion = @SourceVersion
          AND MappingFingerprint = @MappingFingerprint
          AND RouteFingerprint = @RouteFingerprint;
        """;

    private const string CreateReactivatePendingSql = """
        UPDATE dbo.QLHV_CsdtRealtimeSourceMembership WITH (UPDLOCK, HOLDLOCK)
        SET MembershipStatus = 'REACTIVATE_PENDING',
            IsActive = 0,
            ClaimsTargetKey = 1,
            OwnershipReserved = 1,
            LastObservedSourceVersion = @SourceVersion,
            LastSeenCycleId = @CycleId,
            TargetAction = 'NONE',
            ReasonCode = 'REACTIVATED_AT_SOURCE',
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE MembershipId = @MembershipId
          AND MembershipStatus = 'INACTIVE'
          AND LastObservedSourceVersion < @SourceVersion
          AND DeletedAtSourceVersion < @SourceVersion
          AND MappingFingerprint = @MappingFingerprint
          AND RouteFingerprint = @RouteFingerprint;
        """;

    private const string ApplyReactivatedSql = """
        UPDATE dbo.QLHV_CsdtRealtimeSourceMembership WITH (UPDLOCK, HOLDLOCK)
        SET MembershipStatus = 'ACTIVE',
            IsActive = 1,
            ClaimsTargetKey = 1,
            OwnershipReserved = 1,
            AppliedSourceVersion = @SourceVersion,
            DeletedAtSourceVersion = NULL,
            ReactivatedAtSourceVersion = @SourceVersion,
            LastAppliedCycleId = @CycleId,
            TargetAction = 'UPSERTED',
            ReasonCode = 'TARGET_ACTION_APPLIED',
            ReactivatedAtUtc = SYSUTCDATETIME(),
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE MembershipId = @MembershipId
          AND MembershipStatus = 'REACTIVATE_PENDING'
          AND LastObservedSourceVersion = @SourceVersion
          AND MappingFingerprint = @MappingFingerprint
          AND RouteFingerprint = @RouteFingerprint;
        """;

    private const string MarkConflictSql = """
        UPDATE dbo.QLHV_CsdtRealtimeSourceMembership WITH (UPDLOCK, HOLDLOCK)
        SET MembershipStatus = 'CONFLICT',
            IsActive = 0,
            ClaimsTargetKey = 0,
            OwnershipReserved = 1,
            LastSeenCycleId = @CycleId,
            ReasonCode = @ReasonCode,
            TargetAction = 'NONE',
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE MembershipId = @MembershipId;
        """;

    private const string AppendJournalSql = """
        IF EXISTS
        (
            SELECT 1
            FROM dbo.QLHV_CsdtRealtimeMembershipJournal WITH (UPDLOCK, HOLDLOCK)
            WHERE MembershipId = @MembershipId
              AND CycleId = @CycleId
              AND BeforeStatus = @BeforeStatus
              AND AfterStatus = @AfterStatus
              AND SourceVersion = @SourceVersion
              AND ReasonCode = @ReasonCode
              AND TargetAction = @TargetAction
              AND
              (
                  MappingFingerprint <> @MappingFingerprint
                  OR RouteFingerprint <> @RouteFingerprint
                  OR DiagnosticKeyHash <> @DiagnosticKeyHash
                  OR HashKeyVersion <> @HashKeyVersion
              )
        )
            THROW 527614, 'Membership journal replay has incompatible diagnostic metadata.', 1;

        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.QLHV_CsdtRealtimeMembershipJournal WITH (UPDLOCK, HOLDLOCK)
            WHERE MembershipId = @MembershipId
              AND CycleId = @CycleId
              AND BeforeStatus = @BeforeStatus
              AND AfterStatus = @AfterStatus
              AND SourceVersion = @SourceVersion
              AND ReasonCode = @ReasonCode
              AND TargetAction = @TargetAction
        )
        BEGIN
            INSERT INTO dbo.QLHV_CsdtRealtimeMembershipJournal
            (
                MembershipId, CycleId, BeforeStatus, AfterStatus, SourceVersion,
                ReasonCode, TargetAction, MappingFingerprint, RouteFingerprint,
                OccurredAtUtc, DiagnosticKeyHash, HashKeyVersion
            )
            VALUES
            (
                @MembershipId, @CycleId, @BeforeStatus, @AfterStatus, @SourceVersion,
                @ReasonCode, @TargetAction, @MappingFingerprint, @RouteFingerprint,
                SYSUTCDATETIME(), @DiagnosticKeyHash, @HashKeyVersion
            );
        END;
        """;

    private const string CreateCycleSql = """
        IF EXISTS
        (
            SELECT 1
            FROM dbo.QLHV_CsdtRealtimeCycle WITH (UPDLOCK, HOLDLOCK)
            WHERE CycleId = @CycleId
              AND
              (
                  TargetProfile <> @TargetProfile
                  OR SourceProfile <> @SourceProfile
                  OR StreamCode <> @StreamCode
                  OR MaCSDT <> @MaCSDT
                  OR StartSourceVersion <> @StartSourceVersion
                  OR EndSourceVersion <> @EndSourceVersion
                  OR MappingFingerprint <> @MappingFingerprint
                  OR RouteFingerprint <> @RouteFingerprint
                  OR SourceSchemaFingerprint <> @SourceSchemaFingerprint
                  OR TargetSchemaFingerprint <> @TargetSchemaFingerprint
                  OR EnabledDomainCount <> @EnabledDomainCount
              )
        )
            THROW 527612, 'Cycle replay does not match the durable cycle identity.', 1;

        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.QLHV_CsdtRealtimeCycle WITH (UPDLOCK, HOLDLOCK)
            WHERE CycleId = @CycleId
        )
        BEGIN
            INSERT INTO dbo.QLHV_CsdtRealtimeCycle
            (
                CycleId, TargetProfile, SourceProfile, StreamCode, MaCSDT,
                StartSourceVersion, EndSourceVersion, EnabledDomainCount,
                MappingFingerprint, RouteFingerprint,
                SourceSchemaFingerprint, TargetSchemaFingerprint, CycleStatus
            )
            VALUES
            (
                @CycleId, @TargetProfile, @SourceProfile, @StreamCode, @MaCSDT,
                @StartSourceVersion, @EndSourceVersion, @EnabledDomainCount,
                @MappingFingerprint, @RouteFingerprint,
                @SourceSchemaFingerprint, @TargetSchemaFingerprint, 'PREPARING'
            );
        END;
        """;

    private const string MarkTargetCommittedSql = """
        UPDATE cycle WITH (UPDLOCK, HOLDLOCK)
        SET CycleStatus = 'TARGET_COMMITTED',
            TargetCommittedAtUtc = SYSUTCDATETIME()
        FROM dbo.QLHV_CsdtRealtimeCycle AS cycle
        WHERE cycle.CycleId = @CycleId
          AND cycle.CycleStatus = 'TARGET_COMMITTING'
          AND cycle.EnabledDomainCount =
          (
              SELECT COUNT(*)
              FROM dbo.QLHV_CsdtRealtimeCycleDomain AS domainResult
              WHERE domainResult.CycleId = cycle.CycleId
                AND domainResult.DomainStatus = 'COMMITTED'
                AND domainResult.ResultHash IS NOT NULL
                AND domainResult.CompletedAtUtc IS NOT NULL
          )
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.QLHV_CsdtRealtimeCycleDomain AS domainResult
              WHERE domainResult.CycleId = cycle.CycleId
                AND domainResult.DomainStatus <> 'COMMITTED'
          );
        """;

    private const string UpsertCycleDomainSql = """
        IF EXISTS
        (
            SELECT 1
            FROM dbo.QLHV_CsdtRealtimeCycleDomain WITH (UPDLOCK, HOLDLOCK)
            WHERE CycleId = @CycleId
              AND DomainName = @DomainName
              AND DomainStatus = 'COMMITTED'
              AND
              (
                  @DomainStatus <> 'COMMITTED'
                  OR SourceRowCount <> @SourceRowCount
                  OR InsertCount <> @InsertCount
                  OR UpdateCount <> @UpdateCount
                  OR DeleteCount <> @DeleteCount
                  OR PreservedExcludedCount <> @PreservedExcludedCount
                  OR ConflictCount <> @ConflictCount
                  OR SourceKeySetHash <> @SourceKeySetHash
                  OR ResultHash <> @ResultHash
              )
        )
            THROW 527615, 'Committed cycle-domain result cannot be changed by replay.', 1;

        UPDATE dbo.QLHV_CsdtRealtimeCycleDomain WITH (UPDLOCK, HOLDLOCK)
        SET DomainStatus = @DomainStatus,
            SourceRowCount = @SourceRowCount,
            InsertCount = @InsertCount,
            UpdateCount = @UpdateCount,
            DeleteCount = @DeleteCount,
            PreservedExcludedCount = @PreservedExcludedCount,
            ConflictCount = @ConflictCount,
            SourceKeySetHash = @SourceKeySetHash,
            ResultHash = @ResultHash,
            ErrorCode = @ErrorCode,
            StartedAtUtc = @StartedAtUtc,
            CompletedAtUtc = @CompletedAtUtc
        WHERE CycleId = @CycleId AND DomainName = @DomainName;

        IF @@ROWCOUNT = 0
        BEGIN
            INSERT INTO dbo.QLHV_CsdtRealtimeCycleDomain
            (
                CycleId, DomainName, DomainStatus, SourceRowCount,
                InsertCount, UpdateCount, DeleteCount, PreservedExcludedCount,
                ConflictCount, SourceKeySetHash, ResultHash, ErrorCode,
                StartedAtUtc, CompletedAtUtc
            )
            VALUES
            (
                @CycleId, @DomainName, @DomainStatus, @SourceRowCount,
                @InsertCount, @UpdateCount, @DeleteCount, @PreservedExcludedCount,
                @ConflictCount, @SourceKeySetHash, @ResultHash, @ErrorCode,
                @StartedAtUtc, @CompletedAtUtc
            );
        END;
        """;

    private const string UpsertCoverageSql = """
        IF EXISTS
        (
            SELECT 1
            FROM dbo.QLHV_CsdtRealtimeStreamCoverage WITH (UPDLOCK, HOLDLOCK)
            WHERE TargetProfile = @TargetProfile
              AND SourceProfile = @SourceProfile
              AND StreamCode = @StreamCode
              AND MaCSDT = @MaCSDT
              AND TableName = @TableName
              AND
              (
                  MappingFingerprint <> @MappingFingerprint
                  OR RouteFingerprint <> @RouteFingerprint
                  OR SourceSchemaFingerprint <> @SourceSchemaFingerprint
                  OR TargetSchemaFingerprint <> @TargetSchemaFingerprint
                  OR BaselineSourceVersion > @BaselineSourceVersion
                  OR (IsComplete = 1 AND @IsComplete = 0)
              )
        )
            THROW 527613, 'Coverage marker replay is stale or has incompatible fingerprints.', 1;

        UPDATE dbo.QLHV_CsdtRealtimeStreamCoverage WITH (UPDLOCK, HOLDLOCK)
        SET BaselineSourceVersion = @BaselineSourceVersion,
            MappingFingerprint = @MappingFingerprint,
            RouteFingerprint = @RouteFingerprint,
            SourceSchemaFingerprint = @SourceSchemaFingerprint,
            TargetSchemaFingerprint = @TargetSchemaFingerprint,
            SourceKeySetHash = @SourceKeySetHash,
            MembershipCount = @MembershipCount,
            IsComplete = @IsComplete,
            CompletedCycleId = @CompletedCycleId,
            CompletedAtUtc = CASE WHEN @IsComplete = 1 THEN SYSUTCDATETIME() ELSE NULL END
        WHERE TargetProfile = @TargetProfile
          AND SourceProfile = @SourceProfile
          AND StreamCode = @StreamCode
          AND MaCSDT = @MaCSDT
          AND TableName = @TableName;

        IF @@ROWCOUNT = 0
        BEGIN
            INSERT INTO dbo.QLHV_CsdtRealtimeStreamCoverage
            (
                TargetProfile, SourceProfile, StreamCode, MaCSDT, TableName,
                BaselineSourceVersion, MappingFingerprint, RouteFingerprint,
                SourceSchemaFingerprint, TargetSchemaFingerprint,
                SourceKeySetHash, MembershipCount, IsComplete,
                CompletedCycleId, CompletedAtUtc
            )
            VALUES
            (
                @TargetProfile, @SourceProfile, @StreamCode, @MaCSDT, @TableName,
                @BaselineSourceVersion, @MappingFingerprint, @RouteFingerprint,
                @SourceSchemaFingerprint, @TargetSchemaFingerprint,
                @SourceKeySetHash, @MembershipCount, @IsComplete,
                @CompletedCycleId,
                CASE WHEN @IsComplete = 1 THEN SYSUTCDATETIME() ELSE NULL END
            );
        END;
        """;

    private const string ReadCoverageSql = """
        SELECT
            BaselineSourceVersion, MappingFingerprint, RouteFingerprint,
            SourceSchemaFingerprint, TargetSchemaFingerprint,
            SourceKeySetHash, MembershipCount, IsComplete,
            CompletedCycleId, CompletedAtUtc
        FROM dbo.QLHV_CsdtRealtimeStreamCoverage WITH (UPDLOCK, HOLDLOCK)
        WHERE TargetProfile = @TargetProfile
          AND SourceProfile = @SourceProfile
          AND StreamCode = @StreamCode
          AND MaCSDT = @MaCSDT
          AND TableName = @TableName;
        """;

    private const string ReadActiveMembershipSql = """
        SELECT
            MembershipId, MembershipStatus, IsActive, ClaimsTargetKey,
            OwnershipReserved, LastObservedSourceVersion, AppliedSourceVersion,
            DeletedAtSourceVersion, ReactivatedAtSourceVersion, OwnershipEpoch,
            KeySchemaVersion, MappingFingerprint, RouteFingerprint
        FROM dbo.QLHV_CsdtRealtimeSourceMembership WITH (UPDLOCK, HOLDLOCK)
        WHERE TargetProfile = @TargetProfile
          AND SourceProfile = @SourceProfile
          AND StreamCode = @StreamCode
          AND MaCSDT = @MaCSDT
          AND TableName = @TableName
          AND KeySchemaVersion = @KeySchemaVersion
          AND CanonicalBusinessKey = @CanonicalBusinessKey
          AND IsActive = 1
          AND MembershipStatus = 'ACTIVE';
        """;

    private const string ReadMembershipSql = """
        SELECT
            MembershipId, MembershipStatus, IsActive, ClaimsTargetKey,
            OwnershipReserved, LastObservedSourceVersion, AppliedSourceVersion,
            DeletedAtSourceVersion, ReactivatedAtSourceVersion, OwnershipEpoch,
            KeySchemaVersion, MappingFingerprint, RouteFingerprint
        FROM dbo.QLHV_CsdtRealtimeSourceMembership WITH (UPDLOCK, HOLDLOCK)
        WHERE TargetProfile = @TargetProfile
          AND SourceProfile = @SourceProfile
          AND StreamCode = @StreamCode
          AND MaCSDT = @MaCSDT
          AND TableName = @TableName
          AND KeySchemaVersion = @KeySchemaVersion
          AND CanonicalBusinessKey = @CanonicalBusinessKey;
        """;

    private const string ReadOwnershipSql = """
        IF EXISTS
        (
            SELECT 1
            FROM
            (
                VALUES
                    (@DmDonViGtvtMaDv),
                    (@GiaoVienMaGv),
                    (@KhoaHocMaKh),
                    (@BaoCaoIMaBci),
                    (@NguoiLxMaDk),
                    (@NguoiLxHoSoMaDk),
                    (@GiayToMaDk)
            ) AS typedText([Value])
            WHERE typedText.[Value] IS NOT NULL
              AND CONVERT(varbinary(max), typedText.[Value]) <>
                  CONVERT
                  (
                      varbinary(max),
                      CONVERT
                      (
                          nvarchar(max),
                          CONVERT
                          (
                              varchar(25),
                              typedText.[Value]
                                  COLLATE SQL_Latin1_General_CP1_CI_AS
                          )
                      )
                  )
        )
            THROW 527618, 'Typed target key is not lossless in the fixed target varchar type.', 1;

        SELECT
            membership.MembershipId,
            membership.TargetProfile,
            membership.SourceProfile,
            membership.StreamCode,
            membership.MaCSDT,
            membership.TableName,
            membership.OwnershipReserved
        FROM dbo.QLHV_CsdtRealtimeOwnershipClaim AS claim
            WITH (UPDLOCK, HOLDLOCK)
        INNER JOIN dbo.QLHV_CsdtRealtimeSourceMembership AS membership
            WITH (UPDLOCK, HOLDLOCK)
          ON membership.MembershipId = claim.MembershipId
        WHERE claim.TargetProfile = @TargetProfile
          AND claim.TableName = @TableName
          AND claim.ProofVersion = 1
          AND claim.ProofId =
              'TYPED_OWNER_SQLSERVER_SQL_LATIN1_GENERAL_CP1_CI_AS_V1'
          AND membership.OwnershipReserved = 1
          AND
          (
              (
                  @TableName = 'DM_DonViGTVT'
                  AND claim.DmDonViGtvtMaDV =
                      CONVERT(varchar(6), @DmDonViGtvtMaDv COLLATE SQL_Latin1_General_CP1_CI_AS)
              )
              OR
              (
                  @TableName = 'GiaoVien'
                  AND claim.GiaoVienMaGV =
                      CONVERT(varchar(8), @GiaoVienMaGv COLLATE SQL_Latin1_General_CP1_CI_AS)
              )
              OR
              (
                  @TableName = 'KhoaHoc'
                  AND claim.KhoaHocMaKH =
                      CONVERT(varchar(13), @KhoaHocMaKh COLLATE SQL_Latin1_General_CP1_CI_AS)
              )
              OR
              (
                  @TableName = 'KhoaHoc_GiaoVien'
                  AND claim.KhoaHocGiaoVienMaLichLV = @KhoaHocGiaoVienMaLichLv
              )
              OR
              (
                  @TableName = 'BaoCaoI'
                  AND claim.BaoCaoIMaBCI =
                      CONVERT(varchar(18), @BaoCaoIMaBci COLLATE SQL_Latin1_General_CP1_CI_AS)
              )
              OR
              (
                  @TableName = 'NguoiLX'
                  AND claim.NguoiLXMaDK =
                      CONVERT(varchar(25), @NguoiLxMaDk COLLATE SQL_Latin1_General_CP1_CI_AS)
              )
              OR
              (
                  @TableName = 'NguoiLX_HoSo'
                  AND claim.NguoiLXHoSoMaDK =
                      CONVERT(varchar(25), @NguoiLxHoSoMaDk COLLATE SQL_Latin1_General_CP1_CI_AS)
              )
              OR
              (
                  @TableName = 'NguoiLXHS_GiayTo'
                  AND claim.GiayToMaGT = @GiayToMaGt
                  AND claim.GiayToMaDK =
                      CONVERT(varchar(25), @GiayToMaDk COLLATE SQL_Latin1_General_CP1_CI_AS)
              )
          );
        """;

    private const string ReadCycleMarkerSql = """
        SELECT
            CycleId, SourceProfile, TargetProfile, StreamCode, MaCSDT,
            StartSourceVersion, EndSourceVersion, CycleStatus,
            EnabledDomainCount, MappingFingerprint, RouteFingerprint,
            SourceSchemaFingerprint, TargetSchemaFingerprint, StagedKeySetHash
        FROM dbo.QLHV_CsdtRealtimeCycle WITH (UPDLOCK, HOLDLOCK)
        WHERE CycleId = @CycleId;

        SELECT
            DomainName, SourceRowCount, InsertCount, UpdateCount, DeleteCount,
            SourceKeySetHash, ResultHash
        FROM dbo.QLHV_CsdtRealtimeCycleDomain WITH (UPDLOCK, HOLDLOCK)
        WHERE CycleId = @CycleId
          AND DomainStatus = 'COMMITTED'
          AND ResultHash IS NOT NULL
        ORDER BY CASE DomainName
            WHEN 'DM_DonViGTVT' THEN 1
            WHEN 'KhoaHoc' THEN 2
            WHEN 'BaoCaoI' THEN 3
            WHEN 'NguoiLX' THEN 4
            WHEN 'NguoiLX_HoSo' THEN 5
            WHEN 'NguoiLXHS_GiayTo' THEN 6
            ELSE 99
        END;
        """;
}

internal static class CsdtControlPlaneSqlTokens
{
    private static readonly IReadOnlySet<string> ErrorCodes =
        new HashSet<string>(
            [
                "CYCLE_FAILED",
                "CYCLE_CONFLICT",
                "BOOTSTRAP_INCOMPLETE",
                "DOMAIN_INCOMPLETE",
                "MAPPING_FINGERPRINT_MISMATCH",
                "ROUTE_FINGERPRINT_MISMATCH",
                "TARGET_EQUALITY_UNPROVEN",
                "SOURCE_VERSION_REGRESSION",
                "TARGET_COMMIT_NOT_VERIFIED",
                "DELETE_EXECUTION_NOT_ENABLED",
                "TARGET_LOCK_TIMEOUT",
                "COVERAGE_INCOMPLETE",
                "CHECKPOINT_CONFLICT",
                "BOOTSTRAP_PARENT_MISSING",
            ],
            StringComparer.Ordinal);

    internal static string MembershipStatus(SourceMembershipStatus value) => value switch
    {
        SourceMembershipStatus.InsertPending => "INSERT_PENDING",
        SourceMembershipStatus.Active => "ACTIVE",
        SourceMembershipStatus.DeletePending => "DELETE_PENDING",
        SourceMembershipStatus.Inactive => "INACTIVE",
        SourceMembershipStatus.ReactivatePending => "REACTIVATE_PENDING",
        SourceMembershipStatus.Conflict => "CONFLICT",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    internal static SourceMembershipStatus ParseMembershipStatus(string value) => value switch
    {
        "INSERT_PENDING" => SourceMembershipStatus.InsertPending,
        "ACTIVE" => SourceMembershipStatus.Active,
        "DELETE_PENDING" => SourceMembershipStatus.DeletePending,
        "INACTIVE" => SourceMembershipStatus.Inactive,
        "REACTIVATE_PENDING" => SourceMembershipStatus.ReactivatePending,
        "CONFLICT" => SourceMembershipStatus.Conflict,
        _ => throw new InvalidOperationException("Stored membership status is outside the allowlist."),
    };

    internal static string TargetAction(SourceMembershipTargetAction value) => value switch
    {
        SourceMembershipTargetAction.None => "NONE",
        SourceMembershipTargetAction.Upserted => "UPSERTED",
        SourceMembershipTargetAction.ExistingVerified => "EXISTING_VERIFIED",
        SourceMembershipTargetAction.HardDeleted => "HARD_DELETED",
        SourceMembershipTargetAction.PreservedExcluded => "PRESERVED_EXCLUDED",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    internal static string Reason(SourceMembershipReasonCode value)
        => value switch
        {
            SourceMembershipReasonCode.None => "NONE",
            SourceMembershipReasonCode.SourcePresent => "SOURCE_PRESENT",
            SourceMembershipReasonCode.SourceDelete => "SOURCE_DELETE",
            SourceMembershipReasonCode.FullReconcileAbsent => "FULL_RECONCILE_ABSENT",
            SourceMembershipReasonCode.ReactivatedAtSource => "REACTIVATED_AT_SOURCE",
            SourceMembershipReasonCode.TargetActionApplied => "TARGET_ACTION_APPLIED",
            SourceMembershipReasonCode.DuplicateReplay => "DUPLICATE_REPLAY",
            SourceMembershipReasonCode.LateSourceEvent => "LATE_SOURCE_EVENT",
            SourceMembershipReasonCode.StreamOwnershipConflict => "STREAM_OWNERSHIP_CONFLICT",
            SourceMembershipReasonCode.MappingFingerprintMismatch => "MAPPING_FINGERPRINT_MISMATCH",
            SourceMembershipReasonCode.RouteFingerprintMismatch => "ROUTE_FINGERPRINT_MISMATCH",
            SourceMembershipReasonCode.TargetEqualityUnproven => "TARGET_EQUALITY_UNPROVEN",
            SourceMembershipReasonCode.BootstrapIncomplete => "BOOTSTRAP_INCOMPLETE",
            SourceMembershipReasonCode.BootstrapMembershipCreated =>
                "BOOTSTRAP_MEMBERSHIP_CREATED",
            SourceMembershipReasonCode.BootstrapMembershipVerified =>
                "BOOTSTRAP_MEMBERSHIP_VERIFIED",
            SourceMembershipReasonCode.SourceRowObserved => "SOURCE_ROW_OBSERVED",
            SourceMembershipReasonCode.CtDeleteObserved => "CT_DELETE_OBSERVED",
            SourceMembershipReasonCode.DeletePendingNotApplied =>
                "DELETE_PENDING_NOT_APPLIED",
            SourceMembershipReasonCode.ReactivationCandidate =>
                "REACTIVATION_CANDIDATE",
            SourceMembershipReasonCode.TargetOnlyUnclassified =>
                "TARGET_ONLY_UNCLASSIFIED",
            SourceMembershipReasonCode.OwnershipConflict => "OWNERSHIP_CONFLICT",
            SourceMembershipReasonCode.CoverageComplete => "COVERAGE_COMPLETE",
            SourceMembershipReasonCode.CheckpointConflict => "CHECKPOINT_CONFLICT",
            SourceMembershipReasonCode.BootstrapParentMissing =>
                "BOOTSTRAP_PARENT_MISSING",
            SourceMembershipReasonCode.DeleteExecutionNotEnabled =>
                "DELETE_EXECUTION_NOT_ENABLED",
            SourceMembershipReasonCode.UnownedDeleteKey => "UNOWNED_DELETE_KEY",
            SourceMembershipReasonCode.BlockDeleteConflict => "BLOCK_DELETE_CONFLICT",
            SourceMembershipReasonCode.ManualConflict => "MANUAL_CONFLICT",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    internal static string CycleStatus(SyncCycleStatus value) => value switch
    {
        SyncCycleStatus.Preparing => "PREPARING",
        SyncCycleStatus.Staged => "STAGED",
        SyncCycleStatus.Validated => "VALIDATED",
        SyncCycleStatus.TargetCommitting => "TARGET_COMMITTING",
        SyncCycleStatus.TargetCommitted => "TARGET_COMMITTED",
        SyncCycleStatus.CheckpointPublished => "CHECKPOINT_PUBLISHED",
        SyncCycleStatus.Complete => "COMPLETE",
        SyncCycleStatus.Failed => "FAILED",
        SyncCycleStatus.Conflict => "CONFLICT",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    internal static SyncCycleStatus ParseCycleStatus(string value) => value switch
    {
        "PREPARING" => SyncCycleStatus.Preparing,
        "STAGED" => SyncCycleStatus.Staged,
        "VALIDATED" => SyncCycleStatus.Validated,
        "TARGET_COMMITTING" => SyncCycleStatus.TargetCommitting,
        "TARGET_COMMITTED" => SyncCycleStatus.TargetCommitted,
        "CHECKPOINT_PUBLISHED" => SyncCycleStatus.CheckpointPublished,
        "COMPLETE" => SyncCycleStatus.Complete,
        "FAILED" => SyncCycleStatus.Failed,
        "CONFLICT" => SyncCycleStatus.Conflict,
        _ => throw new InvalidOperationException(
            "Stored cycle status is outside the allowlist."),
    };

    internal static string DomainStatus(SyncCycleDomainStatus value) => value switch
    {
        SyncCycleDomainStatus.Pending => "PENDING",
        SyncCycleDomainStatus.Staged => "STAGED",
        SyncCycleDomainStatus.Validated => "VALIDATED",
        SyncCycleDomainStatus.Committed => "COMMITTED",
        SyncCycleDomainStatus.Failed => "FAILED",
        SyncCycleDomainStatus.Conflict => "CONFLICT",
        SyncCycleDomainStatus.Skipped => "SKIPPED",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    internal static void RequireConflictReason(SourceMembershipReasonCode reason)
    {
        if (reason is not (
                SourceMembershipReasonCode.StreamOwnershipConflict or
                SourceMembershipReasonCode.MappingFingerprintMismatch or
                SourceMembershipReasonCode.RouteFingerprintMismatch or
                SourceMembershipReasonCode.TargetEqualityUnproven or
                SourceMembershipReasonCode.BootstrapIncomplete or
                SourceMembershipReasonCode.OwnershipConflict or
                SourceMembershipReasonCode.CheckpointConflict or
                SourceMembershipReasonCode.BootstrapParentMissing or
                SourceMembershipReasonCode.UnownedDeleteKey or
                SourceMembershipReasonCode.BlockDeleteConflict or
                SourceMembershipReasonCode.ManualConflict))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
    }

    internal static void RequireErrorCode(string errorCode)
    {
        if (!ErrorCodes.Contains(errorCode))
        {
            throw new ArgumentException(
                "Cycle error code is outside the fixed allowlist.",
                nameof(errorCode));
        }
    }
}

internal sealed class ControlPlaneOwnershipConflictException : InvalidOperationException
{
    internal ControlPlaneOwnershipConflictException()
        : base("The target key is already reserved by another control-plane membership.")
    {
    }
}

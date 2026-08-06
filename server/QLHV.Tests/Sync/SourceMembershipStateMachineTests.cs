using System.Security.Cryptography;
using System.Text;
using QLHV.Application.Sync.Realtime.ControlPlane;

namespace QLHV.Tests.Sync;

public sealed class SourceMembershipStateMachineTests
{
    public static IEnumerable<object?[]> ValidTransitions()
    {
        yield return [null, SourceMembershipStatus.InsertPending];
        yield return [SourceMembershipStatus.InsertPending, SourceMembershipStatus.Active];
        yield return [SourceMembershipStatus.Active, SourceMembershipStatus.Active];
        yield return [SourceMembershipStatus.Active, SourceMembershipStatus.DeletePending];
        yield return [SourceMembershipStatus.DeletePending, SourceMembershipStatus.Inactive];
        yield return [SourceMembershipStatus.Inactive, SourceMembershipStatus.Inactive];
        yield return [SourceMembershipStatus.Inactive, SourceMembershipStatus.ReactivatePending];
        yield return [SourceMembershipStatus.ReactivatePending, SourceMembershipStatus.Active];
        yield return [null, SourceMembershipStatus.Conflict];
        foreach (var before in Enum.GetValues<SourceMembershipStatus>())
        {
            yield return [before, SourceMembershipStatus.Conflict];
        }
    }

    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void Every_contract_transition_is_valid(
        SourceMembershipStatus? before,
        SourceMembershipStatus after)
        => Assert.True(SourceMembershipStateMachine.CanTransition(before, after));

    [Fact]
    public void Every_other_membership_transition_is_invalid()
    {
        var possibleBefore = new SourceMembershipStatus?[] { null }
            .Concat(Enum.GetValues<SourceMembershipStatus>().Cast<SourceMembershipStatus?>());
        var possibleAfter = Enum.GetValues<SourceMembershipStatus>();
        var valid = ValidTransitions()
            .Select(row => ((SourceMembershipStatus?)row[0], (SourceMembershipStatus)row[1]!))
            .ToHashSet();

        foreach (var before in possibleBefore)
        {
            foreach (var after in possibleAfter)
            {
                if (!valid.Contains((before, after)))
                {
                    Assert.False(SourceMembershipStateMachine.CanTransition(before, after));
                }
            }
        }
    }

    [Fact]
    public void Source_versions_are_non_negative_and_observations_are_monotonic()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SourceMembershipStateMachine.CreateInsertPending(-1, Fingerprint(1), Fingerprint(2)));

        var active = ActiveAt(10);
        var older = SourceMembershipStateMachine.ObserveActive(
            active,
            9,
            Fingerprint(1),
            Fingerprint(2));

        Assert.Equal(MembershipTransitionOutcome.IdempotentNoOp, older.Outcome);
        Assert.Equal(10, older.State.LastObservedSourceVersion);
        Assert.Equal(10, older.State.AppliedSourceVersion);
    }

    [Fact]
    public void Duplicate_or_older_delete_is_an_idempotent_no_op()
    {
        var active = ActiveAt(20);
        var duplicate = SourceMembershipStateMachine.CreateDeletePending(
            active,
            20,
            SourceMembershipReasonCode.SourceDelete,
            Fingerprint(1),
            Fingerprint(2));
        var older = SourceMembershipStateMachine.CreateDeletePending(
            active,
            19,
            SourceMembershipReasonCode.SourceDelete,
            Fingerprint(1),
            Fingerprint(2));

        Assert.Equal(MembershipTransitionOutcome.IdempotentNoOp, duplicate.Outcome);
        Assert.Equal(MembershipTransitionOutcome.IdempotentNoOp, older.Outcome);
        Assert.Equal(SourceMembershipStatus.Active, duplicate.State.Status);
    }

    [Fact]
    public void Duplicate_reactivation_is_an_idempotent_no_op()
    {
        var inactive = InactiveAt(30);
        var pending = SourceMembershipStateMachine.CreateReactivatePending(
            inactive,
            40,
            Fingerprint(1),
            Fingerprint(2)).State;
        var active = SourceMembershipStateMachine.ApplyReactivated(pending).State;

        var replay = SourceMembershipStateMachine.CreateReactivatePending(
            active,
            40,
            Fingerprint(1),
            Fingerprint(2));
        var older = SourceMembershipStateMachine.CreateReactivatePending(
            active,
            39,
            Fingerprint(1),
            Fingerprint(2));

        Assert.Equal(MembershipTransitionOutcome.IdempotentNoOp, replay.Outcome);
        Assert.Equal(MembershipTransitionOutcome.IdempotentNoOp, older.Outcome);
        Assert.Equal(SourceMembershipStatus.Active, replay.State.Status);
    }

    [Fact]
    public void Late_tombstone_cannot_deactivate_a_newer_reactivation()
    {
        var inactive = InactiveAt(30);
        var pending = SourceMembershipStateMachine.CreateReactivatePending(
            inactive,
            50,
            Fingerprint(1),
            Fingerprint(2)).State;
        var active = SourceMembershipStateMachine.ApplyReactivated(pending).State;

        var lateDelete = SourceMembershipStateMachine.CreateDeletePending(
            active,
            49,
            SourceMembershipReasonCode.SourceDelete,
            Fingerprint(1),
            Fingerprint(2));

        Assert.Equal(MembershipTransitionOutcome.IdempotentNoOp, lateDelete.Outcome);
        Assert.True(lateDelete.State.IsActive);
        Assert.Equal(50, lateDelete.State.ReactivatedAtSourceVersion);
    }

    [Fact]
    public void Applying_delete_keeps_target_ownership_reserved_while_inactive()
    {
        var pending = SourceMembershipStateMachine.CreateDeletePending(
            ActiveAt(10),
            11,
            SourceMembershipReasonCode.SourceDelete,
            Fingerprint(1),
            Fingerprint(2)).State;
        var inactive = SourceMembershipStateMachine.ApplyInactive(
            pending,
            SourceMembershipTargetAction.PreservedExcluded).State;

        Assert.Equal(SourceMembershipStatus.Inactive, inactive.Status);
        Assert.False(inactive.IsActive);
        Assert.False(inactive.ClaimsTargetKey);
        Assert.True(inactive.OwnershipReserved);
    }

    [Fact]
    public void Different_stream_claim_for_the_same_target_key_is_a_conflict()
    {
        var existing = new OwnershipReservation(
            1,
            "OTO_V1",
            "OTO_V2",
            "OTO_V2_TO_V1",
            "66029",
            "NguoiLX",
            true);
        var requested = new MembershipRoute(
            "OTO_V1",
            "OTHER_SOURCE",
            "OTHER_STREAM",
            "66029",
            "NguoiLX");

        Assert.True(
            SourceMembershipStateMachine.IsDifferentStreamOwnershipConflict(
                existing,
                requested));
    }

    [Fact]
    public void Mapping_fingerprint_mismatch_marks_conflict_fail_closed()
    {
        var result = SourceMembershipStateMachine.ObserveActive(
            ActiveAt(10),
            11,
            Fingerprint(99),
            Fingerprint(2));

        Assert.Equal(MembershipTransitionOutcome.Conflict, result.Outcome);
        Assert.Equal(SourceMembershipStatus.Conflict, result.State.Status);
        Assert.Equal(
            SourceMembershipReasonCode.MappingFingerprintMismatch,
            result.State.ReasonCode);
        Assert.False(result.State.IsActive);
    }

    [Fact]
    public void Route_fingerprint_mismatch_marks_conflict_fail_closed()
    {
        var result = SourceMembershipStateMachine.ObserveActive(
            ActiveAt(10),
            11,
            Fingerprint(1),
            Fingerprint(99));

        Assert.Equal(MembershipTransitionOutcome.Conflict, result.Outcome);
        Assert.Equal(SourceMembershipStatus.Conflict, result.State.Status);
        Assert.Equal(
            SourceMembershipReasonCode.RouteFingerprintMismatch,
            result.State.ReasonCode);
    }

    [Theory]
    [InlineData("DM_DonViGTVT.MaDV", "66029", null, "514C48560001000101000000053636303239")]
    [InlineData("KhoaHoc.MaKH", "66029K260001", null, "514C485600010001010000000C36363032394B323630303031")]
    [InlineData("BaoCaoI.MaBCI", "BCI-001", null, "514C48560001000101000000074243492D303031")]
    [InlineData("NguoiLX.MaDK", "66029-000001", null, "514C485600010001010000000C36363032392D303030303031")]
    [InlineData("NguoiLX_HoSo.MaDK", "66029-000001", null, "514C485600010001010000000C36363032392D303030303031")]
    [InlineData("NguoiLXHS_GiayTo", "GT01", "66029-000001", "514C485600010002010000000447543031010000000C36363032392D303030303031")]
    public void Canonical_key_vectors_are_stable(
        string contractName,
        string first,
        string? second,
        string expectedHex)
    {
        var components = second is null
            ? new[] { CanonicalKeyComponent.FromString(first) }
            :
            [
                CanonicalKeyComponent.FromString(first),
                CanonicalKeyComponent.FromString(second),
            ];

        var encoded = CanonicalBusinessKeyEncoder.Encode(1, components);

        Assert.False(string.IsNullOrWhiteSpace(contractName));
        Assert.Equal(expectedHex, Convert.ToHexString(encoded.ToArray()));
    }

    [Fact]
    public void Length_prefixing_prevents_delimiter_collision()
    {
        var left = CanonicalBusinessKeyEncoder.Encode(
            1,
            CanonicalKeyComponent.FromString("a|b"),
            CanonicalKeyComponent.FromString("c"));
        var right = CanonicalBusinessKeyEncoder.Encode(
            1,
            CanonicalKeyComponent.FromString("a"),
            CanonicalKeyComponent.FromString("b|c"));

        Assert.NotEqual(left.ToArray(), right.ToArray());
    }

    [Fact]
    public void Composite_component_order_changes_the_key()
    {
        var first = CanonicalBusinessKeyEncoder.Encode(
            1,
            CanonicalKeyComponent.FromString("GT01"),
            CanonicalKeyComponent.FromString("MaDK"));
        var reversed = CanonicalBusinessKeyEncoder.Encode(
            1,
            CanonicalKeyComponent.FromString("MaDK"),
            CanonicalKeyComponent.FromString("GT01"));

        Assert.NotEqual(first.ToArray(), reversed.ToArray());
    }

    [Fact]
    public void Case_and_space_bytes_are_preserved()
    {
        var exact = CanonicalBusinessKeyEncoder.Encode(
            1,
            CanonicalKeyComponent.FromString("Abc "));
        var caseChanged = CanonicalBusinessKeyEncoder.Encode(
            1,
            CanonicalKeyComponent.FromString("abc "));
        var trimmed = CanonicalBusinessKeyEncoder.Encode(
            1,
            CanonicalKeyComponent.FromString("Abc"));

        Assert.NotEqual(exact.ToArray(), caseChanged.ToArray());
        Assert.NotEqual(exact.ToArray(), trimmed.ToArray());
    }

    [Fact]
    public async Task Diagnostic_identity_is_versioned_hmac_not_plain_sha256()
    {
        var canonical = CanonicalBusinessKeyEncoder.Encode(
            1,
            CanonicalKeyComponent.FromString("sensitive-key"));
        var hasher = new HmacSha256DiagnosticKeyHasher(
            new FixedHmacKeyProvider(7, Enumerable.Repeat((byte)0x5a, 32).ToArray()));

        var diagnostic = await hasher.ComputeAsync(canonical);
        var plain = SHA256.HashData(canonical.ToArray());

        Assert.Equal(7, diagnostic.KeyVersion);
        Assert.False(CryptographicOperations.FixedTimeEquals(plain, diagnostic.ToArray()));
    }

    [Fact]
    public void Operational_key_does_not_appear_in_ToString_or_pending_exception()
    {
        const string raw = "sensitive|66029-000001";
        var canonical = CanonicalBusinessKeyEncoder.Encode(
            1,
            CanonicalKeyComponent.FromString(raw));
        var target = TargetEqualityKey.Pending(Encoding.UTF8.GetBytes(raw));

        var exception = Assert.Throws<TargetEqualityNotVerifiedException>(
            target.EnsureTypedClaimForMutation);

        Assert.DoesNotContain(raw, canonical.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(raw, target.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(raw, exception.Message, StringComparison.Ordinal);
    }

    private static SourceMembershipState ActiveAt(long version)
    {
        var pending = SourceMembershipStateMachine.CreateInsertPending(
            version,
            Fingerprint(1),
            Fingerprint(2));
        return SourceMembershipStateMachine.ApplyActive(pending).State;
    }

    private static SourceMembershipState InactiveAt(long version)
    {
        var active = ActiveAt(version - 1);
        var pending = SourceMembershipStateMachine.CreateDeletePending(
            active,
            version,
            SourceMembershipReasonCode.SourceDelete,
            Fingerprint(1),
            Fingerprint(2)).State;
        return SourceMembershipStateMachine.ApplyInactive(
            pending,
            SourceMembershipTargetAction.PreservedExcluded).State;
    }

    private static ControlPlaneFingerprint Fingerprint(byte value)
        => new(Enumerable.Repeat(value, SHA256.HashSizeInBytes).ToArray());

    private sealed class FixedHmacKeyProvider : IDiagnosticHmacKeyProvider
    {
        private readonly DiagnosticHmacKeyMaterial _material;

        internal FixedHmacKeyProvider(int version, byte[] key)
        {
            _material = new DiagnosticHmacKeyMaterial(version, key);
        }

        public ValueTask<DiagnosticHmacKeyMaterial> GetCurrentKeyAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_material);
    }
}

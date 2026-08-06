using QLHV.Application.Sync.Rt03;

namespace QLHV.Tests.Sync.Rt03;

public sealed class Rt03ReviewedRetainedPolicyTests
{
    [Fact]
    public void Exact_reviewed_retained_evidence_is_a_safe_steady_state()
    {
        var result = Evaluate(Valid());

        Assert.Equal(Rt03ReviewedRetainedContract.HealthyState, result.State);
        Assert.Equal(Rt03ReviewedRetainedReasonCodes.ReviewedAndRetained,
            result.ReasonCode);
        Assert.True(result.IsReviewedRetained);
        Assert.True(result.IsSafeSteadyState);
        Assert.True(result.WritesAllowed);
    }

    [Fact]
    public void Same_evidence_is_deterministic_and_redacted()
    {
        var first = Evaluate(Valid());
        var second = Evaluate(Valid());

        Assert.Equal(first, second);
        Assert.StartsWith("RT03-V9-", first.DiagnosticId, StringComparison.Ordinal);
        Assert.DoesNotContain("66029", first.DiagnosticId, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Rt03ReviewedRetainedContext.IncrementalWorker)]
    [InlineData(Rt03ReviewedRetainedContext.NoChangeCycle)]
    [InlineData(Rt03ReviewedRetainedContext.FullConvergence)]
    [InlineData(Rt03ReviewedRetainedContext.RecoveryVerification)]
    [InlineData(Rt03ReviewedRetainedContext.ProductionPreflight)]
    [InlineData(Rt03ReviewedRetainedContext.RuntimeDiagnostics)]
    public void Every_context_uses_the_same_fail_closed_decision(
        Rt03ReviewedRetainedContext context)
    {
        var result = Rt03ReviewedRetainedPolicy.Evaluate(Valid(), context);

        Assert.True(result.IsSafeSteadyState);
        Assert.Equal(Rt03ReviewedRetainedReasonCodes.ReviewedAndRetained,
            result.ReasonCode);
    }

    [Fact]
    public void New_source_event_makes_review_stale()
        => AssertBlocked(Valid() with { HasNewSourceEvent = true },
            Rt03ReviewedRetainedReasonCodes.ReviewStale);

    [Fact]
    public void Source_fingerprint_change_fails_closed()
        => AssertBlocked(Valid() with { CurrentSourceFingerprint = Hash('b') },
            Rt03ReviewedRetainedReasonCodes.ReviewSourceChanged);

    [Fact]
    public void Target_fingerprint_change_fails_closed()
        => AssertBlocked(Valid() with { CurrentTargetFingerprint = Hash('c') },
            Rt03ReviewedRetainedReasonCodes.ReviewTargetChanged);

    [Fact]
    public void Qlhv_owned_fingerprint_change_fails_closed()
        => AssertBlocked(Valid() with { CurrentOwnershipFingerprint = Hash('d') },
            Rt03ReviewedRetainedReasonCodes.ReviewTargetChanged);

    [Fact]
    public void Reviewed_field_set_change_fails_closed()
        => AssertBlocked(Valid() with { CurrentFieldSet = "AnhRelativePath" },
            Rt03ReviewedRetainedReasonCodes.ReviewFieldSetChanged);

    [Fact]
    public void Drift_outside_reviewed_fields_fails_closed()
        => AssertBlocked(Valid() with { HasNewDriftOutsideReviewedFields = true },
            Rt03ReviewedRetainedReasonCodes.ReviewFieldSetChanged);

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 2, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, 2)]
    public void Missing_duplicate_or_conflicting_identity_fails_closed(
        int sourceCount,
        int targetCount,
        int reviewCount)
        => AssertBlocked(Valid() with
            {
                SourceIdentityCount = sourceCount,
                LiveTargetIdentityCount = targetCount,
                ActiveReviewCount = reviewCount,
            }, Rt03ReviewedRetainedReasonCodes.ReviewIdentityAmbiguous);

    [Fact]
    public void Missing_target_identity_fails_closed()
        => AssertBlocked(Valid() with { TargetIdentity = null },
            Rt03ReviewedRetainedReasonCodes.ReviewIdentityAmbiguous);

    [Fact]
    public void Closed_or_cancelled_review_fails_closed()
        => AssertBlocked(Valid() with { ReviewIsActive = false },
            Rt03ReviewedRetainedReasonCodes.ReviewStale);

    [Fact]
    public void Checkpoint_below_reviewed_event_fails_closed()
        => AssertBlocked(Valid() with { CheckpointVersion = 122 },
            Rt03ReviewedRetainedReasonCodes.ReviewStale);

    [Fact]
    public void Marker_checkpoint_mismatch_fails_closed()
        => AssertBlocked(Valid() with { MarkerCheckpointAtomic = false },
            Rt03ReviewedRetainedReasonCodes.ReviewStale);

    [Fact]
    public void Target_not_retained_active_fails_closed()
        => AssertBlocked(Valid() with { TargetRetainedActive = false },
            Rt03ReviewedRetainedReasonCodes.ReviewStale);

    [Fact]
    public void Target_mutated_flag_fails_closed()
        => AssertBlocked(Valid() with { TargetMutated = true },
            Rt03ReviewedRetainedReasonCodes.ReviewStale);

    [Theory]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("ownership")]
    [InlineData("identity")]
    public void Missing_required_hash_evidence_fails_closed(string field)
    {
        var input = field switch
        {
            "source" => Valid() with { ReviewedSourceFingerprint = string.Empty },
            "target" => Valid() with { ReviewedTargetFingerprint = string.Empty },
            "ownership" => Valid() with { ReviewedOwnershipFingerprint = string.Empty },
            _ => Valid() with { SourceBusinessIdentityHash = string.Empty },
        };
        AssertBlocked(input, Rt03ReviewedRetainedReasonCodes.ReviewEvidenceIncomplete);
    }

    [Fact]
    public void Legacy_review_without_v9_contract_fails_closed()
        => AssertBlocked(Valid() with { EvidenceContractVersion = string.Empty },
            Rt03ReviewedRetainedReasonCodes.ReviewEvidenceIncomplete);

    [Fact]
    public void Unexpected_drift_classification_fails_closed()
        => AssertBlocked(Valid() with { DriftClassification = "STALE_IMPORTED_VALUE" },
            Rt03ReviewedRetainedReasonCodes.ReviewEvidenceIncomplete);

    [Fact]
    public void Invalid_review_version_fails_closed()
        => AssertBlocked(Valid() with { ReviewVersion = -1 },
            Rt03ReviewedRetainedReasonCodes.ReviewEvidenceIncomplete);

    [Fact]
    public void Field_set_normalization_is_ordered_and_exact()
    {
        Assert.Equal("ChatLuongAnh,NgayThuNhanAnh",
            Rt03ReviewedRetainedPolicy.NormalizeFieldSet(
                "NgayThuNhanAnh, ChatLuongAnh,NgayThuNhanAnh"));
    }

    [Fact]
    public void Source_delete_or_deactivation_is_not_a_safe_reviewed_state()
        => AssertBlocked(Valid() with { SourceIdentityCount = 0 },
            Rt03ReviewedRetainedReasonCodes.ReviewIdentityAmbiguous);

    private static Rt03ReviewedRetainedEvaluation Evaluate(
        Rt03ReviewedRetainedInput input)
        => Rt03ReviewedRetainedPolicy.Evaluate(
            input,
            Rt03ReviewedRetainedContext.NoChangeCycle);

    private static void AssertBlocked(
        Rt03ReviewedRetainedInput input,
        string reason)
    {
        var result = Evaluate(input);
        Assert.Equal(Rt03ReviewedRetainedContract.BlockedState, result.State);
        Assert.Equal(reason, result.ReasonCode);
        Assert.False(result.IsReviewedRetained);
        Assert.False(result.IsSafeSteadyState);
        Assert.False(result.WritesAllowed);
    }

    private static Rt03ReviewedRetainedInput Valid() => new()
    {
        SourceProfileCode = Rt03Profiles.Oto,
        DomainCode = Rt03ReviewedRetainedContract.DomainLearner,
        SourceBusinessIdentityHash = Hash('1'),
        TargetIdentity = 42,
        DriftClassification = Rt03ReviewedRetainedContract.PhotoClassification,
        ReviewedFieldSet = "NgayThuNhanAnh",
        CurrentFieldSet = "NgayThuNhanAnh",
        SourceVersion = 124,
        ReviewVersion = 123,
        CheckpointVersion = 124,
        SourceIdentityCount = 1,
        LiveTargetIdentityCount = 1,
        ActiveReviewCount = 1,
        MarkerCheckpointAtomic = true,
        TargetRetainedActive = true,
        TargetMutated = false,
        ReviewIsActive = true,
        HasNewSourceEvent = false,
        HasNewDriftOutsideReviewedFields = false,
        ReviewedSourceFingerprint = Hash('2'),
        CurrentSourceFingerprint = Hash('2'),
        ReviewedTargetFingerprint = Hash('3'),
        CurrentTargetFingerprint = Hash('3'),
        ReviewedOwnershipFingerprint = Hash('4'),
        CurrentOwnershipFingerprint = Hash('4'),
        EvidenceContractVersion = Rt03ReviewedRetainedContract.Version,
    };

    private static string Hash(char value) => new(value, 64);
}

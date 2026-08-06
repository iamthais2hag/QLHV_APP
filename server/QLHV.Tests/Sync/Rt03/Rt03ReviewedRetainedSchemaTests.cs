namespace QLHV.Tests.Sync.Rt03;

public sealed class Rt03ReviewedRetainedSchemaTests
{
    private static readonly string Root = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void V9_schema_is_additive_and_keeps_legacy_reviews_unchanged()
    {
        var sql = Read("database/patches/20260731_add_rt03_v9_reviewed_retained.sql");

        Assert.Contains("EvidenceContractVersion", sql, StringComparison.Ordinal);
        Assert.Contains("SourceFingerprint", sql, StringComparison.Ordinal);
        Assert.Contains("TargetFingerprint", sql, StringComparison.Ordinal);
        Assert.Contains("QlhvOwnedFingerprint", sql, StringComparison.Ordinal);
        Assert.Contains("SupersedesManualReviewId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE dbo.App_QlhvDirectRealtimeManualReview", sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM dbo.App_QlhvDirectRealtimeManualReview", sql,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void V9_schema_adds_required_non_pii_observability_fields()
    {
        var sql = Read("database/patches/20260731_add_rt03_v9_reviewed_retained.sql");
        foreach (var field in new[]
                 {
                     "ReviewedRetainedCount", "ReviewedRetainedDomains",
                     "ActiveReviewCount", "StaleReviewCount", "NewDriftCount",
                     "OldestActiveReviewUtc", "NewestActiveReviewUtc", "CycleOutcome",
                 })
        {
            Assert.Contains(field, sql, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("MaDK", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SoCCCD", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void V9_evidence_is_immutable_and_unique_by_hash_and_predecessor()
    {
        var sql = Read("database/patches/20260731_add_rt03_v9_reviewed_retained.sql");

        Assert.Contains("UX_App_QlhvDirectRealtimeManualReview_V9EvidenceHash", sql,
            StringComparison.Ordinal);
        Assert.Contains("UX_App_QlhvDirectRealtimeManualReview_V9Supersedes", sql,
            StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY(SupersedesManualReviewId)", sql,
            StringComparison.Ordinal);
        Assert.Contains("RT03-REVIEWED-RETAINED-1.0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void V9_rollback_refuses_to_remove_committed_review_evidence()
    {
        var sql = Read("database/patches/20260731_rollback_rt03_v9_reviewed_retained.sql");

        Assert.Contains("RT03_V9_ROLLBACK_REVIEW_EVIDENCE_EXISTS", sql,
            StringComparison.Ordinal);
        Assert.Contains("RT03_V9_ROLLBACK_WORKER_ACTIVE", sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void V9_schema_does_not_touch_prohibited_domains()
    {
        foreach (var relative in new[]
                 {
                     "database/patches/20260731_add_rt03_v9_reviewed_retained.sql",
                     "database/patches/20260731_rollback_rt03_v9_reviewed_retained.sql",
                 })
        {
            var sql = Read(relative);
            Assert.DoesNotContain("App_HocVien_PhanCong", sql,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("BaoCaoI", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("NgayThuNhanAnh=", sql,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CHANGE_TRACKING", sql,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Full_convergence_writer_preserves_shared_evaluator_approved_targets()
    {
        var sql = Read("server/QLHV.Infrastructure/Sync/QlhvFullSnapshotSyncSql.cs");
        var repository = Read(
            "server/QLHV.Infrastructure/Sync/QlhvHocVienTargetRepository.cs");

        Assert.Contains("RetainReviewedTarget BIT", sql, StringComparison.Ordinal);
        Assert.Contains(
            "WHEN MATCHED AND source.RetainReviewedTarget = 0",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("ReviewedRetainedSourceBusinessIdentityHashes", repository,
            StringComparison.Ordinal);
        Assert.Contains("Rt03ReviewedRetainedFingerprints.SourceBusinessIdentity",
            repository, StringComparison.Ordinal);
    }

    [Fact]
    public void Full_convergence_and_recovery_verification_use_shared_evaluator()
    {
        var service = Read(
            "server/QLHV.Infrastructure/Sync/Rt03/Rt03FullConvergenceRecoveryService.cs");

        Assert.Contains("Rt03ReviewedRetainedContext.FullConvergence", service,
            StringComparison.Ordinal);
        Assert.Contains("Rt03ReviewedRetainedContext.RecoveryVerification", service,
            StringComparison.Ordinal);
        Assert.Contains("RequireSafeReviewedRetained", service,
            StringComparison.Ordinal);
    }

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
}

using QLHV.Application.Sync.Rt03;

namespace QLHV.Tests.Sync.Rt03;

public sealed class Rt03ChangeTrackingEventClassifierTests
{
    [Fact]
    public void Historical_photo_capture_mask_is_manual_review()
    {
        var classification = Rt03ChangeTrackingEventClassifier.Classify(
            "dbo.NguoiLX_HoSo",
            "U",
            ["TT_XuLy", "DuongDanAnh", "ChatLuongAnh", "NgayThuNhanAnh", "NguoiThuNhanAnh"]);

        Assert.Equal(
            Rt03ChangeTrackingClassifications.MultiFieldPhotoDrift,
            classification);
    }

    [Fact]
    public void Unmapped_processing_status_only_can_advance_without_target_mutation()
    {
        Assert.Equal(
            Rt03ChangeTrackingClassifications.NoMappedChange,
            Rt03ChangeTrackingEventClassifier.Classify(
                "dbo.NguoiLX_HoSo", "U", ["TT_XuLy"]));
    }

    [Theory]
    [InlineData("dbo.NguoiLX_HoSo", "D")]
    [InlineData("dbo.NguoiLX_HoSo", "I")]
    [InlineData("dbo.NguoiLX", "U")]
    public void Delete_insert_and_other_table_events_remain_fail_closed(
        string table,
        string operation)
    {
        Assert.Equal(
            Rt03ChangeTrackingClassifications.UnknownUnsafe,
            Rt03ChangeTrackingEventClassifier.Classify(
                table, operation, ["DuongDanAnh"]));
    }

    [Fact]
    public void Unknown_column_mixed_into_photo_event_remains_fail_closed()
    {
        Assert.Equal(
            Rt03ChangeTrackingClassifications.UnknownUnsafe,
            Rt03ChangeTrackingEventClassifier.Classify(
                "dbo.NguoiLX_HoSo", "U", ["DuongDanAnh", "UnknownField"]));
    }

    [Fact]
    public void Course_insert_with_null_change_mask_is_source_owned_insert()
    {
        Assert.Equal(
            Rt03ChangeTrackingClassifications.KhoaHocSourceInsert,
            Rt03ChangeTrackingEventClassifier.Classify(
                "dbo.KhoaHoc", "I", []));
    }

    [Fact]
    public void Course_update_with_reviewed_columns_is_source_owned_update()
    {
        Assert.Equal(
            Rt03ChangeTrackingClassifications.KhoaHocSourceUpdate,
            Rt03ChangeTrackingEventClassifier.Classify(
                "dbo.KhoaHoc", "U", ["NgayKG", "TrangThai"]));
    }

    [Fact]
    public void Course_forward_column_is_explicitly_unclassified()
    {
        Assert.Equal(
            Rt03ChangeTrackingClassifications.UnclassifiedForwardColumn,
            Rt03ChangeTrackingEventClassifier.Classify(
                "dbo.KhoaHoc",
                "U",
                [Rt03ChangeTrackingEventClassifier.ForwardColumnSentinel]));
    }

    [Fact]
    public void Course_delete_remains_fail_closed()
    {
        Assert.Equal(
            Rt03ChangeTrackingClassifications.UnknownUnsafe,
            Rt03ChangeTrackingEventClassifier.Classify(
                "dbo.KhoaHoc", "D", []));
    }

    [Theory]
    [InlineData("dbo.GiaoVien", "I")]
    [InlineData("dbo.XeTap", "U")]
    [InlineData("dbo.KhoaHoc_GiaoVien", "D")]
    [InlineData("dbo.KhoaHoc_XeTap", "U")]
    public void Projection_events_are_advanced_only_after_projection_coordinator(
        string table,
        string operation)
    {
        Assert.Equal(
            Rt03ChangeTrackingClassifications.NoMappedChange,
            Rt03ChangeTrackingEventClassifier.Classify(table, operation, []));
    }

    [Fact]
    public void Concurrent_source_commit_is_retryable_but_unsupported_drift_is_not()
    {
        Assert.True(Rt03WorkerFailurePolicy.IsRetryable(
            Rt03Errors.SourceChangedDuringPlan));
        Assert.False(Rt03WorkerFailurePolicy.IsRetryable(
            Rt03Errors.UnsupportedDrift));
        Assert.False(Rt03WorkerFailurePolicy.IsRetryable(
            Rt03Errors.TimeAuthorityBlocked));
        Assert.False(Rt03WorkerFailurePolicy.IsRetryable(null));
    }
}

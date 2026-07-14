using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class MotoCenterTransferRunHistoryRepositoryTests
{
    [Fact]
    public void Normalize_take_uses_default_and_max_limit()
    {
        Assert.Equal(50, MotoCenterTransferRunHistoryRepository.NormalizeTake(0));
        Assert.Equal(50, MotoCenterTransferRunHistoryRepository.NormalizeTake(-1));
        Assert.Equal(75, MotoCenterTransferRunHistoryRepository.NormalizeTake(75));
        Assert.Equal(200, MotoCenterTransferRunHistoryRepository.NormalizeTake(500));
    }

    [Fact]
    public void List_query_orders_newest_first()
    {
        Assert.Contains(
            "ORDER BY StartedAt DESC, Id DESC",
            MotoCenterTransferRunHistoryRepository.ListOrderBySql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void List_query_filters_by_ma_khoa_hoc()
    {
        var sql = MotoCenterTransferRunHistoryRepository.BuildListSql(new()
        {
            MaKhoaHoc = "66016",
        });

        Assert.Contains("MaKhoaHocCu LIKE @MaKhoaHocLike", sql, StringComparison.Ordinal);
        Assert.Contains("MaKhoaHocMoi LIKE @MaKhoaHocLike", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY StartedAt DESC, Id DESC", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("66016", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void List_query_filters_by_ma_csdt()
    {
        var sql = MotoCenterTransferRunHistoryRepository.BuildListSql(new()
        {
            MaCSDT = "01001",
        });

        Assert.Contains("MaCSDTCu LIKE @MaCSDTLike", sql, StringComparison.Ordinal);
        Assert.Contains("MaCSDTMoi LIKE @MaCSDTLike", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("01001", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void List_query_filters_by_status_and_executed()
    {
        var sql = MotoCenterTransferRunHistoryRepository.BuildListSql(new()
        {
            Status = "ThanhCong",
            Executed = true,
        });

        Assert.Contains("Status = @Status", sql, StringComparison.Ordinal);
        Assert.Contains("Executed = @Executed", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ThanhCong", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void List_query_combines_filters_and_keeps_newest_first()
    {
        var sql = MotoCenterTransferRunHistoryRepository.BuildListSql(new()
        {
            MaKhoaHoc = "66016",
            MaCSDT = "01001",
            Status = "BiChan",
            Executed = false,
        });

        Assert.Contains("WHERE", sql, StringComparison.Ordinal);
        Assert.Contains("  AND ", sql, StringComparison.Ordinal);
        Assert.Contains("MaKhoaHocCu LIKE @MaKhoaHocLike", sql, StringComparison.Ordinal);
        Assert.Contains("MaCSDTCu LIKE @MaCSDTLike", sql, StringComparison.Ordinal);
        Assert.Contains("Status = @Status", sql, StringComparison.Ordinal);
        Assert.Contains("Executed = @Executed", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY StartedAt DESC, Id DESC", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_masks_connection_secret_tokens()
    {
        var sanitized = MotoCenterTransferRunHistoryRepository.Sanitize(
            "Server=.;User ID=sa;Password=secret;Pwd=secret2;UID=user;");

        Assert.DoesNotContain("secret", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sa", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Password=<masked>", sanitized, StringComparison.Ordinal);
        Assert.Contains("Pwd=<masked>", sanitized, StringComparison.Ordinal);
        Assert.Contains("User ID=<masked>", sanitized, StringComparison.Ordinal);
        Assert.Contains("UID=<masked>", sanitized, StringComparison.Ordinal);
    }
}

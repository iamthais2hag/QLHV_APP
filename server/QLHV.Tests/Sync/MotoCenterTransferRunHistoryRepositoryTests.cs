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

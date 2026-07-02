using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class MotoSyncRunHistoryRepositoryTests
{
    [Fact]
    public void Normalize_take_uses_default_and_max_limit()
    {
        Assert.Equal(50, MotoSyncRunHistoryRepository.NormalizeTake(0));
        Assert.Equal(50, MotoSyncRunHistoryRepository.NormalizeTake(-1));
        Assert.Equal(75, MotoSyncRunHistoryRepository.NormalizeTake(75));
        Assert.Equal(200, MotoSyncRunHistoryRepository.NormalizeTake(500));
    }

    [Fact]
    public void List_query_orders_newest_first()
    {
        Assert.Contains(
            "ORDER BY StartedAt DESC, Id DESC",
            MotoSyncRunHistoryRepository.ListOrderBySql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_masks_connection_secret_tokens()
    {
        var sanitized = MotoSyncRunHistoryRepository.Sanitize(
            "Server=.;User ID=sa;Password=secret;Pwd=secret2;UID=user;");

        Assert.DoesNotContain("secret", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sa", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Password=<masked>", sanitized, StringComparison.Ordinal);
        Assert.Contains("Pwd=<masked>", sanitized, StringComparison.Ordinal);
        Assert.Contains("User ID=<masked>", sanitized, StringComparison.Ordinal);
        Assert.Contains("UID=<masked>", sanitized, StringComparison.Ordinal);
    }
}

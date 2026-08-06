namespace QLHV.Infrastructure.Sync.Realtime;

internal static class CsdtRealtimeOptionalDomainRetryPolicy
{
    internal static bool ShouldDefer(
        CsdtRealtimeRuntimeDomain domain,
        DateTimeOffset nowUtc)
        => domain.IsOptional &&
           domain.NextRetryAtUtc.HasValue &&
           domain.NextRetryAtUtc.Value > nowUtc;

    internal static bool IsRetryDue(
        CsdtRealtimeRuntimeDomain domain,
        DateTimeOffset nowUtc)
        => domain.IsOptional &&
           domain.NextRetryAtUtc.HasValue &&
           domain.NextRetryAtUtc.Value <= nowUtc;

    internal static bool HasWorkDue(
        CsdtRealtimeRuntimeDomain domain,
        long currentVersion,
        DateTimeOffset nowUtc)
    {
        if (ShouldDefer(domain, nowUtc))
        {
            return false;
        }

        if (IsRetryDue(domain, nowUtc))
        {
            return true;
        }

        return !domain.LastSuccessfulVersion.HasValue || domain.LastSuccessfulVersion.Value < currentVersion;
    }
}

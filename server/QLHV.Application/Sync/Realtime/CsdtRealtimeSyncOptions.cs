using Microsoft.Extensions.Options;

namespace QLHV.Application.Sync.Realtime;

public sealed class CsdtRealtimeSyncOptions
{
    public const string SectionName = "CsdtRealtimeSync";

    public bool Enabled { get; set; }

    public bool UseBackupProfiles { get; set; }

    /// <summary>
    /// Gates the Task 02 atomic mapped-table foundation. The production stream
    /// processor does not consume this option; callers must explicitly select
    /// the isolated atomic coordinator.
    /// </summary>
    public bool UseAtomicMappedTableCycle { get; set; }

    /// <summary>
    /// Gates the Task 03A membership bootstrap foundation. This is intentionally
    /// disconnected from the production processor and composition root.
    /// </summary>
    public bool EnableMembershipBootstrap { get; set; }

    /// <summary>
    /// Gates immutable full-reconcile planning. Reconcile execution is not
    /// selected by the production processor.
    /// </summary>
    public bool EnableMembershipReconcile { get; set; }

    /// <summary>
    /// Reserved for a separately approved deletion/deactivation implementation.
    /// Task 03A never consumes this flag to mutate a business row.
    /// </summary>
    public bool EnableDeleteExecution { get; set; }

    public int PollIntervalSeconds { get; set; } = 1;

    public int ReconcileIntervalMinutes { get; set; } = 5;

    public int ChangeRetentionDays { get; set; } = 7;

    public CsdtRealtimeStreamsOptions Streams { get; set; } = new();
}

public sealed class CsdtRealtimeStreamsOptions
{
    public CsdtRealtimeStreamOptions Oto { get; set; } = new()
    {
        Enabled = true,
        StreamCode = CsdtRealtimeStreamCodes.OtoV2ToV1,
        SourceProfile = CsdtRealtimeProfileCodes.OtoV2,
        TargetProfile = CsdtRealtimeProfileCodes.OtoV1,
        MaCSDT = "66029",
    };

    public CsdtRealtimeStreamOptions Moto { get; set; } = new()
    {
        Enabled = true,
        StreamCode = CsdtRealtimeStreamCodes.MotoV2ToV1,
        SourceProfile = CsdtRealtimeProfileCodes.MotoV2,
        TargetProfile = CsdtRealtimeProfileCodes.MotoV1,
        MaCSDT = "66030",
    };
}

public sealed class CsdtRealtimeStreamOptions
{
    public bool Enabled { get; set; }

    public string StreamCode { get; set; } = string.Empty;

    public string SourceProfile { get; set; } = string.Empty;

    public string TargetProfile { get; set; } = string.Empty;

    public string MaCSDT { get; set; } = string.Empty;
}

public sealed class CsdtRealtimeSyncOptionsValidator : IValidateOptions<CsdtRealtimeSyncOptions>
{
    public ValidateOptionsResult Validate(string? name, CsdtRealtimeSyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.PollIntervalSeconds is < 1 or > 60)
        {
            failures.Add("CsdtRealtimeSync:PollIntervalSeconds phai nam trong khoang 1..60.");
        }

        if (options.ReconcileIntervalMinutes is < 1 or > 1440)
        {
            failures.Add("CsdtRealtimeSync:ReconcileIntervalMinutes phai nam trong khoang 1..1440.");
        }

        if (options.ChangeRetentionDays is < 1 or > 30)
        {
            failures.Add("CsdtRealtimeSync:ChangeRetentionDays phai nam trong khoang 1..30.");
        }

        if (options.Streams is null)
        {
            failures.Add("CsdtRealtimeSync:Streams la bat buoc.");
        }
        else
        {
            ValidateStream(
                "Oto",
                CsdtRealtimeStreamCodes.OtoV2ToV1,
                options.Streams.Oto,
                options.UseBackupProfiles,
                failures);
            ValidateStream(
                "Moto",
                CsdtRealtimeStreamCodes.MotoV2ToV1,
                options.Streams.Moto,
                options.UseBackupProfiles,
                failures);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateStream(
        string optionName,
        string streamCode,
        CsdtRealtimeStreamOptions? stream,
        bool useBackupProfiles,
        ICollection<string> failures)
    {
        if (stream is null)
        {
            failures.Add($"CsdtRealtimeSync:Streams:{optionName} la bat buoc.");
            return;
        }

        if (!CsdtRealtimeStreamCatalog.TryResolveAllowedRoute(
                streamCode,
                stream.SourceProfile,
                stream.TargetProfile,
                out var route))
        {
            failures.Add(
                $"CsdtRealtimeSync:Streams:{optionName} co cap profile khong nam trong allowlist.");
            return;
        }

        if (!string.Equals(stream.StreamCode, streamCode, StringComparison.Ordinal))
        {
            failures.Add(
                $"CsdtRealtimeSync:Streams:{optionName}:StreamCode phai la {streamCode}.");
        }

        if (route.IsBackup != useBackupProfiles)
        {
            failures.Add(
                useBackupProfiles
                    ? $"CsdtRealtimeSync:Streams:{optionName} phai dung cap profile BAK."
                    : $"CsdtRealtimeSync:Streams:{optionName} phai dung cap profile live.");
        }

        if (!string.Equals(stream.MaCSDT, route.MaCSDT, StringComparison.Ordinal) ||
            !CsdtRealtimeIdentityRules.IsCurrentMaCsdt(stream.MaCSDT))
        {
            failures.Add(
                $"CsdtRealtimeSync:Streams:{optionName}:MaCSDT phai la {route.MaCSDT}.");
        }
    }
}

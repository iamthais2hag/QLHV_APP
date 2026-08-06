using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace QLHV.Application.Sync.Rt03;

public static class Rt03Modes
{
    public const string ObservationOnly = "OBSERVATION_ONLY";
    public const string Canary = "CANARY";
    public const string ControlledCutover = "CONTROLLED_CUTOVER";
}

public static class Rt03Profiles
{
    public const string Oto = "CSDT_OTO";
    public const string Moto = "CSDT_MOTO";

    public static IReadOnlyList<string> Ordered { get; } = [Oto, Moto];
}

public static class Rt03Errors
{
    public const string ProductionIdentityRejected = "RT03_PRODUCTION_IDENTITY_REJECTED";
    public const string FeatureStateRejected = "RT03_FEATURE_STATE_REJECTED";
    public const string AllowlistRejected = "RT03_ALLOWLIST_REJECTED";
    public const string AutoSyncActive = "RT03_AUTOSYNC_ACTIVE";
    public const string SourceDrift = "RT03_SOURCE_DRIFT";
    public const string SourceChangedDuringPlan = "RT03_SOURCE_CHANGED_DURING_PLAN";
    public const string TargetDrift = "RT03_TARGET_DRIFT";
    public const string CheckpointConflict = "RT03_CHECKPOINT_CONFLICT";
    public const string OtoMustPassFirst = "RT03_OTO_MUST_PASS_FIRST";
    public const string RollbackRejected = "RT03_ROLLBACK_REJECTED";
    public const string ConfigurationRejected = "RT03_CONFIGURATION_REJECTED";
    public const string ChangeTrackingWindowRejected = "RT03_CT_WINDOW_REJECTED";
    public const string UnsupportedDrift = "RT03_UNSUPPORTED_DRIFT";
    public const string AmbiguousCourseIdentity = "BLOCKED_AMBIGUOUS_IDENTITY";
    public const string UnclassifiedForwardColumn = "UNCLASSIFIED_FORWARD_COLUMN";
    public const string LearnerCourseNotConvergent = "BLOCKED_LEARNER_COURSE_IDENTITY";
    public const string WorkerAlreadyActive = "RT03_WORKER_ALREADY_ACTIVE";
    public const string TimeAuthorityBlocked = "RT03_TIME_AUTHORITY_BLOCKED";
    public const string OwnershipProofRejected =
        "RT03_FULL_CONVERGENCE_OWNERSHIP_REJECTED";
}

public static class Rt03WorkerFailurePolicy
{
    public static bool IsRetryable(string? code)
        => string.Equals(
            code,
            Rt03Errors.SourceChangedDuringPlan,
            StringComparison.Ordinal);
}

public sealed class Rt03SafetyException : InvalidOperationException
{
    public Rt03SafetyException(string code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>
/// No production host binds this type in RT-03 Task 1. Every activation flag
/// defaults false; validation-only remains true.
/// </summary>
public sealed class Rt03ProductionOptions
{
    public const string SectionName = "Rt03Production";

    public bool EnableRt03ProductionRealtime { get; set; }
    public bool EnableRt03ProductionShadow { get; set; }
    public bool EnableRt03ProductionWrites { get; set; }
    public bool EnableRt03ProductionCanary { get; set; }
    public bool EnableRt03ControlledCutover { get; set; }
    public bool EnableRt03ProductionDeletes { get; set; }
    public bool ValidationOnly { get; set; } = true;
    public bool EnableOto { get; set; }
    public bool EnableMoto { get; set; }
    public int PollIntervalSeconds { get; set; } = 2;
    public string ExpectedMappingFingerprint { get; set; } =
        "7bb2c2fc99cd06a222af2e36c0c61f259a4488ceecad7064c6e308fc223e4ee9";
    public string ExpectedOtoSourceSchemaFingerprint { get; set; } =
        "b406355371fc6ecda51156cf0c10f525288d5fd0a083fc97a6b1c175cc859bb8";
    public string ExpectedMotoSourceSchemaFingerprint { get; set; } =
        "b406355371fc6ecda51156cf0c10f525288d5fd0a083fc97a6b1c175cc859bb8";
    public string ExpectedTargetSchemaFingerprint { get; set; } =
        "21075486fa8c34a59feada9bc82b1b2a8278240e980f97eaadc963aec6c51380";
}

public sealed class Rt03ProductionOptionsValidator : IValidateOptions<Rt03ProductionOptions>
{
    public ValidateOptionsResult Validate(string? name, Rt03ProductionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var subordinateEnabled = options.EnableRt03ProductionShadow ||
                                 options.EnableRt03ProductionWrites ||
                                 options.EnableRt03ProductionCanary ||
                                 options.EnableRt03ControlledCutover ||
                                 options.EnableRt03ProductionDeletes ||
                                 options.EnableOto || options.EnableMoto;

        if (!options.EnableRt03ProductionRealtime && subordinateEnabled)
        {
            failures.Add("RT-03 subordinate flags require the production realtime master flag.");
        }

        if (options.EnableRt03ProductionRealtime)
        {
            if (options.ValidationOnly ||
                !options.EnableRt03ProductionShadow ||
                !options.EnableRt03ProductionWrites ||
                !options.EnableRt03ControlledCutover ||
                options.EnableRt03ProductionCanary ||
                options.EnableRt03ProductionDeletes)
            {
                failures.Add(
                    "RT-03 production registration requires shadow/write/cutover, " +
                    "ValidationOnly=false, canary=false, and delete=false.");
            }

            if (!options.EnableOto)
            {
                failures.Add("RT-03 production registration requires OTO before MOTO.");
            }

            if (options.EnableMoto && !options.EnableOto)
            {
                failures.Add("RT-03 MOTO cannot be configured without OTO.");
            }

            if (options.PollIntervalSeconds is < 1 or > 10)
            {
                failures.Add("RT-03 PollIntervalSeconds must be in the range 1..10.");
            }

            if (new[]
                {
                    options.ExpectedMappingFingerprint,
                    options.ExpectedOtoSourceSchemaFingerprint,
                    options.ExpectedMotoSourceSchemaFingerprint,
                    options.ExpectedTargetSchemaFingerprint,
                }.Any(value => string.IsNullOrWhiteSpace(value) || value.Length != 64))
            {
                failures.Add("RT-03 expected mapping/schema fingerprints must be exact SHA-256 values.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

public sealed record Rt03ProductionFingerprintSnapshot(
    string MappingFingerprint,
    string OtoSourceSchemaFingerprint,
    string MotoSourceSchemaFingerprint,
    string TargetSchemaFingerprint);

public sealed record Rt03SourceCapabilitySnapshot(
    string SourceProfileCode,
    Guid DatabaseGuid,
    long CurrentChangeTrackingVersion,
    long MinimumValidChangeTrackingVersion,
    int TrackedTableCount,
    bool SnapshotIsolationEnabled,
    bool ReadCommittedSnapshotEnabled);

public static class Rt03ProductionStartupGuard
{
    public static void Validate(
        Rt03ProductionOptions options,
        IReadOnlyCollection<Rt03ObservedDatabase> identities,
        Rt03ProductionFingerprintSnapshot fingerprints,
        IReadOnlyCollection<Rt03SourceCapabilitySnapshot> capabilities,
        Rt03AutoSyncExclusionSnapshot autoSync)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(fingerprints);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(autoSync);

        var validation = new Rt03ProductionOptionsValidator().Validate(null, options);
        Reject(validation.Failed, Rt03Errors.ConfigurationRejected,
            validation.FailureMessage ?? "RT-03 production configuration is invalid.");
        Reject(!options.EnableRt03ProductionRealtime,
            Rt03Errors.FeatureStateRejected, "RT-03 production registration is disabled.");
        Rt03ProductionIdentityValidator.Validate(identities);

        Reject(!string.Equals(fingerprints.MappingFingerprint,
                   options.ExpectedMappingFingerprint, StringComparison.Ordinal) ||
               !string.Equals(fingerprints.OtoSourceSchemaFingerprint,
                   options.ExpectedOtoSourceSchemaFingerprint, StringComparison.Ordinal) ||
               !string.Equals(fingerprints.MotoSourceSchemaFingerprint,
                   options.ExpectedMotoSourceSchemaFingerprint, StringComparison.Ordinal) ||
               !string.Equals(fingerprints.TargetSchemaFingerprint,
                   options.ExpectedTargetSchemaFingerprint, StringComparison.Ordinal),
            Rt03Errors.SourceDrift,
            "Production mapping or schema fingerprint does not match registration.");

        var requiredProfiles = options.EnableMoto
            ? Rt03Profiles.Ordered
            : new[] { Rt03Profiles.Oto };
        foreach (var profile in requiredProfiles)
        {
            var capability = capabilities.SingleOrDefault(item =>
                string.Equals(item.SourceProfileCode, profile, StringComparison.Ordinal));
            Reject(capability is null, Rt03Errors.ChangeTrackingWindowRejected,
                $"Missing Change Tracking capability for {profile}.");
            Reject(capability!.CurrentChangeTrackingVersion < 0 ||
                   capability.MinimumValidChangeTrackingVersion < 0 ||
                   capability.MinimumValidChangeTrackingVersion >
                       capability.CurrentChangeTrackingVersion ||
                   capability.TrackedTableCount != 5 ||
                   !capability.SnapshotIsolationEnabled ||
                   capability.ReadCommittedSnapshotEnabled,
                Rt03Errors.ChangeTrackingWindowRejected,
                $"Unsafe Change Tracking/Snapshot/RCSI state for {profile}.");
        }

        Reject(autoSync.ConfigurationEnabled || autoSync.PollingEnabled ||
               autoSync.IsPolling || autoSync.ActiveRunRows != 0 ||
               autoSync.ActiveSlotRows != 0 || autoSync.ActiveOperationRows != 0 ||
               !autoSync.ExistingAutoSyncGlobalLockAcquired ||
               autoSync.ExistingRealtimeRunActive,
            Rt03Errors.AutoSyncActive,
            "Existing Auto Sync must be paused and mutually excluded.");
    }

    private static void Reject(bool condition, string code, string message)
    {
        if (condition)
        {
            throw new Rt03SafetyException(code, message);
        }
    }
}

public sealed record Rt03ExpectedDatabase(
    string Role,
    string DatabaseName,
    int DatabaseId,
    Guid DatabaseGuid);

public sealed record Rt03ObservedDatabase(
    string Role,
    string ServerIdentity,
    string RequestedDatabaseName,
    string ActualDatabaseName,
    int DatabaseId,
    Guid DatabaseGuid,
    string ConnectionRoute,
    bool IsOnline,
    bool IsAlias,
    bool IsLinkedOrExternal);

public static class Rt03ProductionCatalog
{
    public const string ServerIdentity = "CSDLTTTC";
    public const string TargetDatabase = "QLHV_APP";

    public static IReadOnlyList<Rt03ExpectedDatabase> RequiredDatabases { get; } =
    [
        new("SOURCE_OTO", "CSDL_OTO", 9,
            Guid.Parse("9A8B9BC1-18F3-4823-8123-3DC197A9D540")),
        new("SOURCE_MOTO", "CSDL_MOTO", 8,
            Guid.Parse("308BDDA8-80F3-4ACB-9836-578D80A9E98E")),
        new("TARGET", "QLHV_APP", 12,
            Guid.Parse("9C44B304-8A84-4D0D-9A82-19C7233FF6BB")),
    ];

    public static IReadOnlySet<string> DeniedDatabases { get; } =
        new HashSet<string>(
            [
                "CSDL_OTO_BAK",
                "CSDL_MOTO_BAK",
                "CSDL_OTO_V1",
                "CSDL_MOTO_V1",
            ],
            StringComparer.OrdinalIgnoreCase);
}

public static class Rt03ProductionIdentityValidator
{
    public static void Validate(IReadOnlyCollection<Rt03ObservedDatabase> observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        Reject(observed.Count != Rt03ProductionCatalog.RequiredDatabases.Count,
            "Exactly the OTO source, MOTO source and QLHV_APP target are required.");

        foreach (var expected in Rt03ProductionCatalog.RequiredDatabases)
        {
            var identity = observed.SingleOrDefault(candidate =>
                string.Equals(candidate.Role, expected.Role, StringComparison.Ordinal));
            Reject(identity is null, $"Missing identity for {expected.Role}.");
            Reject(!string.Equals(identity!.ServerIdentity,
                    Rt03ProductionCatalog.ServerIdentity, StringComparison.Ordinal),
                $"Unexpected server for {expected.Role}.");
            Reject(!string.Equals(identity.RequestedDatabaseName,
                    expected.DatabaseName, StringComparison.Ordinal) ||
                   !string.Equals(identity.ActualDatabaseName,
                    expected.DatabaseName, StringComparison.Ordinal),
                $"Database name mismatch for {expected.Role}.");
            Reject(identity.DatabaseId != expected.DatabaseId ||
                   identity.DatabaseGuid != expected.DatabaseGuid,
                $"Database ID/GUID mismatch for {expected.Role}.");
            Reject(!identity.IsOnline || identity.IsAlias || identity.IsLinkedOrExternal,
                $"Unsafe connection topology for {expected.Role}.");
            Reject(Rt03ProductionCatalog.DeniedDatabases.Any(denied =>
                    identity.ConnectionRoute.Contains(denied, StringComparison.OrdinalIgnoreCase)),
                $"Denied BAK/V1 route in {expected.Role} connection.");
        }

        Reject(observed.Select(item => item.DatabaseId).Distinct().Count() != observed.Count ||
               observed.Select(item => item.DatabaseGuid).Distinct().Count() != observed.Count,
            "Production source and target identities must be distinct.");
    }

    private static void Reject(bool condition, string message)
    {
        if (condition)
        {
            throw new Rt03SafetyException(Rt03Errors.ProductionIdentityRejected, message);
        }
    }
}

public enum Rt03CandidateKind
{
    Insert,
    UpdateSourceOwnedFields,
    RetainForManualReview,
}

public sealed record Rt03CanaryCandidate(
    string CandidateId,
    string SourceProfile,
    Rt03CandidateKind Kind,
    string IdentityHmac,
    string Classification,
    string BeforeSourceOwnedHash,
    string BeforeQlhvOwnedHash,
    string ExpectedMutation,
    string ExpectedInvariant,
    string RollbackImageHash,
    IReadOnlyList<string> RequestedFields,
    IReadOnlyList<string> ExclusionReasons);

public sealed record Rt03CanaryPlan(
    string PlanId,
    string Mode,
    string EnvironmentId,
    string MappingFingerprint,
    string OtoSourceSchemaFingerprint,
    string MotoSourceSchemaFingerprint,
    string TargetSchemaFingerprint,
    string OtoStageHash,
    string MotoStageHash,
    string OtoTargetComparisonHash,
    string MotoTargetComparisonHash,
    long? OtoInitialChangeTrackingVersion,
    long? MotoInitialChangeTrackingVersion,
    string OtoCanaryResult,
    IReadOnlyList<Rt03CanaryCandidate> Candidates)
{
    public string PlanHash => Rt03Hash.Sha256(string.Join("|",
        PlanId,
        Mode,
        EnvironmentId,
        MappingFingerprint,
        OtoSourceSchemaFingerprint,
        MotoSourceSchemaFingerprint,
        TargetSchemaFingerprint,
        OtoStageHash,
        MotoStageHash,
        OtoTargetComparisonHash,
        MotoTargetComparisonHash,
        OtoInitialChangeTrackingVersion?.ToString() ?? "<NULL>",
        MotoInitialChangeTrackingVersion?.ToString() ?? "<NULL>",
        OtoCanaryResult,
        string.Join(",", Candidates.Select(candidate => candidate.CandidateId))));
}

public static class Rt03CanaryPlanValidator
{
    private const string IdentityHmacPrefix = "RT03-HMAC-SHA256-v1:";

    public static void Validate(Rt03CanaryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Reject(string.IsNullOrWhiteSpace(plan.PlanId) || ContainsWildcard(plan.PlanId) ||
               string.IsNullOrWhiteSpace(plan.EnvironmentId) ||
               string.IsNullOrWhiteSpace(plan.MappingFingerprint) ||
               string.IsNullOrWhiteSpace(plan.OtoSourceSchemaFingerprint) ||
               string.IsNullOrWhiteSpace(plan.MotoSourceSchemaFingerprint) ||
               string.IsNullOrWhiteSpace(plan.TargetSchemaFingerprint) ||
               string.IsNullOrWhiteSpace(plan.OtoStageHash) ||
               string.IsNullOrWhiteSpace(plan.MotoStageHash) ||
               string.IsNullOrWhiteSpace(plan.OtoTargetComparisonHash) ||
               string.IsNullOrWhiteSpace(plan.MotoTargetComparisonHash),
            "The immutable plan fingerprint set is incomplete or contains a wildcard.");
        Reject(plan.Candidates.Select(item => item.CandidateId)
                .Distinct(StringComparer.Ordinal).Count() != plan.Candidates.Count,
            "Candidate IDs must be unique.");
        Reject(plan.Candidates.Any(candidate =>
                !Rt03Profiles.Ordered.Contains(candidate.SourceProfile)),
            "Only exact CSDT_OTO/CSDT_MOTO profiles are allowed.");

        if (string.Equals(plan.Mode, Rt03Modes.ObservationOnly, StringComparison.Ordinal))
        {
            Reject(plan.Candidates.Count != 0,
                "Observation-only plans must have an empty mutation allowlist.");
            Reject(plan.OtoInitialChangeTrackingVersion is not null ||
                   plan.MotoInitialChangeTrackingVersion is not null,
                "Observation-only before CT initialization must not invent checkpoints.");
            return;
        }

        Reject(!string.Equals(plan.Mode, Rt03Modes.Canary, StringComparison.Ordinal),
            "Only OBSERVATION_ONLY or CANARY is valid for the Task 2 entry plan.");
        Reject(plan.Candidates.Count == 0, "A mutation canary cannot have an empty allowlist.");

        var oto = plan.Candidates.Where(candidate => candidate.SourceProfile == Rt03Profiles.Oto)
            .ToArray();
        var moto = plan.Candidates.Where(candidate => candidate.SourceProfile == Rt03Profiles.Moto)
            .ToArray();
        Reject(oto.Count(candidate => candidate.Kind == Rt03CandidateKind.Insert) > 1 ||
               oto.Count(candidate => candidate.Kind == Rt03CandidateKind.UpdateSourceOwnedFields) > 1 ||
               oto.Count(candidate => candidate.Kind == Rt03CandidateKind.RetainForManualReview) > 1,
            "OTO canary exceeds the 1 insert / 1 update / 1 retain maximum.");
        Reject(moto.Length > 0 &&
               !string.Equals(plan.OtoCanaryResult, "PASSED", StringComparison.Ordinal),
            "MOTO candidates are forbidden until OTO has passed and been verified.",
            Rt03Errors.OtoMustPassFirst);

        foreach (var candidate in plan.Candidates)
        {
            Reject(string.IsNullOrWhiteSpace(candidate.CandidateId) ||
                   ContainsWildcard(candidate.CandidateId) ||
                   !candidate.IdentityHmac.StartsWith(
                       IdentityHmacPrefix, StringComparison.Ordinal) ||
                   candidate.IdentityHmac.Length != IdentityHmacPrefix.Length + 64 ||
                   string.IsNullOrWhiteSpace(candidate.BeforeSourceOwnedHash) ||
                   string.IsNullOrWhiteSpace(candidate.BeforeQlhvOwnedHash) ||
                   string.IsNullOrWhiteSpace(candidate.RollbackImageHash) ||
                   string.IsNullOrWhiteSpace(candidate.ExpectedMutation) ||
                   string.IsNullOrWhiteSpace(candidate.ExpectedInvariant),
                $"Candidate {candidate.CandidateId} is incomplete or not privacy-safe.");
            Reject(candidate.ExclusionReasons.Count != 0,
                $"Candidate {candidate.CandidateId} has an exclusion reason.");

            switch (candidate.Kind)
            {
                case Rt03CandidateKind.Insert:
                    Reject(candidate.Classification != "SOURCE_ONLY_NEW_ROW" ||
                           candidate.RequestedFields.Count != 0,
                        "Insert canary must be one source-only row with fixed mapped columns.");
                    break;
                case Rt03CandidateKind.UpdateSourceOwnedFields:
                    Reject(candidate.Classification != "STALE_IMPORTED_VALUE" ||
                           candidate.RequestedFields.Count != 1 ||
                           !string.Equals(candidate.RequestedFields[0], "HoTen",
                               StringComparison.Ordinal),
                        "Only the source-owned HoTen update is canary-eligible.");
                    break;
                case Rt03CandidateKind.RetainForManualReview:
                    Reject(candidate.Classification != "SOURCE_ROW_REMOVED" ||
                           candidate.RequestedFields.Count != 0 ||
                           candidate.ExpectedMutation != "NONE",
                        "Target-only canary evidence must retain active without mutation.");
                    break;
                default:
                    Reject(true, "Unknown candidate kind.");
                    break;
            }
        }
    }

    private static bool ContainsWildcard(string value)
        => value.Contains('*') ||
           value.Contains('%') ||
           value.Contains('[') ||
           value.Contains(']');

    private static void Reject(bool condition, string message, string? code = null)
    {
        if (condition)
        {
            throw new Rt03SafetyException(code ?? Rt03Errors.AllowlistRejected, message);
        }
    }
}

public sealed record Rt03AutoSyncExclusionSnapshot(
    bool ConfigurationEnabled,
    bool PollingEnabled,
    bool IsPolling,
    long ActiveRunRows,
    long ActiveSlotRows,
    long ActiveOperationRows,
    bool ExistingAutoSyncGlobalLockAcquired,
    bool ExistingRealtimeRunActive);

public sealed record Rt03RevalidationSnapshot(
    string MappingFingerprint,
    string OtoSourceSchemaFingerprint,
    string MotoSourceSchemaFingerprint,
    string TargetSchemaFingerprint,
    string OtoStageHash,
    string MotoStageHash,
    string OtoTargetComparisonHash,
    string MotoTargetComparisonHash,
    bool DuplicateActiveTarget,
    bool QlhvOwnedFieldDrift,
    bool UnexpectedDeleteOrDeactivation);

public sealed record Rt03CheckpointState(
    bool Exists,
    string? CycleId,
    string? PlanHash,
    string? MarkerHash,
    long? SourceVersion,
    long? ExpectedVersion);

public static class Rt03ExecutionGate
{
    public static void ValidateTask1DisabledState(
        Rt03ProductionOptions options,
        int productionWriterRegistrationCount)
    {
        ArgumentNullException.ThrowIfNull(options);
        Reject(!options.ValidationOnly ||
               options.EnableRt03ProductionRealtime ||
               options.EnableRt03ProductionShadow ||
               options.EnableRt03ProductionWrites ||
               options.EnableRt03ProductionCanary ||
               options.EnableRt03ControlledCutover ||
               options.EnableRt03ProductionDeletes ||
               productionWriterRegistrationCount != 0,
            Rt03Errors.FeatureStateRejected,
            "Task 1 requires all RT-03 production flags false and no writer registration.");
    }

    public static void ValidateMutationCanary(
        Rt03ProductionOptions options,
        Rt03CanaryPlan plan,
        Rt03AutoSyncExclusionSnapshot autoSync,
        Rt03RevalidationSnapshot current,
        Rt03CheckpointState checkpoint)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(autoSync);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(checkpoint);
        Rt03CanaryPlanValidator.Validate(plan);

        Reject(options.ValidationOnly ||
               !options.EnableRt03ProductionRealtime ||
               !options.EnableRt03ProductionShadow ||
               !options.EnableRt03ProductionWrites ||
               !options.EnableRt03ProductionCanary ||
               options.EnableRt03ProductionDeletes,
            Rt03Errors.FeatureStateRejected,
            "Canary requires explicit master/shadow/write/canary flags and delete=false.");
        Reject(autoSync.ConfigurationEnabled || autoSync.PollingEnabled ||
               autoSync.IsPolling || autoSync.ActiveRunRows != 0 ||
               autoSync.ActiveSlotRows != 0 || autoSync.ActiveOperationRows != 0 ||
               !autoSync.ExistingAutoSyncGlobalLockAcquired ||
               autoSync.ExistingRealtimeRunActive,
            Rt03Errors.AutoSyncActive,
            "Existing Auto Sync/realtime must be stopped and its global lock held.");

        Reject(!string.Equals(current.MappingFingerprint, plan.MappingFingerprint,
                   StringComparison.Ordinal) ||
               !string.Equals(current.OtoSourceSchemaFingerprint,
                   plan.OtoSourceSchemaFingerprint, StringComparison.Ordinal) ||
               !string.Equals(current.MotoSourceSchemaFingerprint,
                   plan.MotoSourceSchemaFingerprint, StringComparison.Ordinal) ||
               !string.Equals(current.TargetSchemaFingerprint,
                   plan.TargetSchemaFingerprint, StringComparison.Ordinal),
            Rt03Errors.SourceDrift,
            "Mapping or schema fingerprint changed after plan sealing.");
        Reject(!string.Equals(current.OtoStageHash, plan.OtoStageHash,
                   StringComparison.Ordinal) ||
               !string.Equals(current.MotoStageHash, plan.MotoStageHash,
                   StringComparison.Ordinal),
            Rt03Errors.SourceDrift,
            "Source stage changed after plan sealing.");
        Reject(!string.Equals(current.OtoTargetComparisonHash,
                   plan.OtoTargetComparisonHash, StringComparison.Ordinal) ||
               !string.Equals(current.MotoTargetComparisonHash,
                   plan.MotoTargetComparisonHash, StringComparison.Ordinal) ||
               current.DuplicateActiveTarget || current.QlhvOwnedFieldDrift ||
               current.UnexpectedDeleteOrDeactivation,
            Rt03Errors.TargetDrift,
            "Target/QLHV-owned state changed or violated an invariant.");
        Reject(checkpoint.Exists &&
               (!string.Equals(checkpoint.PlanHash, plan.PlanHash, StringComparison.Ordinal) ||
                checkpoint.SourceVersion != checkpoint.ExpectedVersion),
            Rt03Errors.CheckpointConflict,
            "Checkpoint content/version conflicts with the immutable plan.");
    }

    private static void Reject(bool condition, string code, string message)
    {
        if (condition)
        {
            throw new Rt03SafetyException(code, message);
        }
    }
}

public enum Rt03RollbackKind
{
    DeleteExactCanaryInsert,
    RestoreExactSourceOwnedFields,
    NoMutationManualReview,
}

public sealed record Rt03RollbackAction(
    string CandidateId,
    string SourceProfile,
    Rt03RollbackKind Kind,
    string IdentityHmac,
    string LockedBeforeImageHash,
    string ExpectedCurrentSourceOwnedHash,
    string ExpectedCurrentQlhvOwnedHash,
    IReadOnlyList<string> RestoredFields,
    long DownstreamReferenceCount,
    bool TargetStillInExactAllowlist);

public static class Rt03RollbackValidator
{
    public static void Validate(
        Rt03CanaryPlan plan,
        IReadOnlyCollection<Rt03RollbackAction> actions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(actions);
        Rt03CanaryPlanValidator.Validate(plan);
        var candidates = plan.Candidates.ToDictionary(item => item.CandidateId,
            StringComparer.Ordinal);

        Reject(actions.Count != candidates.Count,
            "Rollback must cover the exact sealed canary allowlist.");
        foreach (var action in actions)
        {
            if (!candidates.TryGetValue(action.CandidateId, out var candidate))
            {
                throw new Rt03SafetyException(
                    Rt03Errors.RollbackRejected,
                    $"Rollback candidate {action.CandidateId} is outside the allowlist.");
            }

            Reject(!action.TargetStillInExactAllowlist ||
                   !string.Equals(action.SourceProfile, candidate.SourceProfile,
                       StringComparison.Ordinal) ||
                   !string.Equals(action.IdentityHmac, candidate.IdentityHmac,
                       StringComparison.Ordinal) ||
                   !string.Equals(action.LockedBeforeImageHash,
                       candidate.RollbackImageHash, StringComparison.Ordinal) ||
                   string.IsNullOrWhiteSpace(action.ExpectedCurrentSourceOwnedHash) ||
                   string.IsNullOrWhiteSpace(action.ExpectedCurrentQlhvOwnedHash),
                $"Rollback revalidation failed for {action.CandidateId}.");

            switch (candidate.Kind)
            {
                case Rt03CandidateKind.Insert:
                    Reject(action.Kind != Rt03RollbackKind.DeleteExactCanaryInsert ||
                           action.RestoredFields.Count != 0 ||
                           action.DownstreamReferenceCount != 0,
                        "Inserted row rollback requires exact row and zero downstream use.");
                    break;
                case Rt03CandidateKind.UpdateSourceOwnedFields:
                    Reject(action.Kind != Rt03RollbackKind.RestoreExactSourceOwnedFields ||
                           action.RestoredFields.Count != 1 ||
                           action.RestoredFields[0] != "HoTen",
                        "Update rollback may restore only the locked HoTen before-image.");
                    break;
                case Rt03CandidateKind.RetainForManualReview:
                    Reject(action.Kind != Rt03RollbackKind.NoMutationManualReview ||
                           action.RestoredFields.Count != 0,
                        "Retained target-only evidence has no learner mutation to reverse.");
                    break;
            }
        }
    }

    private static void Reject(bool condition, string message)
    {
        if (condition)
        {
            throw new Rt03SafetyException(Rt03Errors.RollbackRejected, message);
        }
    }
}

public enum Rt03RecoveryDisposition
{
    StopWithoutMutation,
    RollbackOpenTransaction,
    VerifyMarkerThenPublishCheckpoint,
    AlreadyCompleted,
    BlockAsAmbiguous,
}

public sealed record Rt03RecoverySnapshot(
    bool TransactionStarted,
    bool CommitConfirmed,
    bool CommitAmbiguous,
    bool MarkerExists,
    bool MarkerMatchesPlan,
    bool IntegrityVerified,
    bool CheckpointExists,
    bool CheckpointMatchesMarker);

public static class Rt03RecoveryPlanner
{
    public static Rt03RecoveryDisposition Decide(Rt03RecoverySnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.CommitAmbiguous)
        {
            return Rt03RecoveryDisposition.BlockAsAmbiguous;
        }

        if (!state.TransactionStarted)
        {
            return state.MarkerExists || state.CheckpointExists
                ? Rt03RecoveryDisposition.BlockAsAmbiguous
                : Rt03RecoveryDisposition.StopWithoutMutation;
        }

        if (!state.CommitConfirmed)
        {
            return state.MarkerExists || state.CheckpointExists
                ? Rt03RecoveryDisposition.BlockAsAmbiguous
                : Rt03RecoveryDisposition.RollbackOpenTransaction;
        }

        if (!state.MarkerExists || !state.MarkerMatchesPlan ||
            !state.IntegrityVerified)
        {
            return Rt03RecoveryDisposition.BlockAsAmbiguous;
        }

        if (!state.CheckpointExists)
        {
            return Rt03RecoveryDisposition.VerifyMarkerThenPublishCheckpoint;
        }

        return state.CheckpointMatchesMarker
            ? Rt03RecoveryDisposition.AlreadyCompleted
            : Rt03RecoveryDisposition.BlockAsAmbiguous;
    }
}

public static class Rt03Hash
{
    public static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string DiagnosticHmac(string secret, string purpose, string value)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(purpose))
        {
            throw new ArgumentException("A non-empty RT-03 HMAC secret and purpose are required.");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "RT03-HMAC-SHA256-v1:" + Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes($"RT03-HMAC-SHA256-v1|{purpose}|{value}")))
            .ToLowerInvariant();
    }
}

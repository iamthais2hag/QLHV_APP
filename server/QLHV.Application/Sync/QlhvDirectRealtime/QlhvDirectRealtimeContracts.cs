using System.Security.Cryptography;
using System.Text;

namespace QLHV.Application.Sync.QlhvDirectRealtime;

public static class QlhvDirectRealtimeModes
{
    public const string DirectRealtimeApply = "DIRECT_REALTIME_APPLY";
}

public static class QlhvDirectRealtimeDispositions
{
    public const string WouldInsertSafeAfterApproval = "WOULD_INSERT_SAFE_AFTER_APPROVAL";
    public const string StaleImportedValue = "STALE_IMPORTED_VALUE";
    public const string ManualReviewRequired = "MANUAL_REVIEW_REQUIRED";
}

public static class QlhvDirectRealtimeErrors
{
    public const string IsolatedDatabaseIdentityRejected =
        "ISOLATED_DATABASE_IDENTITY_REJECTED";
    public const string TargetChangedSinceShadow = "TARGET_CHANGED_SINCE_SHADOW";
    public const string SourceChangedSinceShadow = "SOURCE_CHANGED_SINCE_SHADOW";
    public const string PlanFingerprintConflict = "PLAN_FINGERPRINT_CONFLICT";
    public const string CheckpointConflict = "CHECKPOINT_CONFLICT";
    public const string FeatureDisabled = "DIRECT_REALTIME_FEATURE_DISABLED";
}

/// <summary>
/// All switches are fail-closed. This type is intentionally not bound or
/// registered by either production composition root in RT-02A.
/// </summary>
public sealed class QlhvDirectRealtimeOptions
{
    public bool EnableQlhvDirectRealtime { get; set; }

    public bool EnableQlhvDirectRealtimeShadow { get; set; }

    public bool EnableQlhvDirectRealtimeWrites { get; set; }

    public bool EnableQlhvDirectRealtimeDeletes { get; set; }

    public bool EnableQlhvDirectRealtimeIsolatedApply { get; set; }
}

public sealed record QlhvDirectRealtimeIsolatedEnvironment(
    string IsolatedSourceOtoDatabase,
    string IsolatedSourceMotoDatabase,
    string IsolatedTargetDatabase,
    string SqlServerInstance,
    string EnvironmentId,
    string DatasetFingerprint,
    string SourceCopyProvenance,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    string OwnerApprovalId);

public sealed record QlhvDirectRealtimeDatabaseIdentity(
    string Role,
    string RequestedDatabaseName,
    string ActualDatabaseName,
    int DatabaseId,
    string ServerIdentity,
    Guid DatabaseGuid,
    bool IsReadWrite,
    string RecoveryModel,
    string ConnectionRoute,
    string EnvironmentMarker,
    bool IsAliasOfProduction,
    bool MatchesProductionIdentity);

public sealed class QlhvDirectRealtimeSafetyException : InvalidOperationException
{
    public QlhvDirectRealtimeSafetyException(string code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
    }

    public string Code { get; }
}

public static class QlhvDirectRealtimeIsolatedEnvironmentValidator
{
    public static IReadOnlySet<string> ProductionDatabaseNames { get; } =
        new HashSet<string>(
            [
                "CSDL_OTO",
                "CSDL_MOTO",
                "CSDL_OTO_BAK",
                "CSDL_MOTO_BAK",
                "QLHV_APP",
                "CSDL_OTO_V1",
                "CSDL_MOTO_V1",
            ],
            StringComparer.OrdinalIgnoreCase);

    public static void Validate(
        QlhvDirectRealtimeIsolatedEnvironment environment,
        IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(identities);

        var names = new[]
        {
            environment.IsolatedSourceOtoDatabase,
            environment.IsolatedSourceMotoDatabase,
            environment.IsolatedTargetDatabase,
        };

        Reject(
            names.Any(string.IsNullOrWhiteSpace),
            "An exact isolated database name is missing.");
        Reject(
            names.Any(ProductionDatabaseNames.Contains),
            "A requested database name is a production database name.");
        Reject(
            names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length,
            "Source and target database identities must be distinct.");
        Reject(
            string.IsNullOrWhiteSpace(environment.SqlServerInstance) ||
            string.IsNullOrWhiteSpace(environment.EnvironmentId) ||
            string.IsNullOrWhiteSpace(environment.DatasetFingerprint) ||
            string.IsNullOrWhiteSpace(environment.SourceCopyProvenance) ||
            string.IsNullOrWhiteSpace(environment.OwnerApprovalId),
            "The isolated environment approval contract is incomplete.");
        Reject(
            environment.CreatedAtUtc > nowUtc ||
            environment.ExpiresAtUtc <= nowUtc ||
            environment.ExpiresAtUtc <= environment.CreatedAtUtc,
            "The isolated environment approval window is invalid or expired.");
        Reject(
            identities.Count != 3,
            "Exactly three observed database identities are required.");

        var observedIds = new HashSet<int>();
        var observedGuids = new HashSet<Guid>();
        foreach (var requestedName in names)
        {
            var identity = identities.SingleOrDefault(candidate =>
                string.Equals(
                    candidate.RequestedDatabaseName,
                    requestedName,
                    StringComparison.OrdinalIgnoreCase));
            Reject(identity is null, $"Identity preflight is missing for {requestedName}.");

            Reject(
                !string.Equals(
                    identity!.ActualDatabaseName,
                    requestedName,
                    StringComparison.Ordinal),
                $"DB_NAME() did not exactly match {requestedName}.");
            Reject(
                ProductionDatabaseNames.Contains(identity.ActualDatabaseName) ||
                identity.IsAliasOfProduction ||
                identity.MatchesProductionIdentity,
                $"Database identity for {requestedName} resolves to production.");
            Reject(
                !string.Equals(
                    identity.ServerIdentity,
                    environment.SqlServerInstance,
                    StringComparison.Ordinal),
                $"Server identity did not match for {requestedName}.");
            Reject(
                !string.Equals(
                    identity.EnvironmentMarker,
                    environment.EnvironmentId,
                    StringComparison.Ordinal),
                $"Approved TEST marker did not match for {requestedName}.");
            Reject(
                identity.DatabaseId <= 0 ||
                identity.DatabaseGuid == Guid.Empty ||
                !identity.IsReadWrite ||
                string.IsNullOrWhiteSpace(identity.RecoveryModel) ||
                string.IsNullOrWhiteSpace(identity.ConnectionRoute),
                $"Database metadata was incomplete or not read/write for {requestedName}.");
            Reject(
                !observedIds.Add(identity.DatabaseId) ||
                !observedGuids.Add(identity.DatabaseGuid),
                "Observed database IDs and GUIDs must be unique.");
        }
    }

    private static void Reject(bool condition, string message)
    {
        if (condition)
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.IsolatedDatabaseIdentityRejected,
                message);
        }
    }
}

public enum QlhvDirectRealtimeApplyOperationKind
{
    Insert,
    Update,
    RetainForManualReview,
}

public sealed record QlhvDirectRealtimeApplyOperation(
    string OperationId,
    QlhvDirectRealtimeApplyOperationKind Kind,
    string Disposition,
    string IdentityHmac,
    string SourceRowHash,
    string StagedTargetMappedHash,
    string StagedQlhvOwnedHash,
    IReadOnlyList<string> RequestedColumns,
    string? DesiredHoTen = null);

public sealed record QlhvDirectRealtimeApplyPlan(
    string CycleId,
    string EnvironmentId,
    string SourceProfile,
    string MappingFingerprint,
    string SourceSchemaFingerprint,
    string TargetSchemaFingerprint,
    long SourceWatermark,
    string IdentityNormalizationVersion,
    string StageHash,
    string ComparisonHash,
    string DispositionHash,
    IReadOnlyList<QlhvDirectRealtimeApplyOperation> Operations)
{
    public string PlanHash => QlhvDirectRealtimeHash.Sha256(
        string.Join(
            "|",
            CycleId,
            EnvironmentId,
            SourceProfile,
            MappingFingerprint,
            SourceSchemaFingerprint,
            TargetSchemaFingerprint,
            SourceWatermark,
            IdentityNormalizationVersion,
            StageHash,
            ComparisonHash,
            DispositionHash,
            string.Join(",", Operations.Select(operation => operation.OperationId))));
}

public sealed record QlhvDirectRealtimeApplyCheckpointKey(
    string SourceProfile,
    string Mode,
    string MappingFingerprint,
    string EnvironmentId);

public sealed record QlhvDirectRealtimeApplyCheckpoint(
    QlhvDirectRealtimeApplyCheckpointKey Key,
    string CycleId,
    string PlanHash,
    string MarkerHash,
    long SourceWatermark,
    DateTime PublishedAtUtc);

public sealed record QlhvDirectRealtimeManualReviewEvidence(
    string CycleId,
    string OperationId,
    string IdentityHmac,
    string Disposition,
    string DispositionHash,
    bool TargetRetainedActive,
    bool TargetMutated);

public sealed record QlhvDirectRealtimeApplyMarker(
    string CycleId,
    string PlanHash,
    string DispositionHash,
    int InsertedRows,
    int UpdatedRows,
    int RetainedRows,
    string PreservedQlhvOwnedHash,
    DateTime CommittedAtUtc)
{
    public string MarkerHash => QlhvDirectRealtimeHash.Sha256(
        $"{CycleId}|{PlanHash}|{DispositionHash}|{InsertedRows}|" +
        $"{UpdatedRows}|{RetainedRows}|{PreservedQlhvOwnedHash}");
}

public sealed record QlhvDirectRealtimeApplyResult(
    string CycleId,
    string Status,
    int InsertedRows,
    int UpdatedRows,
    int RetainedRows,
    bool TransactionCommitted,
    bool CheckpointPublished,
    bool RecoveredFromDurableMarker,
    string MarkerHash);

public sealed record QlhvDirectRealtimeTargetVerification(
    int InsertedRows,
    int UpdatedRows,
    int RetainedRows,
    string PreservedQlhvOwnedHash);

public interface IQlhvDirectRealtimeTargetTransaction : IAsyncDisposable
{
    Task RevalidateIsolatedTargetIdentityAsync(
        QlhvDirectRealtimeIsolatedEnvironment environment,
        CancellationToken cancellationToken);

    Task AcquireSourceProfileLockAsync(
        string lockName,
        CancellationToken cancellationToken);

    Task VerifyPlanFingerprintsAsync(
        QlhvDirectRealtimeApplyPlan plan,
        CancellationToken cancellationToken);

    Task InsertAsync(
        QlhvDirectRealtimeApplyOperation operation,
        CancellationToken cancellationToken);

    Task UpdateSourceOwnedFieldsAsync(
        QlhvDirectRealtimeApplyOperation operation,
        CancellationToken cancellationToken);

    Task RetainAndRecordManualReviewAsync(
        QlhvDirectRealtimeManualReviewEvidence evidence,
        CancellationToken cancellationToken);

    Task<QlhvDirectRealtimeTargetVerification> VerifyAsync(
        QlhvDirectRealtimeApplyPlan plan,
        CancellationToken cancellationToken);

    Task WriteApplyMarkerAsync(
        QlhvDirectRealtimeApplyMarker marker,
        CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}

public interface IQlhvDirectRealtimeTargetTransactionFactory
{
    Task<IQlhvDirectRealtimeTargetTransaction> OpenAsync(
        CancellationToken cancellationToken);

    Task<QlhvDirectRealtimeApplyMarker?> FindCommittedMarkerAsync(
        string cycleId,
        CancellationToken cancellationToken);
}

public interface IQlhvDirectRealtimeApplyCheckpointStore
{
    Task<QlhvDirectRealtimeApplyCheckpoint?> ReadAsync(
        QlhvDirectRealtimeApplyCheckpointKey key,
        CancellationToken cancellationToken);

    Task PublishAsync(
        QlhvDirectRealtimeApplyCheckpoint checkpoint,
        CancellationToken cancellationToken);
}

public interface IQlhvDirectRealtimeFaultInjector
{
    Task AfterTargetCommitAsync(
        QlhvDirectRealtimeApplyMarker marker,
        CancellationToken cancellationToken);
}

public sealed class QlhvDirectRealtimeNoFaultInjector :
    IQlhvDirectRealtimeFaultInjector
{
    public Task AfterTargetCommitAsync(
        QlhvDirectRealtimeApplyMarker marker,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public static class QlhvDirectRealtimeHash
{
    public static string Sha256(string value)
        => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    public static string KeyedDiagnosticHmac(
        string secret,
        string purpose,
        string version,
        string value)
    {
        if (string.IsNullOrWhiteSpace(secret) ||
            string.IsNullOrWhiteSpace(purpose) ||
            string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("HMAC secret, purpose and version are required.");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(
            hmac.ComputeHash(
                Encoding.UTF8.GetBytes($"{purpose}|{version}|{value}")));
    }
}

public static class QlhvDirectRealtimeFixtureContract
{
    public const int OtoNoChangeRows = 150;
    public const int OtoInsertCandidates = 1;
    public const int OtoHoTenUpdateCandidates = 1;
    public const int OtoTargetOnlyRetainedRows = 1;
    public const int OtoExistingSoftDeletedRows = 3;
    public const int MotoNoChangeRows = 5;
    public const int DuplicateActiveIdentityGroups = 0;
}

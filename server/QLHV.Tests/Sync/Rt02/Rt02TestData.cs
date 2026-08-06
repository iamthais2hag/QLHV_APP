using QLHV.Application.Sync.QlhvDirectRealtime;

namespace QLHV.Tests.Sync.Rt02;

internal static class Rt02TestData
{
    public const string EnvironmentId = "RT02-ISOLATED-ENV-A7F9";
    public const string ServerIdentity = "RT02-SQL-ISOLATED";
    public const string OtoDatabase = "RT02_OTO_A7F9";
    public const string MotoDatabase = "RT02_MOTO_A7F9";
    public const string TargetDatabase = "RT02_QLHV_A7F9";
    public const string SourceProfile = "CSDT_OTO";
    public const string InsertIdentity = "HMAC-INSERT-0001";
    public const string UpdateIdentity = "HMAC-UPDATE-0001";
    public const string RetainIdentity = "HMAC-RETAIN-0001";
    public const string InsertSourceHash = "SOURCE-HASH-INSERT";
    public const string UpdateSourceHash = "SOURCE-HASH-UPDATE";
    public const string OldMappedHash = "TARGET-MAPPED-HASH-OLD";
    public const string QlhvOwnedHash = "QLHV-OWNED-HASH-STABLE";

    public static QlhvDirectRealtimeIsolatedEnvironment Environment(
        string? otoDatabase = null,
        string? motoDatabase = null,
        string? targetDatabase = null,
        string? serverIdentity = null,
        string? environmentId = null)
        => new(
            otoDatabase ?? OtoDatabase,
            motoDatabase ?? MotoDatabase,
            targetDatabase ?? TargetDatabase,
            serverIdentity ?? ServerIdentity,
            environmentId ?? EnvironmentId,
            "DATASET-SHA256-A7F9",
            "approved-synthetic-deterministic-fixture",
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow.AddDays(1),
            "OWNER-APPROVAL-RT02B-PENDING");

    public static IReadOnlyList<QlhvDirectRealtimeDatabaseIdentity> Identities(
        QlhvDirectRealtimeIsolatedEnvironment? environment = null)
    {
        environment ??= Environment();
        return
        [
            Identity(
                "OTO",
                environment.IsolatedSourceOtoDatabase,
                environment.SqlServerInstance,
                environment.EnvironmentId,
                databaseId: 101),
            Identity(
                "MOTO",
                environment.IsolatedSourceMotoDatabase,
                environment.SqlServerInstance,
                environment.EnvironmentId,
                databaseId: 102),
            Identity(
                "TARGET",
                environment.IsolatedTargetDatabase,
                environment.SqlServerInstance,
                environment.EnvironmentId,
                databaseId: 103),
        ];
    }

    public static QlhvDirectRealtimeDatabaseIdentity Identity(
        string role,
        string databaseName,
        string? serverIdentity = null,
        string? environmentMarker = null,
        int databaseId = 101,
        bool isAliasOfProduction = false,
        bool matchesProductionIdentity = false)
        => new(
            role,
            databaseName,
            databaseName,
            databaseId,
            serverIdentity ?? ServerIdentity,
            GuidFrom(databaseId),
            IsReadWrite: true,
            RecoveryModel: "SIMPLE",
            ConnectionRoute: $"Server={serverIdentity ?? ServerIdentity};Database={databaseName}",
            EnvironmentMarker: environmentMarker ?? EnvironmentId,
            IsAliasOfProduction: isAliasOfProduction,
            MatchesProductionIdentity: matchesProductionIdentity);

    public static QlhvDirectRealtimeApplyOperation InsertOperation(
        string identity = InsertIdentity)
        => new(
            $"OP-INSERT-{identity}",
            QlhvDirectRealtimeApplyOperationKind.Insert,
            QlhvDirectRealtimeDispositions.WouldInsertSafeAfterApproval,
            identity,
            InsertSourceHash,
            string.Empty,
            string.Empty,
            [],
            "SYNTHETIC LEARNER INSERT");

    public static QlhvDirectRealtimeApplyOperation UpdateOperation(
        IReadOnlyList<string>? requestedColumns = null,
        string identity = UpdateIdentity)
        => new(
            $"OP-UPDATE-{identity}",
            QlhvDirectRealtimeApplyOperationKind.Update,
            QlhvDirectRealtimeDispositions.StaleImportedValue,
            identity,
            UpdateSourceHash,
            OldMappedHash,
            QlhvOwnedHash,
            requestedColumns ?? ["HoTen"],
            "SYNTHETIC LEARNER UPDATED");

    public static QlhvDirectRealtimeApplyOperation RetainOperation(
        string identity = RetainIdentity)
        => new(
            $"OP-RETAIN-{identity}",
            QlhvDirectRealtimeApplyOperationKind.RetainForManualReview,
            QlhvDirectRealtimeDispositions.ManualReviewRequired,
            identity,
            string.Empty,
            OldMappedHash,
            QlhvOwnedHash,
            []);

    public static QlhvDirectRealtimeApplyPlan Plan(
        IReadOnlyList<QlhvDirectRealtimeApplyOperation> operations,
        string? cycleId = null,
        string? environmentId = null,
        string? sourceProfile = null,
        string? mappingFingerprint = null,
        long sourceWatermark = 700)
        => new(
            cycleId ?? $"RT02-CYCLE-{Guid.NewGuid():N}",
            environmentId ?? EnvironmentId,
            sourceProfile ?? SourceProfile,
            mappingFingerprint ?? "MAPPING-SHA256-V1",
            "SOURCE-SCHEMA-SHA256-V1",
            "TARGET-SCHEMA-SHA256-V1",
            sourceWatermark,
            "IDENTITY-NORMALIZATION-V1",
            "STAGE-SHA256-V1",
            "COMPARISON-SHA256-V1",
            QlhvDirectRealtimeHash.Sha256(
                string.Join("|", operations.Select(operation => operation.Disposition))),
            operations);

    public static void SeedForPlan(
        Rt02InMemoryTargetStore store,
        QlhvDirectRealtimeApplyPlan plan)
    {
        foreach (var operation in plan.Operations)
        {
            if (operation.Kind == QlhvDirectRealtimeApplyOperationKind.Insert)
            {
                store.CurrentSourceHashes[operation.IdentityHmac] =
                    operation.SourceRowHash;
            }
            else if (operation.Kind == QlhvDirectRealtimeApplyOperationKind.Update)
            {
                store.CurrentSourceHashes[operation.IdentityHmac] =
                    operation.SourceRowHash;
                store.Learners[operation.IdentityHmac] = new Rt02TestLearner
                {
                    IdentityHmac = operation.IdentityHmac,
                    SourceProfile = SourceProfile,
                    HoTen = "SYNTHETIC LEARNER OLD",
                    MappedHash = operation.StagedTargetMappedHash,
                    QlhvOwnedHash = operation.StagedQlhvOwnedHash,
                };
            }
            else
            {
                store.Learners[operation.IdentityHmac] = new Rt02TestLearner
                {
                    IdentityHmac = operation.IdentityHmac,
                    SourceProfile = SourceProfile,
                    HoTen = "SYNTHETIC TARGET ONLY",
                    MappedHash = operation.StagedTargetMappedHash,
                    QlhvOwnedHash = operation.StagedQlhvOwnedHash,
                };
            }
        }
    }

    private static Guid GuidFrom(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        bytes[15] = 0xA7;
        return new Guid(bytes);
    }
}

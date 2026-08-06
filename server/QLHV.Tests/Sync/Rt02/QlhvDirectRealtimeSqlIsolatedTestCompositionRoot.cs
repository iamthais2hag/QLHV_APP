using System.Data;
using System.Diagnostics;
using System.Text;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync.QlhvDirectRealtime;

namespace QLHV.Tests.Sync.Rt02;

/// <summary>
/// RT-02B2-only SQL composition root. It remains in the test assembly and has
/// fixed allowlisted database identities. Production projects cannot register
/// or reference this writer.
/// </summary>
internal sealed class QlhvDirectRealtimeSqlIsolatedTestCompositionRoot
{
    public QlhvDirectRealtimeSqlIsolatedTestCompositionRoot(
        string sourceProfile,
        string sourceDatabase,
        Rt02SqlCommitFaultController? commitFault = null,
        IQlhvDirectRealtimeFaultInjector? faultInjector = null)
    {
        if (string.IsNullOrWhiteSpace(sourceProfile))
        {
            throw new ArgumentException("A test-only source profile is required.");
        }

        Metrics = new Rt02SqlHarnessMetrics();
        CommitFault = commitFault ?? new Rt02SqlCommitFaultController();
        TransactionFactory = new Rt02SqlTransactionFactory(
            sourceProfile,
            sourceDatabase,
            Rt02b2SqlRoute.TargetDatabase,
            Metrics,
            CommitFault);
        Checkpoints = new Rt02SqlCheckpointStore(
            Rt02b2SqlRoute.TargetDatabase,
            Metrics);
        Options = new QlhvDirectRealtimeOptions
        {
            EnableQlhvDirectRealtime = true,
            EnableQlhvDirectRealtimeShadow = false,
            EnableQlhvDirectRealtimeWrites = true,
            EnableQlhvDirectRealtimeDeletes = false,
            EnableQlhvDirectRealtimeIsolatedApply = true,
        };
        Cycle = new QlhvDirectRealtimeApplyCycle(
            Options,
            TransactionFactory,
            Checkpoints,
            faultInjector);
    }

    public QlhvDirectRealtimeOptions Options { get; }

    public Rt02SqlHarnessMetrics Metrics { get; }

    public Rt02SqlCommitFaultController CommitFault { get; }

    public Rt02SqlTransactionFactory TransactionFactory { get; }

    public Rt02SqlCheckpointStore Checkpoints { get; }

    public QlhvDirectRealtimeApplyCycle Cycle { get; }
}

internal static class Rt02b2SqlRoute
{
    public const string ServerIdentity = @"CSDLTTTC\QLHVRT02";
    public const string SharedMemoryServer = @"lpc:CSDLTTTC\QLHVRT02";
    public const string OtoDatabase = "QLHV_RT02_OTO_TEST";
    public const string MotoDatabase = "QLHV_RT02_MOTO_TEST";
    public const string TargetDatabase = "QLHV_RT02_TARGET_TEST";
    public const string EnvironmentId =
        "RT02B0-CSDLTTTC-QLHVRT02-20260727-01";
    public const string ApprovalId =
        "RT02B-OPERATOR-APPROVAL-20260727-01";
    public const string ExpiryUtc = "2026-07-31T16:59:59Z";
    public const string ApplicationName = "QLHV.RT02B2.IsolatedSqlHarness";

    private static readonly IReadOnlySet<string> AllowedDatabases =
        new HashSet<string>(
            [OtoDatabase, MotoDatabase, TargetDatabase],
            StringComparer.Ordinal);

    public static string ConnectionString(string database)
    {
        if (!AllowedDatabases.Contains(database))
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.IsolatedDatabaseIdentityRejected,
                "The SQL harness database route is not allowlisted.");
        }

        return new SqlConnectionStringBuilder
        {
            DataSource = SharedMemoryServer,
            InitialCatalog = database,
            IntegratedSecurity = true,
            Encrypt = false,
            TrustServerCertificate = true,
            ApplicationName = ApplicationName,
            ConnectTimeout = 15,
            Pooling = false,
        }.ConnectionString;
    }

    public static async Task<SqlConnection> OpenConnectionAsync(
        string database,
        CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(ConnectionString(database));
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

IF CONVERT(int, SESSIONPROPERTY('ANSI_NULLS')) <> 1
   OR CONVERT(int, SESSIONPROPERTY('ANSI_PADDING')) <> 1
   OR CONVERT(int, SESSIONPROPERTY('ANSI_WARNINGS')) <> 1
   OR CONVERT(int, SESSIONPROPERTY('ARITHABORT')) <> 1
   OR CONVERT(int, SESSIONPROPERTY('CONCAT_NULL_YIELDS_NULL')) <> 1
   OR CONVERT(int, SESSIONPROPERTY('QUOTED_IDENTIFIER')) <> 1
   OR CONVERT(int, SESSIONPROPERTY('NUMERIC_ROUNDABORT')) <> 0
    THROW 528200, 'RT02 filtered-index session SET vector rejected.', 1;
""";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

internal sealed class Rt02SqlHarnessMetrics
{
    private int _queryCount;
    private int _openTransactionCount;
    private int _commitCount;
    private int _rollbackCount;
    private int _secondSessionWriteCount;

    public int QueryCount => Volatile.Read(ref _queryCount);

    public int OpenTransactionCount =>
        Volatile.Read(ref _openTransactionCount);

    public int CommitCount => Volatile.Read(ref _commitCount);

    public int RollbackCount => Volatile.Read(ref _rollbackCount);

    public int SecondSessionWriteCount =>
        Volatile.Read(ref _secondSessionWriteCount);

    public TimeSpan LastTransactionDuration { get; set; }

    public string? LastLockName { get; set; }

    public void Query() => Interlocked.Increment(ref _queryCount);

    public void OpenTransaction() =>
        Interlocked.Increment(ref _openTransactionCount);

    public void Commit() => Interlocked.Increment(ref _commitCount);

    public void Rollback() => Interlocked.Increment(ref _rollbackCount);

    public void SecondSessionWrite() =>
        Interlocked.Increment(ref _secondSessionWriteCount);
}

internal enum Rt02SqlCommitFaultMode
{
    None,
    TimeoutOnce,
    DeadlockOnce,
}

internal sealed class Rt02SqlCommitFaultController
{
    private int _mode;

    public Rt02SqlCommitFaultMode Mode
    {
        get => (Rt02SqlCommitFaultMode)Volatile.Read(ref _mode);
        set => Volatile.Write(ref _mode, (int)value);
    }

    public Rt02SqlCommitFaultMode Consume()
        => (Rt02SqlCommitFaultMode)Interlocked.Exchange(
            ref _mode,
            (int)Rt02SqlCommitFaultMode.None);
}

internal sealed class Rt02InjectedDeadlockException : Exception
{
    public Rt02InjectedDeadlockException()
        : base("Injected isolated SQL deadlock before target commit.")
    {
    }
}

internal sealed class Rt02SqlTransactionFactory :
    IQlhvDirectRealtimeTargetTransactionFactory
{
    private readonly string _sourceProfile;
    private readonly string _sourceDatabase;
    private readonly string _targetDatabase;
    private readonly Rt02SqlHarnessMetrics _metrics;
    private readonly Rt02SqlCommitFaultController _commitFault;

    public Rt02SqlTransactionFactory(
        string sourceProfile,
        string sourceDatabase,
        string targetDatabase,
        Rt02SqlHarnessMetrics metrics,
        Rt02SqlCommitFaultController commitFault)
    {
        if (sourceDatabase is not
            (Rt02b2SqlRoute.OtoDatabase or Rt02b2SqlRoute.MotoDatabase))
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.IsolatedDatabaseIdentityRejected,
                "The SQL harness source route is not allowlisted.");
        }

        _sourceProfile = sourceProfile;
        _sourceDatabase = sourceDatabase;
        _targetDatabase = targetDatabase;
        _metrics = metrics;
        _commitFault = commitFault;
    }

    public bool CreateTargetBeforeInsert { get; set; }

    public bool ChangeTargetBeforeUpdate { get; set; }

    public bool FailUpdate { get; set; }

    public bool FailVerification { get; set; }

    public QlhvDirectRealtimeApplyMarker? CommittedMarkerOverride { get; set; }

    public string? BeforeVerificationProcessTerminationSignalPath { get; set; }

    public async Task<IQlhvDirectRealtimeTargetTransaction> OpenAsync(
        CancellationToken cancellationToken)
    {
        var connection = await Rt02b2SqlRoute.OpenConnectionAsync(
            _targetDatabase,
            cancellationToken);
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        _metrics.OpenTransaction();
        return new Rt02SqlTargetTransaction(
            connection,
            transaction,
            _sourceProfile,
            _sourceDatabase,
            _metrics,
            _commitFault,
            () =>
            {
                var value = CreateTargetBeforeInsert;
                CreateTargetBeforeInsert = false;
                return value;
            },
            () =>
            {
                var value = ChangeTargetBeforeUpdate;
                ChangeTargetBeforeUpdate = false;
                return value;
            },
            () => FailUpdate,
            () => FailVerification,
            () => BeforeVerificationProcessTerminationSignalPath);
    }

    public async Task<QlhvDirectRealtimeApplyMarker?> FindCommittedMarkerAsync(
        string cycleId,
        CancellationToken cancellationToken)
    {
        var markerOverride = CommittedMarkerOverride;
        if (markerOverride is not null &&
            string.Equals(
                markerOverride.CycleId,
                cycleId,
                StringComparison.Ordinal))
        {
            CommittedMarkerOverride = null;
            return markerOverride;
        }

        await using var connection = await Rt02b2SqlRoute.OpenConnectionAsync(
            _targetDatabase,
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT
    CycleId,
    PlanHash,
    DispositionHash,
    InsertedRows,
    UpdatedRows,
    RetainedRows,
    PreservedQlhvOwnedHash,
    CommittedAtUtc
FROM dbo.Rt02ApplyMarker
WHERE CycleId = @CycleId;
""";
        command.Parameters.Add("@CycleId", SqlDbType.VarChar, 120).Value =
            cycleId;
        _metrics.Query();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new QlhvDirectRealtimeApplyMarker(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetString(6),
            reader.GetDateTime(7));
    }
}

internal sealed class Rt02SqlTargetTransaction :
    IQlhvDirectRealtimeTargetTransaction
{
    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;
    private readonly string _sourceProfile;
    private readonly string _sourceDatabase;
    private readonly Rt02SqlHarnessMetrics _metrics;
    private readonly Rt02SqlCommitFaultController _commitFault;
    private readonly Func<bool> _createTargetBeforeInsert;
    private readonly Func<bool> _changeTargetBeforeUpdate;
    private readonly Func<bool> _failUpdate;
    private readonly Func<bool> _failVerification;
    private readonly Func<string?> _beforeVerificationSignalPath;
    private readonly Stopwatch _timer = Stopwatch.StartNew();
    private bool _finished;
    private int _inserted;
    private int _updated;
    private int _retained;

    public Rt02SqlTargetTransaction(
        SqlConnection connection,
        SqlTransaction transaction,
        string sourceProfile,
        string sourceDatabase,
        Rt02SqlHarnessMetrics metrics,
        Rt02SqlCommitFaultController commitFault,
        Func<bool> createTargetBeforeInsert,
        Func<bool> changeTargetBeforeUpdate,
        Func<bool> failUpdate,
        Func<bool> failVerification,
        Func<string?> beforeVerificationSignalPath)
    {
        _connection = connection;
        _transaction = transaction;
        _sourceProfile = sourceProfile;
        _sourceDatabase = sourceDatabase;
        _metrics = metrics;
        _commitFault = commitFault;
        _createTargetBeforeInsert = createTargetBeforeInsert;
        _changeTargetBeforeUpdate = changeTargetBeforeUpdate;
        _failUpdate = failUpdate;
        _failVerification = failVerification;
        _beforeVerificationSignalPath = beforeVerificationSignalPath;
    }

    public async Task RevalidateIsolatedTargetIdentityAsync(
        QlhvDirectRealtimeIsolatedEnvironment environment,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT
    DB_NAME(),
    CONVERT(int, DB_ID()),
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')),
    CONVERT(nvarchar(36), recovery.database_guid),
    databaseItem.state_desc,
    databaseItem.is_read_only,
    databaseItem.source_database_id,
    CONVERT(nvarchar(128),
        (
            SELECT value
            FROM sys.extended_properties
            WHERE class = 0
              AND name = N'RT02_ISOLATED_ENVIRONMENT_ID'
        )),
    CONVERT(nvarchar(128),
        (
            SELECT value
            FROM sys.extended_properties
            WHERE class = 0
              AND name = N'RT02_OWNER_APPROVAL_ID'
        )),
    CONVERT(nvarchar(128),
        (
            SELECT value
            FROM sys.extended_properties
            WHERE class = 0
              AND name = N'RT02_DATASET_MODE'
        )),
    CONVERT(nvarchar(128),
        (
            SELECT value
            FROM sys.extended_properties
            WHERE class = 0
              AND name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
        ))
FROM sys.databases AS databaseItem
INNER JOIN sys.database_recovery_status AS recovery
    ON recovery.database_id = databaseItem.database_id
WHERE databaseItem.database_id = DB_ID();
""";
        await using var command = Command(sql);
        _metrics.Query();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) ||
            !string.Equals(
                reader.GetString(0),
                environment.IsolatedTargetDatabase,
                StringComparison.Ordinal) ||
            reader.GetInt32(1) != 7 ||
            !string.Equals(
                reader.GetString(2),
                environment.SqlServerInstance,
                StringComparison.Ordinal) ||
            !Guid.TryParse(reader.GetString(3), out var databaseGuid) ||
            databaseGuid != Guid.Parse(
                "F7BAC56F-8329-47AB-A17C-A0D592ADD484") ||
            !string.Equals(reader.GetString(4), "ONLINE", StringComparison.Ordinal) ||
            reader.GetBoolean(5) ||
            !reader.IsDBNull(6) ||
            !string.Equals(
                reader.GetString(7),
                environment.EnvironmentId,
                StringComparison.Ordinal) ||
            !string.Equals(
                reader.GetString(8),
                environment.OwnerApprovalId,
                StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(9), "SYNTHETIC", StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(10), "FALSE", StringComparison.Ordinal))
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.IsolatedDatabaseIdentityRejected,
                "The target SQL identity/marker revalidation failed.");
        }
    }

    public async Task AcquireSourceProfileLockAsync(
        string lockName,
        CancellationToken cancellationToken)
    {
        await using var command = Command("""
DECLARE @Result int;
EXEC @Result = sys.sp_getapplock
    @Resource = @Resource,
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 10000;
SELECT @Result;
""");
        command.Parameters.Add("@Resource", SqlDbType.NVarChar, 255).Value =
            lockName;
        _metrics.Query();
        var result = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0)
        {
            throw new TimeoutException(
                "The isolated source-profile transaction lock was not acquired.");
        }

        _metrics.LastLockName = lockName;
    }

    public async Task VerifyPlanFingerprintsAsync(
        QlhvDirectRealtimeApplyPlan plan,
        CancellationToken cancellationToken)
    {
        await using var command = Command("""
SELECT
    MappingFingerprint,
    SourceSchemaFingerprint,
    TargetSchemaFingerprint,
    IdentityNormalizationVersion,
    DatasetFingerprint
FROM dbo.Rt02EnvironmentState
WHERE EnvironmentId = @EnvironmentId
  AND DatasetMode = 'SYNTHETIC'
  AND PiiRows = 0;
""");
        command.Parameters.Add("@EnvironmentId", SqlDbType.VarChar, 128).Value =
            plan.EnvironmentId;
        _metrics.Query();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) ||
            !string.Equals(reader.GetString(0), plan.MappingFingerprint, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(1), plan.SourceSchemaFingerprint, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(2), plan.TargetSchemaFingerprint, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(3), plan.IdentityNormalizationVersion, StringComparison.Ordinal))
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.PlanFingerprintConflict,
                "The SQL mapping/schema/normalization fingerprint drifted.");
        }
    }

    public async Task InsertAsync(
        QlhvDirectRealtimeApplyOperation operation,
        CancellationToken cancellationToken)
    {
        if (_createTargetBeforeInsert())
        {
            await using var concurrentConnection =
                await Rt02b2SqlRoute.OpenConnectionAsync(
                    _connection.Database,
                    cancellationToken);
            await using var concurrent = concurrentConnection.CreateCommand();
            concurrent.CommandText = """
INSERT dbo.Rt02Learner
(
    IdentityHmac, SourceProfile, ScenarioCode, DatasetRole, HoTen,
    MappedHash, QlhvOwnedHash, WorkflowState, NotesHash, PhotoState,
    Active, SoftDeleted
)
VALUES
(
    @IdentityHmac, @SourceProfile, 'FAULT_INJECTION',
    'CONCURRENT_TARGET', N'SYNTHETIC CONCURRENT TARGET',
    @MappedHash, @QlhvOwnedHash, 'READY', @NotesHash,
    'PHOTO_DISABLED', 1, 0
);
""";
            AddIdentity(concurrent, operation.IdentityHmac);
            concurrent.Parameters.Add("@SourceProfile", SqlDbType.VarChar, 20).Value =
                _sourceProfile;
            concurrent.Parameters.Add("@MappedHash", SqlDbType.Char, 64).Value =
                QlhvDirectRealtimeHash.Sha256("RT02B2-CONCURRENT-MAPPED");
            concurrent.Parameters.Add("@QlhvOwnedHash", SqlDbType.Char, 64).Value =
                QlhvDirectRealtimeHash.Sha256("RT02B2-CONCURRENT-QLHV");
            concurrent.Parameters.Add("@NotesHash", SqlDbType.Char, 64).Value =
                QlhvDirectRealtimeHash.Sha256("RT02B2-CONCURRENT-NOTES");
            _metrics.Query();
            var created = await concurrent.ExecuteNonQueryAsync(cancellationToken);
            if (created != 1)
            {
                throw new QlhvDirectRealtimeSafetyException(
                    QlhvDirectRealtimeErrors.TargetChangedSinceShadow,
                    "The second-session target creation did not affect exactly one row.");
            }

            _metrics.SecondSessionWrite();
        }

        await EnsureCurrentSourceHashAsync(operation, cancellationToken);

        await using (var existence = Command("""
SELECT COUNT_BIG(*)
FROM dbo.Rt02Learner
WHERE IdentityHmac = @IdentityHmac;
"""))
        {
            AddIdentity(existence, operation.IdentityHmac);
            _metrics.Query();
            var existing = Convert.ToInt64(
                await existence.ExecuteScalarAsync(cancellationToken));
            if (existing != 0)
            {
                throw new QlhvDirectRealtimeSafetyException(
                    QlhvDirectRealtimeErrors.TargetChangedSinceShadow,
                    "The SQL target identity appeared after shadow.");
            }
        }

        await using var command = Command("""
INSERT dbo.Rt02Learner
(
    IdentityHmac, SourceProfile, ScenarioCode, DatasetRole, HoTen,
    MappedHash, QlhvOwnedHash, WorkflowState, NotesHash, PhotoState,
    Active, SoftDeleted
)
VALUES
(
    @IdentityHmac, @SourceProfile, 'HARNESS',
    'SOURCE_ONLY_NEW_ROW', @HoTen, @SourceRowHash,
    @QlhvOwnedHash, 'READY', @NotesHash, 'PHOTO_DISABLED', 1, 0
);
""");
        AddIdentity(command, operation.IdentityHmac);
        command.Parameters.Add("@SourceProfile", SqlDbType.VarChar, 20).Value =
            _sourceProfile;
        command.Parameters.Add("@HoTen", SqlDbType.NVarChar, 200).Value =
            operation.DesiredHoTen ?? "SYNTHETIC INSERT";
        command.Parameters.Add("@SourceRowHash", SqlDbType.Char, 64).Value =
            operation.SourceRowHash;
        command.Parameters.Add("@QlhvOwnedHash", SqlDbType.Char, 64).Value =
            QlhvDirectRealtimeHash.Sha256(
                $"RT02B2|QLHV|{operation.IdentityHmac}|READY|NOTES|PHOTO_DISABLED");
        command.Parameters.Add("@NotesHash", SqlDbType.Char, 64).Value =
            QlhvDirectRealtimeHash.Sha256(
                $"RT02B2|NOTES|{operation.IdentityHmac}");
        _metrics.Query();
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        if (inserted != 1)
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.TargetChangedSinceShadow,
                "The isolated insert affected a row count other than one.");
        }

        _inserted++;
    }

    public async Task UpdateSourceOwnedFieldsAsync(
        QlhvDirectRealtimeApplyOperation operation,
        CancellationToken cancellationToken)
    {
        if (_failUpdate())
        {
            throw new InvalidOperationException(
                "Injected isolated update failure.");
        }

        await EnsureCurrentSourceHashAsync(operation, cancellationToken);

        if (_changeTargetBeforeUpdate())
        {
            await using var concurrentConnection =
                await Rt02b2SqlRoute.OpenConnectionAsync(
                    _connection.Database,
                    cancellationToken);
            await using var change = concurrentConnection.CreateCommand();
            change.CommandText = """
UPDATE dbo.Rt02Learner
SET MappedHash = @ChangedHash,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE IdentityHmac = @IdentityHmac
  AND SourceProfile = @SourceProfile
  AND Active = 1
  AND SoftDeleted = 0;
""";
            AddIdentity(change, operation.IdentityHmac);
            change.Parameters.Add("@SourceProfile", SqlDbType.VarChar, 20).Value =
                _sourceProfile;
            change.Parameters.Add("@ChangedHash", SqlDbType.Char, 64).Value =
                QlhvDirectRealtimeHash.Sha256("RT02B2-CONCURRENT-TARGET-CHANGE");
            _metrics.Query();
            var changed = await change.ExecuteNonQueryAsync(cancellationToken);
            if (changed != 1)
            {
                throw new QlhvDirectRealtimeSafetyException(
                    QlhvDirectRealtimeErrors.TargetChangedSinceShadow,
                    "The second-session target change did not affect exactly one row.");
            }

            _metrics.SecondSessionWrite();
        }

        await using var command = Command("""
UPDATE dbo.Rt02Learner
SET HoTen = @HoTen,
    MappedHash = @SourceRowHash,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE IdentityHmac = @IdentityHmac
  AND SourceProfile = @SourceProfile
  AND Active = 1
  AND SoftDeleted = 0
  AND MappedHash = @ExpectedMappedHash
  AND QlhvOwnedHash = @ExpectedQlhvOwnedHash;
""");
        AddIdentity(command, operation.IdentityHmac);
        command.Parameters.Add("@SourceProfile", SqlDbType.VarChar, 20).Value =
            _sourceProfile;
        command.Parameters.Add("@HoTen", SqlDbType.NVarChar, 200).Value =
            operation.DesiredHoTen!;
        command.Parameters.Add("@SourceRowHash", SqlDbType.Char, 64).Value =
            operation.SourceRowHash;
        command.Parameters.Add("@ExpectedMappedHash", SqlDbType.Char, 64).Value =
            operation.StagedTargetMappedHash;
        command.Parameters.Add("@ExpectedQlhvOwnedHash", SqlDbType.Char, 64).Value =
            operation.StagedQlhvOwnedHash;
        _metrics.Query();
        var updated = await command.ExecuteNonQueryAsync(cancellationToken);
        if (updated != 1)
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.TargetChangedSinceShadow,
                "The isolated HoTen update affected a row count other than one.");
        }

        _updated++;
    }

    public async Task RetainAndRecordManualReviewAsync(
        QlhvDirectRealtimeManualReviewEvidence evidence,
        CancellationToken cancellationToken)
    {
        await using (var verify = Command("""
SELECT COUNT_BIG(*)
FROM dbo.Rt02Learner
WHERE IdentityHmac = @IdentityHmac
  AND SourceProfile = @SourceProfile
  AND Active = 1
  AND SoftDeleted = 0;
"""))
        {
            AddIdentity(verify, evidence.IdentityHmac);
            verify.Parameters.Add("@SourceProfile", SqlDbType.VarChar, 20).Value =
                _sourceProfile;
            _metrics.Query();
            var count = Convert.ToInt64(
                await verify.ExecuteScalarAsync(cancellationToken));
            if (count != 1 ||
                !evidence.TargetRetainedActive ||
                evidence.TargetMutated)
            {
                throw new QlhvDirectRealtimeSafetyException(
                    QlhvDirectRealtimeErrors.TargetChangedSinceShadow,
                    "The isolated target-only retention precondition failed.");
            }
        }

        await using var command = Command("""
INSERT dbo.Rt02ManualReviewEvidence
(
    CycleId, OperationId, IdentityHmac, Disposition, DispositionHash,
    TargetRetainedActive, TargetMutated
)
VALUES
(
    @CycleId, @OperationId, @IdentityHmac, @Disposition,
    @DispositionHash, 1, 0
);
""");
        command.Parameters.Add("@CycleId", SqlDbType.VarChar, 120).Value =
            evidence.CycleId;
        command.Parameters.Add("@OperationId", SqlDbType.VarChar, 160).Value =
            evidence.OperationId;
        AddIdentity(command, evidence.IdentityHmac);
        command.Parameters.Add("@Disposition", SqlDbType.VarChar, 60).Value =
            evidence.Disposition;
        command.Parameters.Add("@DispositionHash", SqlDbType.Char, 64).Value =
            evidence.DispositionHash;
        _metrics.Query();
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        if (inserted != 1)
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.TargetChangedSinceShadow,
                "The manual-review evidence was not persisted exactly once.");
        }

        _retained++;
    }

    public async Task<QlhvDirectRealtimeTargetVerification> VerifyAsync(
        QlhvDirectRealtimeApplyPlan plan,
        CancellationToken cancellationToken)
    {
        var processTerminationSignalPath = _beforeVerificationSignalPath();
        if (!string.IsNullOrWhiteSpace(processTerminationSignalPath))
        {
            await Rt02ProcessTerminationSignal.WriteAndBlockAsync(
                processTerminationSignalPath,
                "INSIDE_TARGET_TRANSACTION",
                marker: null);
        }

        if (_failVerification())
        {
            throw new InvalidOperationException(
                "Injected isolated final verification failure.");
        }

        var hashes = new List<string>();
        await using var command = Command("""
SELECT QlhvOwnedHash
FROM dbo.Rt02Learner
ORDER BY IdentityHmac;
""");
        _metrics.Query();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            hashes.Add(reader.GetString(0));
        }

        return new QlhvDirectRealtimeTargetVerification(
            _inserted,
            _updated,
            _retained,
            QlhvDirectRealtimeHash.Sha256(string.Join("|", hashes)));
    }

    public async Task WriteApplyMarkerAsync(
        QlhvDirectRealtimeApplyMarker marker,
        CancellationToken cancellationToken)
    {
        await using var command = Command("""
IF EXISTS
(
    SELECT 1
    FROM dbo.Rt02ApplyMarker
    WHERE CycleId = @CycleId
)
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Rt02ApplyMarker
        WHERE CycleId = @CycleId
          AND PlanHash = @PlanHash
          AND DispositionHash = @DispositionHash
          AND InsertedRows = @InsertedRows
          AND UpdatedRows = @UpdatedRows
          AND RetainedRows = @RetainedRows
          AND PreservedQlhvOwnedHash = @PreservedQlhvOwnedHash
    )
        THROW 527430, 'CHECKPOINT_CONFLICT: durable marker content.', 1;
END
ELSE
BEGIN
    INSERT dbo.Rt02ApplyMarker
    (
        CycleId, PlanHash, DispositionHash, InsertedRows, UpdatedRows,
        RetainedRows, PreservedQlhvOwnedHash, CommittedAtUtc
    )
    VALUES
    (
        @CycleId, @PlanHash, @DispositionHash, @InsertedRows,
        @UpdatedRows, @RetainedRows, @PreservedQlhvOwnedHash,
        @CommittedAtUtc
    );
END;
""");
        command.Parameters.Add("@CycleId", SqlDbType.VarChar, 120).Value =
            marker.CycleId;
        command.Parameters.Add("@PlanHash", SqlDbType.Char, 64).Value =
            marker.PlanHash;
        command.Parameters.Add("@DispositionHash", SqlDbType.Char, 64).Value =
            marker.DispositionHash;
        command.Parameters.Add("@InsertedRows", SqlDbType.Int).Value =
            marker.InsertedRows;
        command.Parameters.Add("@UpdatedRows", SqlDbType.Int).Value =
            marker.UpdatedRows;
        command.Parameters.Add("@RetainedRows", SqlDbType.Int).Value =
            marker.RetainedRows;
        command.Parameters.Add("@PreservedQlhvOwnedHash", SqlDbType.Char, 64).Value =
            marker.PreservedQlhvOwnedHash;
        command.Parameters.Add("@CommittedAtUtc", SqlDbType.DateTime2).Value =
            marker.CommittedAtUtc;
        _metrics.Query();
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        var fault = _commitFault.Consume();
        if (fault == Rt02SqlCommitFaultMode.TimeoutOnce)
        {
            throw new TimeoutException(
                "Injected isolated target timeout before commit.");
        }

        if (fault == Rt02SqlCommitFaultMode.DeadlockOnce)
        {
            throw new Rt02InjectedDeadlockException();
        }

        await _transaction.CommitAsync(cancellationToken);
        _timer.Stop();
        _metrics.LastTransactionDuration = _timer.Elapsed;
        _metrics.Commit();
        _finished = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (_finished)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        _timer.Stop();
        _metrics.LastTransactionDuration = _timer.Elapsed;
        _metrics.Rollback();
        _finished = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_finished)
        {
            await _transaction.RollbackAsync();
            _timer.Stop();
            _metrics.LastTransactionDuration = _timer.Elapsed;
            _metrics.Rollback();
            _finished = true;
        }

        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task EnsureCurrentSourceHashAsync(
        QlhvDirectRealtimeApplyOperation operation,
        CancellationToken cancellationToken)
    {
        var sql = $"""
SELECT SourceRowHash
FROM [{_sourceDatabase}].dbo.NguoiLX
WHERE IdentityHmac = @IdentityHmac
  AND IsActive = 1;
""";
        await using var command = Command(sql);
        AddIdentity(command, operation.IdentityHmac);
        _metrics.Query();
        var current = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (!string.Equals(
                current,
                operation.SourceRowHash,
                StringComparison.Ordinal))
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.SourceChangedSinceShadow,
                "The isolated source row hash changed after shadow.");
        }
    }

    private SqlCommand Command(string sql)
        => new(sql, _connection, _transaction)
        {
            CommandTimeout = 30,
        };

    private static void AddIdentity(SqlCommand command, string identity)
        => command.Parameters.Add("@IdentityHmac", SqlDbType.Char, 64).Value =
            identity;
}

internal static class Rt02ProcessTerminationSignal
{
    public static async Task WriteAndBlockAsync(
        string signalPath,
        string mode,
        QlhvDirectRealtimeApplyMarker? marker)
    {
        var payload = string.Join(
            Environment.NewLine,
            $"Mode={mode}",
            $"ProcessId={Environment.ProcessId}",
            $"SignaledAtUtc={DateTime.UtcNow:O}",
            $"CycleId={marker?.CycleId ?? string.Empty}",
            $"PlanHash={marker?.PlanHash ?? string.Empty}",
            $"MarkerHash={marker?.MarkerHash ?? string.Empty}");
        var bytes = Encoding.UTF8.GetBytes(payload);
        var temporarySignalPath = $"{signalPath}.tmp";
        await using (var stream = new FileStream(
            temporarySignalPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporarySignalPath, signalPath, overwrite: false);
        await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
    }
}

internal sealed class Rt02SqlCheckpointStore :
    IQlhvDirectRealtimeApplyCheckpointStore
{
    private readonly string _targetDatabase;
    private readonly Rt02SqlHarnessMetrics _metrics;

    public Rt02SqlCheckpointStore(
        string targetDatabase,
        Rt02SqlHarnessMetrics metrics)
    {
        _targetDatabase = targetDatabase;
        _metrics = metrics;
    }

    public async Task<QlhvDirectRealtimeApplyCheckpoint?> ReadAsync(
        QlhvDirectRealtimeApplyCheckpointKey key,
        CancellationToken cancellationToken)
    {
        await using var connection = await Rt02b2SqlRoute.OpenConnectionAsync(
            _targetDatabase,
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT
    CycleId,
    PlanHash,
    MarkerHash,
    SourceWatermark,
    PublishedAtUtc
FROM dbo.Rt02ApplyCheckpoint
WHERE SourceProfile = @SourceProfile
  AND Mode = @Mode
  AND MappingFingerprint = @MappingFingerprint
  AND EnvironmentId = @EnvironmentId;
""";
        AddKey(command, key);
        _metrics.Query();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new QlhvDirectRealtimeApplyCheckpoint(
            key,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetDateTime(4));
    }

    public async Task PublishAsync(
        QlhvDirectRealtimeApplyCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        await using var connection = await Rt02b2SqlRoute.OpenConnectionAsync(
            _targetDatabase,
            cancellationToken);
        await using var transaction = (SqlTransaction)
            await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        try
        {
            await using var command = new SqlCommand("""
IF EXISTS
(
    SELECT 1
    FROM dbo.Rt02ApplyCheckpoint WITH (UPDLOCK, HOLDLOCK)
    WHERE SourceProfile = @SourceProfile
      AND Mode = @Mode
      AND MappingFingerprint = @MappingFingerprint
      AND EnvironmentId = @EnvironmentId
)
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Rt02ApplyCheckpoint
        WHERE SourceProfile = @SourceProfile
          AND Mode = @Mode
          AND MappingFingerprint = @MappingFingerprint
          AND EnvironmentId = @EnvironmentId
          AND CycleId = @CycleId
          AND PlanHash = @PlanHash
          AND MarkerHash = @MarkerHash
          AND SourceWatermark = @SourceWatermark
    )
        THROW 527431, 'CHECKPOINT_CONFLICT: isolated checkpoint content.', 1;
END
ELSE
BEGIN
    INSERT dbo.Rt02ApplyCheckpoint
    (
        SourceProfile, Mode, MappingFingerprint, EnvironmentId,
        CycleId, PlanHash, MarkerHash, SourceWatermark, PublishedAtUtc
    )
    VALUES
    (
        @SourceProfile, @Mode, @MappingFingerprint, @EnvironmentId,
        @CycleId, @PlanHash, @MarkerHash, @SourceWatermark, @PublishedAtUtc
    );
END;
""", connection, transaction);
            AddKey(command, checkpoint.Key);
            command.Parameters.Add("@CycleId", SqlDbType.VarChar, 120).Value =
                checkpoint.CycleId;
            command.Parameters.Add("@PlanHash", SqlDbType.Char, 64).Value =
                checkpoint.PlanHash;
            command.Parameters.Add("@MarkerHash", SqlDbType.Char, 64).Value =
                checkpoint.MarkerHash;
            command.Parameters.Add("@SourceWatermark", SqlDbType.BigInt).Value =
                checkpoint.SourceWatermark;
            command.Parameters.Add("@PublishedAtUtc", SqlDbType.DateTime2).Value =
                checkpoint.PublishedAtUtc;
            _metrics.Query();
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException error) when (error.Number == 527431)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.CheckpointConflict,
                "The SQL checkpoint key already contains different content.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void AddKey(
        SqlCommand command,
        QlhvDirectRealtimeApplyCheckpointKey key)
    {
        command.Parameters.Add("@SourceProfile", SqlDbType.VarChar, 20).Value =
            key.SourceProfile;
        command.Parameters.Add("@Mode", SqlDbType.VarChar, 40).Value =
            key.Mode;
        command.Parameters.Add("@MappingFingerprint", SqlDbType.Char, 64).Value =
            key.MappingFingerprint;
        command.Parameters.Add("@EnvironmentId", SqlDbType.VarChar, 128).Value =
            key.EnvironmentId;
    }
}

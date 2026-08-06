using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.CsdtConnections;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Rt01;
using QLHV.Application.Sync.Rt03;
using QLHV.Infrastructure.Sync.Rt01;

namespace QLHV.Infrastructure.Sync.Rt03;

public sealed class Rt03ReviewedRetainedRereviewService :
    IRt03ReviewedRetainedRereviewService
{
    private readonly Rt01aOtoDriftEvidenceReader _reader;
    private readonly ICsdtConnectionProfileRepository _profiles;
    private readonly IConnectionPasswordProtector _passwordProtector;
    private readonly IConnectionSettingsProvider _connections;
    private readonly SyncOptions _syncOptions;

    public Rt03ReviewedRetainedRereviewService(
        Rt01aOtoDriftEvidenceReader reader,
        ICsdtConnectionProfileRepository profiles,
        IConnectionPasswordProtector passwordProtector,
        IConnectionSettingsProvider connections,
        IOptions<SyncOptions> syncOptions)
    {
        _reader = reader;
        _profiles = profiles;
        _passwordProtector = passwordProtector;
        _connections = connections;
        _syncOptions = syncOptions.Value;
    }

    public async Task<Rt03ReviewedRetainedRereviewResult> ExecuteAsync(
        Rt03ReviewedRetainedRereviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var versions = request.ReviewedEventVersions
            .Distinct()
            .Order()
            .ToArray();
        if (!string.Equals(request.SourceProfileCode, Rt03Profiles.Oto,
                StringComparison.Ordinal) ||
            versions.Length != 2 || versions.Any(version => version <= 0))
        {
            throw new Rt03SafetyException(
                Rt03Errors.ConfigurationRejected,
                "V9 operator re-review requires one exact OTO request with two event versions.");
        }

        var route = Rt01ShadowRouteCatalog.Ordered.Single(item =>
            item.SourceProfileCode == request.SourceProfileCode);
        var sourceConnectionString = await ResolveSourceAsync(route, cancellationToken);
        var sourceBefore = await ReadSourceCapabilityAsync(
            sourceConnectionString, cancellationToken);
        if (versions.Any(version => version - 1 < sourceBefore.MinimumValidVersion) ||
            versions.Any(version => version > sourceBefore.CurrentVersion))
        {
            throw new Rt03SafetyException(
                Rt03Errors.ChangeTrackingWindowRejected,
                "The reviewed source events are outside the fresh Change Tracking window.");
        }

        var raw = await _reader.ReadAsync(route, cancellationToken);
        var sourceAfterRead = await ReadSourceCapabilityAsync(
            sourceConnectionString, cancellationToken);
        if (sourceBefore != sourceAfterRead)
        {
            throw new Rt03SafetyException(
                Rt03Errors.SourceChangedDuringPlan,
                "Source CT state changed while building the V9 re-review evidence.");
        }

        var sourceEvents = await ReadSourceEventsAsync(
            sourceConnectionString, versions, sourceBefore.CurrentVersion, cancellationToken);
        if (sourceEvents.Length != versions.Length ||
            sourceEvents.Select(item => item.ChangeVersion).Distinct().Count() != versions.Length ||
            sourceEvents.Any(item => item.Operation != "U" ||
                item.ChangedColumns != "NgayThuNhanAnh"))
        {
            throw new Rt03SafetyException(
                Rt03Errors.UnsupportedDrift,
                "The requested legacy events are not exact single-field photo updates.");
        }

        var targetSettings = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!targetSettings.IsUsable || string.IsNullOrWhiteSpace(targetSettings.ConnectionString))
        {
            throw new Rt03SafetyException(
                Rt03Errors.ProductionIdentityRejected,
                "QLHV_APP connection is unavailable for V9 re-review.");
        }

        await using var targetConnection = new SqlConnection(targetSettings.ConnectionString);
        await targetConnection.OpenAsync(cancellationToken);
        var checkpoint = await targetConnection.QuerySingleAsync<CheckpointRow>(
            new CommandDefinition(
                CheckpointSql,
                new { SourceProfileCode = request.SourceProfileCode },
                commandTimeout: _syncOptions.TimeoutSeconds,
                cancellationToken: cancellationToken));
        if (checkpoint.SourceVersion < versions.Max())
        {
            throw new Rt03SafetyException(
                Rt03Errors.CheckpointConflict,
                "Checkpoint has not committed through both reviewed source events.");
        }

        var planned = new List<PlannedRereview>(versions.Length);
        foreach (var sourceEvent in sourceEvents.OrderBy(item => item.ChangeVersion))
        {
            var legacyRows = (await targetConnection.QueryAsync<LegacyReviewRow>(
                new CommandDefinition(
                    LegacyReviewSql,
                    new
                    {
                        SourceProfileCode = request.SourceProfileCode,
                        SourceChangeTrackingVersion = sourceEvent.ChangeVersion,
                    },
                    commandTimeout: _syncOptions.TimeoutSeconds,
                    cancellationToken: cancellationToken))).ToArray();
            if (legacyRows.Length != 1 || !legacyRows[0].MarkerCheckpointAtomic)
            {
                throw new Rt03SafetyException(
                    Rt03Errors.CheckpointConflict,
                    "Legacy review/marker evidence is missing or ambiguous.");
            }

            var sourceRows = raw.MappedSourceRows.Where(row =>
                string.Equals(row.SourceMaDK.Trim(), sourceEvent.SourceMaDk.Trim(),
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            var targetRows = raw.TargetRows.Where(row =>
                !row.IsDeleted &&
                string.Equals(row.SourceProfileCode, request.SourceProfileCode,
                    StringComparison.Ordinal) &&
                string.Equals(row.SourceMaDK?.Trim(), sourceEvent.SourceMaDk.Trim(),
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            if (sourceRows.Length != 1 || targetRows.Length != 1)
            {
                throw new Rt03SafetyException(
                    Rt03Errors.TargetDrift,
                    "Fresh V9 re-review does not have one exact live source/target identity.");
            }

            var source = sourceRows[0];
            var target = targetRows[0];
            var fieldSet = Rt03ReviewedRetainedFingerprints.CurrentFieldSet(source, target);
            if (!string.Equals(fieldSet, "NgayThuNhanAnh", StringComparison.Ordinal))
            {
                throw new Rt03SafetyException(
                    Rt03Errors.UnsupportedDrift,
                    "Fresh source/target divergence is not exact NgayThuNhanAnh-only drift.");
            }

            var businessIdentityHash =
                Rt03ReviewedRetainedFingerprints.SourceBusinessIdentity(
                    request.SourceProfileCode, source.SourceMaDK);
            var sourceFingerprint = Rt03ReviewedRetainedFingerprints.Source(source);
            var targetFingerprint = Rt03ReviewedRetainedFingerprints.Target(target);
            var ownershipFingerprint = Rt03ReviewedRetainedFingerprints.Ownership(target);
            var evidenceHash = Rt03ReviewedRetainedFingerprints.Evidence(
                request.SourceProfileCode,
                businessIdentityHash,
                target.HocVienId,
                fieldSet,
                sourceEvent.ChangeVersion,
                sourceBefore.CurrentVersion,
                sourceFingerprint,
                targetFingerprint,
                ownershipFingerprint);
            planned.Add(new PlannedRereview(
                legacyRows[0],
                sourceEvent.ChangeVersion,
                businessIdentityHash,
                target.HocVienId,
                fieldSet,
                sourceFingerprint,
                targetFingerprint,
                ownershipFingerprint,
                evidenceHash,
                "RT03-V9-" + evidenceHash[..16]));
        }

        if (planned.Select(item => item.BusinessIdentityHash)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != planned.Count ||
            planned.Select(item => item.TargetIdentity).Distinct().Count() != planned.Count)
        {
            throw new Rt03SafetyException(
                Rt03Errors.TargetDrift,
                "The two V9 re-reviews do not identify two distinct source/target rows.");
        }

        if (!request.Commit)
        {
            var validatedAtUtc = await targetConnection.ExecuteScalarAsync<DateTime>(
                new CommandDefinition(
                    "SELECT CONVERT(datetime2(7),SYSUTCDATETIME());",
                    commandTimeout: _syncOptions.TimeoutSeconds,
                    cancellationToken: cancellationToken));
            return new Rt03ReviewedRetainedRereviewResult(
                request.SourceProfileCode,
                sourceBefore.CurrentVersion,
                0,
                planned.Select(item => item.DiagnosticId).Order(StringComparer.Ordinal)
                    .ToArray(),
                Rt03ReviewedRetainedContract.Version,
                validatedAtUtc)
            {
                CommitRequested = false,
                ValidationPassed = true,
            };
        }

        var completedAtUtc = default(DateTime);
        await using (var transaction =
                     (SqlTransaction)await targetConnection.BeginTransactionAsync(
                         System.Data.IsolationLevel.Serializable, cancellationToken))
        {
            try
            {
                await targetConnection.ExecuteAsync(new CommandDefinition(
                    AcquireLocksSql,
                    new { ProfileLock = $"QLHV:RT03:{request.SourceProfileCode}" },
                    transaction,
                    _syncOptions.TimeoutSeconds,
                    cancellationToken: cancellationToken));
                var currentCheckpoint = await targetConnection.QuerySingleAsync<long>(
                    new CommandDefinition(
                        CheckpointForUpdateSql,
                        new { SourceProfileCode = request.SourceProfileCode },
                        transaction,
                        _syncOptions.TimeoutSeconds,
                        cancellationToken: cancellationToken));
                if (currentCheckpoint != checkpoint.SourceVersion)
                {
                    throw new Rt03SafetyException(
                        Rt03Errors.CheckpointConflict,
                        "Checkpoint changed before the V9 re-review transaction.");
                }

                var sourceBeforeCommit = await ReadSourceCapabilityAsync(
                    sourceConnectionString, cancellationToken);
                if (sourceBeforeCommit != sourceBefore)
                {
                    throw new Rt03SafetyException(
                        Rt03Errors.SourceChangedDuringPlan,
                        "Source CT state changed before the V9 re-review commit.");
                }

                completedAtUtc = await targetConnection.ExecuteScalarAsync<DateTime>(
                    new CommandDefinition(
                        "SELECT CONVERT(datetime2(7),SYSUTCDATETIME());",
                        transaction: transaction,
                        commandTimeout: _syncOptions.TimeoutSeconds,
                        cancellationToken: cancellationToken));
                foreach (var item in planned)
                {
                    var affected = await targetConnection.ExecuteAsync(new CommandDefinition(
                        InsertRereviewSql,
                        new
                        {
                            item.Legacy.CycleId,
                            item.Legacy.PlanHash,
                            CandidateId = $"PHOTO-REREVIEW-V9-{item.ReviewedEventVersion}-" +
                                item.EvidenceHash[..12],
                            SourceProfileCode = request.SourceProfileCode,
                            IdentityHmac = "RT03-V9-SHA256:" + item.BusinessIdentityHash,
                            Classification = Rt03ReviewedRetainedContract.PhotoClassification,
                            RollbackImageHash = item.EvidenceHash,
                            CompletedAtUtc = completedAtUtc,
                            EvidenceContractVersion = Rt03ReviewedRetainedContract.Version,
                            DomainCode = Rt03ReviewedRetainedContract.DomainLearner,
                            SourceBusinessIdentityHash = item.BusinessIdentityHash,
                            item.TargetIdentity,
                            ReviewedFieldSet = item.FieldSet,
                            item.ReviewedEventVersion,
                            EvidenceAnchorVersion = sourceBefore.CurrentVersion,
                            item.SourceFingerprint,
                            item.TargetFingerprint,
                            QlhvOwnedFingerprint = item.OwnershipFingerprint,
                            ReviewState = Rt03ReviewedRetainedContract.ActiveState,
                            SupersedesManualReviewId = item.Legacy.ManualReviewId,
                            DecisionEvidenceHash = item.EvidenceHash,
                            item.DiagnosticId,
                        },
                        transaction,
                        _syncOptions.TimeoutSeconds,
                        cancellationToken: cancellationToken));
                    if (affected != 1)
                    {
                        throw new Rt03SafetyException(
                            Rt03Errors.TargetDrift,
                            "V9 re-review insertion was not exactly one immutable row.");
                    }
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        return new Rt03ReviewedRetainedRereviewResult(
            request.SourceProfileCode,
            sourceBefore.CurrentVersion,
            planned.Count,
            planned.Select(item => item.DiagnosticId).Order(StringComparer.Ordinal).ToArray(),
            Rt03ReviewedRetainedContract.Version,
            completedAtUtc)
        {
            CommitRequested = true,
            ValidationPassed = true,
        };
    }

    private async Task<string> ResolveSourceAsync(
        Rt01ShadowRoute route,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetByCodeAsync(route.SourceProfileCode, cancellationToken);
        if (profile is null || !profile.IsActive ||
            string.IsNullOrWhiteSpace(profile.ServerName) ||
            profile.DatabaseName != route.SourceDatabaseName)
        {
            throw new Rt03SafetyException(
                Rt03Errors.ProductionIdentityRejected,
                "The V9 source profile does not resolve to the exact live database.");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.ServerName,
            InitialCatalog = profile.DatabaseName,
            ConnectTimeout = Math.Clamp(_syncOptions.TimeoutSeconds, 5, 30),
            TrustServerCertificate = true,
            MultipleActiveResultSets = false,
        };
        if (string.Equals(profile.AuthMode, "SqlLogin", StringComparison.OrdinalIgnoreCase))
        {
            if (!profile.IsPasswordConfigured || profile.PasswordCipherText is null ||
                string.IsNullOrWhiteSpace(profile.UserName) || !_passwordProtector.IsAvailable)
            {
                throw new Rt03SafetyException(
                    Rt03Errors.ProductionIdentityRejected,
                    "Live source credentials are unavailable.");
            }

            builder.IntegratedSecurity = false;
            builder.UserID = profile.UserName;
            builder.Password = _passwordProtector.Unprotect(profile.PasswordCipherText);
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    private async Task<SourceCapability> ReadSourceCapabilityAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleAsync<SourceCapability>(new CommandDefinition(
            SourceCapabilitySql,
            commandTimeout: _syncOptions.TimeoutSeconds,
            cancellationToken: cancellationToken));
    }

    private async Task<SourceEventRow[]> ReadSourceEventsAsync(
        string connectionString,
        IReadOnlyCollection<long> versions,
        long currentVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var rows = new List<SourceEventRow>();
        foreach (var version in versions)
        {
            var result = (await connection.QueryAsync<SourceEventRow>(new CommandDefinition(
                ExactSourceEventSql,
                new
                {
                    FromVersion = version - 1,
                    ExactVersion = version,
                    CurrentVersion = currentVersion,
                },
                commandTimeout: _syncOptions.TimeoutSeconds,
                cancellationToken: cancellationToken))).ToArray();
            rows.AddRange(result);
        }

        return rows.ToArray();
    }

    private sealed record SourceCapability(
        long CurrentVersion,
        long MinimumValidVersion);

    private sealed class SourceEventRow
    {
        public long ChangeVersion { get; init; }
        public string Operation { get; init; } = string.Empty;
        public string SourceMaDk { get; init; } = string.Empty;
        public string ChangedColumns { get; init; } = string.Empty;
    }

    private sealed class LegacyReviewRow
    {
        public long ManualReviewId { get; init; }
        public Guid CycleId { get; init; }
        public string PlanHash { get; init; } = string.Empty;
        public bool MarkerCheckpointAtomic { get; init; }
    }

    private sealed class CheckpointRow
    {
        public long SourceVersion { get; init; }
    }

    private sealed record PlannedRereview(
        LegacyReviewRow Legacy,
        long ReviewedEventVersion,
        string BusinessIdentityHash,
        long TargetIdentity,
        string FieldSet,
        string SourceFingerprint,
        string TargetFingerprint,
        string OwnershipFingerprint,
        string EvidenceHash,
        string DiagnosticId);

    private const string SourceCapabilitySql = """
        SELECT CONVERT(bigint,CHANGE_TRACKING_CURRENT_VERSION()) AS CurrentVersion,
               CONVERT(bigint,CHANGE_TRACKING_MIN_VALID_VERSION(
                   OBJECT_ID(N'dbo.NguoiLX_HoSo'))) AS MinimumValidVersion;
        """;

    private const string ExactSourceEventSql = """
        SELECT CONVERT(bigint,changeRow.SYS_CHANGE_VERSION) AS ChangeVersion,
               CONVERT(nvarchar(1),changeRow.SYS_CHANGE_OPERATION) AS Operation,
               CONVERT(nvarchar(200),changeRow.MaDK) AS SourceMaDk,
               NULLIF(CONCAT_WS(N',',
                   CASE WHEN CHANGE_TRACKING_IS_COLUMN_IN_MASK(
                       COLUMNPROPERTY(OBJECT_ID(N'dbo.NguoiLX_HoSo'),N'TT_XuLy',N'ColumnId'),
                       changeRow.SYS_CHANGE_COLUMNS)=1 THEN N'TT_XuLy' END,
                   CASE WHEN CHANGE_TRACKING_IS_COLUMN_IN_MASK(
                       COLUMNPROPERTY(OBJECT_ID(N'dbo.NguoiLX_HoSo'),N'DuongDanAnh',N'ColumnId'),
                       changeRow.SYS_CHANGE_COLUMNS)=1 THEN N'DuongDanAnh' END,
                   CASE WHEN CHANGE_TRACKING_IS_COLUMN_IN_MASK(
                       COLUMNPROPERTY(OBJECT_ID(N'dbo.NguoiLX_HoSo'),N'ChatLuongAnh',N'ColumnId'),
                       changeRow.SYS_CHANGE_COLUMNS)=1 THEN N'ChatLuongAnh' END,
                   CASE WHEN CHANGE_TRACKING_IS_COLUMN_IN_MASK(
                       COLUMNPROPERTY(OBJECT_ID(N'dbo.NguoiLX_HoSo'),N'NgayThuNhanAnh',N'ColumnId'),
                       changeRow.SYS_CHANGE_COLUMNS)=1 THEN N'NgayThuNhanAnh' END,
                   CASE WHEN CHANGE_TRACKING_IS_COLUMN_IN_MASK(
                       COLUMNPROPERTY(OBJECT_ID(N'dbo.NguoiLX_HoSo'),N'NguoiThuNhanAnh',N'ColumnId'),
                       changeRow.SYS_CHANGE_COLUMNS)=1 THEN N'NguoiThuNhanAnh' END),N'')
                   AS ChangedColumns
        FROM CHANGETABLE(CHANGES dbo.NguoiLX_HoSo,@FromVersion) changeRow
        WHERE changeRow.SYS_CHANGE_VERSION=@ExactVersion
          AND changeRow.SYS_CHANGE_VERSION<=@CurrentVersion;
        """;

    private const string CheckpointSql = """
        SELECT SourceChangeTrackingVersion AS SourceVersion
        FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
        WHERE SourceProfileCode=@SourceProfileCode
          AND Mode=N'DIRECT_REALTIME_APPLY'
          AND EnvironmentId=N'PRODUCTION';
        """;

    private const string LegacyReviewSql = """
        SELECT review.ManualReviewId,review.CycleId,review.PlanHash,
               CONVERT(bit,CASE WHEN marker.CycleId IS NOT NULL THEN 1 ELSE 0 END)
                   AS MarkerCheckpointAtomic
        FROM dbo.App_QlhvDirectRealtimeManualReview review
        LEFT JOIN dbo.App_QlhvDirectRealtimeApplyMarker marker
          ON marker.CycleId=review.CycleId
         AND marker.SourceProfileCode=review.SourceProfileCode
         AND marker.PlanHash=review.PlanHash
         AND marker.SourceChangeTrackingVersion=@SourceChangeTrackingVersion
         AND marker.RetainedRows=1
        WHERE review.SourceProfileCode=@SourceProfileCode
          AND review.EvidenceContractVersion IS NULL
          AND review.Classification=N'MULTI_FIELD_PHOTO_DRIFT'
          AND review.TargetRetainedActive=1
          AND review.TargetMutated=0
          AND review.CandidateId=CONCAT(N'PHOTO-CT-',@SourceChangeTrackingVersion,N'-1');
        """;

    private const string AcquireLocksSql = """
        DECLARE @LockResult int;
        EXEC @LockResult=sys.sp_getapplock
            @Resource=N'QLHV:RT03:GLOBAL',@LockMode=N'Exclusive',
            @LockOwner=N'Transaction',@LockTimeout=0;
        IF @LockResult<0 THROW 527720,'RT03_V9_GLOBAL_LOCK_UNAVAILABLE',1;
        EXEC @LockResult=sys.sp_getapplock
            @Resource=@ProfileLock,@LockMode=N'Exclusive',
            @LockOwner=N'Transaction',@LockTimeout=0;
        IF @LockResult<0 THROW 527721,'RT03_V9_PROFILE_LOCK_UNAVAILABLE',1;
        """;

    private const string CheckpointForUpdateSql = """
        SELECT SourceChangeTrackingVersion
        FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint WITH(UPDLOCK,HOLDLOCK)
        WHERE SourceProfileCode=@SourceProfileCode
          AND Mode=N'DIRECT_REALTIME_APPLY'
          AND EnvironmentId=N'PRODUCTION';
        """;

    private const string InsertRereviewSql = """
        IF EXISTS
        (
            SELECT 1 FROM dbo.App_QlhvDirectRealtimeManualReview
            WHERE DecisionEvidenceHash=@DecisionEvidenceHash
               OR (EvidenceContractVersion=@EvidenceContractVersion
                   AND SupersedesManualReviewId=@SupersedesManualReviewId)
        ) THROW 527722,'RT03_V9_REREVIEW_ALREADY_EXISTS',1;

        INSERT dbo.App_QlhvDirectRealtimeManualReview
        (
            CycleId,PlanHash,CandidateId,SourceProfileCode,IdentityHmac,
            Classification,RollbackImageHash,TargetRetainedActive,
            TargetMutated,CreatedAtUtc,EvidenceContractVersion,DomainCode,
            SourceBusinessIdentityHash,TargetIdentity,ReviewedFieldSet,
            ReviewedEventVersion,EvidenceAnchorVersion,SourceFingerprint,
            TargetFingerprint,QlhvOwnedFingerprint,ReviewState,
            SupersedesManualReviewId,DecisionEvidenceHash,DiagnosticId
        )
        VALUES
        (
            @CycleId,@PlanHash,@CandidateId,@SourceProfileCode,@IdentityHmac,
            @Classification,@RollbackImageHash,1,0,@CompletedAtUtc,
            @EvidenceContractVersion,@DomainCode,@SourceBusinessIdentityHash,
            @TargetIdentity,@ReviewedFieldSet,@ReviewedEventVersion,
            @EvidenceAnchorVersion,@SourceFingerprint,@TargetFingerprint,
            @QlhvOwnedFingerprint,@ReviewState,@SupersedesManualReviewId,
            @DecisionEvidenceHash,@DiagnosticId
        );
        """;
}

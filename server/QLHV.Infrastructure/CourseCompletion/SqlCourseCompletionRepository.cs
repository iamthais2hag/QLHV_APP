using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using QLHV.Application.CourseCompletion;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.VehicleRealtime;

namespace QLHV.Infrastructure.CourseCompletion;

/// <summary>
/// Marker-only repository. Source connections execute SELECT batches only;
/// every durable write is confined to one QLHV_APP transaction.
/// </summary>
public sealed class SqlCourseCompletionRepository : ICourseCompletionRepository
{
    private const int CommandTimeoutSeconds = 30;
    private readonly IConfiguration _configuration;
    private readonly IConnectionSettingsProvider _connections;
    private readonly CourseCompletionCanonicalSnapshotBuilder _builder;

    public SqlCourseCompletionRepository(
        IConfiguration configuration,
        IConnectionSettingsProvider connections,
        CourseCompletionCanonicalSnapshotBuilder builder)
    {
        _configuration = configuration;
        _connections = connections;
        _builder = builder;
    }

    public async Task<CourseCompletionSourceScope> ReadSourceScopeAsync(
        long courseId,
        string? requiredProfile,
        CancellationToken cancellationToken)
    {
        var identity = await ResolveCourseIdentityAsync(courseId, requiredProfile, cancellationToken);
        return await ReadStableSourceScopeAsync(identity, cancellationToken);
    }

    public async Task<CourseCompletionCourseIdentity> ReadCourseIdentityAsync(
        long courseId,
        CancellationToken cancellationToken)
    {
        var identity = await ResolveCourseIdentityAsync(courseId, null, cancellationToken);
        return new(identity.KhoaHocId, identity.SourceProfileCode, identity.SourceCourseKey);
    }

    public async Task<CourseCompletionStoredMarker?> ReadMarkerAsync(
        long courseId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenQlhvAsync(cancellationToken);
        return await ReadMarkerAsync(connection, null, courseId, cancellationToken);
    }

    public async Task<CourseCompletionConfirmResult> ConfirmAsync(
        CourseCompletionConfirmCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var connection = await OpenQlhvAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await connection.ExecuteAsync(Command(
                "SET XACT_ABORT ON; SET NOCOUNT ON;",
                transaction: transaction,
                cancellationToken: cancellationToken));
            var lockResult = await connection.ExecuteScalarAsync<int>(Command(
                """
                DECLARE @Result int;
                EXEC @Result = sys.sp_getapplock
                    @Resource=@Resource,
                    @LockMode=N'Exclusive',
                    @LockOwner=N'Transaction',
                    @LockTimeout=15000;
                SELECT @Result;
                """,
                new { Resource = $"CourseCompletion:{command.Preview.SourceProfileCode}:{command.Preview.SourceCourseKey}" },
                transaction,
                cancellationToken));
            if (lockResult < 0)
                throw Conflict(CourseCompletionCodes.Conflict, "Không lấy được khóa xác nhận hoàn thành khóa học.");

            var replay = await ReadOperationReplayAsync(connection, transaction, command, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            var identity = await ResolveCourseIdentityAsync(
                connection, transaction, command.Preview.KhoaHocId,
                command.Preview.SourceProfileCode, cancellationToken);
            if (!string.Equals(identity.SourceCourseKey, command.Preview.SourceCourseKey, StringComparison.Ordinal))
                throw Conflict(CourseCompletionCodes.Conflict, "Identity khóa học đã thay đổi sau preview.");

            var current = _builder.Build(await ReadStableSourceScopeAsync(identity, cancellationToken));
            if (!current.CanConfirm ||
                current.LearnerCount != command.Preview.LearnerCount ||
                !string.Equals(current.SnapshotHash, command.Preview.SnapshotHash, StringComparison.Ordinal))
                throw Conflict(CourseCompletionCodes.Conflict, "Nguồn đã thay đổi sau preview; không có dữ liệu nào được ghi.");

            var marker = await ReadMarkerAsync(connection, transaction, identity.KhoaHocId, cancellationToken);
            if (marker is not null)
            {
                if (!string.Equals(marker.SourceSnapshotHash, current.SnapshotHash, StringComparison.Ordinal) ||
                    marker.LearnerCount != current.LearnerCount)
                    throw Conflict(CourseCompletionCodes.CorrectionRequired, "Marker hiện có khác snapshot nguồn; cần correction workflow riêng.");

                var noChange = await InsertNoChangeOperationAsync(
                    connection, transaction, command, marker, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return noChange;
            }

            var completed = await InsertCompletionAsync(
                connection, transaction, identity, current, command, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return completed;
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<CourseIdentity> ResolveCourseIdentityAsync(
        long courseId,
        string? requiredProfile,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenQlhvAsync(cancellationToken);
        return await ResolveCourseIdentityAsync(connection, null, courseId, requiredProfile, cancellationToken);
    }

    private static async Task<CourseIdentity> ResolveCourseIdentityAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        long courseId,
        string? requiredProfile,
        CancellationToken cancellationToken)
    {
        var rows = (await connection.QueryAsync<CourseIdentity>(Command(
            """
            SELECT KhoaHocId,
                   UPPER(LTRIM(RTRIM(SourceProfileCode))) AS SourceProfileCode,
                   LTRIM(RTRIM(COALESCE(NULLIF(SourceMaKhoaHoc,N''),MaKhoa))) AS SourceCourseKey
            FROM dbo.App_KhoaHoc
            WHERE KhoaHocId=@CourseId AND IsDeleted=0;
            """,
            new { CourseId = courseId }, transaction, cancellationToken))).ToArray();
        if (rows.Length != 1)
            throw new CourseCompletionDomainException(CourseCompletionCodes.CourseNotFound, "Không tìm thấy exact khóa học QLHV.", 404);
        var row = rows[0];
        if (row.SourceProfileCode is not (VehicleRealtimeProfiles.Oto or VehicleRealtimeProfiles.Moto) ||
            string.IsNullOrWhiteSpace(row.SourceCourseKey) ||
            (!string.IsNullOrWhiteSpace(requiredProfile) &&
             !string.Equals(row.SourceProfileCode, requiredProfile, StringComparison.Ordinal)))
            throw new CourseCompletionDomainException(CourseCompletionCodes.AmbiguousIdentity, "Không xác định được exact source profile/course identity.", 409);
        return row;
    }

    private async Task<CourseCompletionSourceScope> ReadStableSourceScopeAsync(
        CourseIdentity identity,
        CancellationToken cancellationToken)
    {
        var route = await ResolveSourceRouteAsync(identity.SourceProfileCode, cancellationToken);
        var first = await ReadV2ScopeAsync(identity, route, cancellationToken);
        var firstV1 = await ReadV1ScopeAsync(identity, route, cancellationToken);
        var second = await ReadV2ScopeAsync(identity, route, cancellationToken);
        var secondV1 = await ReadV1ScopeAsync(identity, route, cancellationToken);
        if (!string.Equals(RawFingerprint(first), RawFingerprint(second), StringComparison.Ordinal) ||
            !string.Equals(RawV1Fingerprint(firstV1), RawV1Fingerprint(secondV1), StringComparison.Ordinal))
            throw Conflict(CourseCompletionCodes.Conflict, "Source thay đổi trong lúc chụp snapshot; hãy preview lại.");

        var v1ByKey = secondV1.Learners
            .GroupBy(x => CourseCompletionCanonicalSnapshotBuilder.Normalize(x.RegistrationCode), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var merged = new List<CourseCompletionLearnerSource>(second.Learners.Count + secondV1.Learners.Count);
        foreach (var learner in second.Learners)
        {
            var key = CourseCompletionCanonicalSnapshotBuilder.Normalize(learner.RegistrationCode);
            v1ByKey.TryGetValue(key, out var candidates);
            var target = candidates?.Length == 1 ? candidates[0] : null;
            merged.Add(learner with
            {
                V1Status = target?.V1Status,
                HasReportII = target?.HasReportII ?? false,
                HasExamLifecycle = target?.HasExamLifecycle ?? false,
                HasLicense = target?.HasLicense ?? false,
                // More than one V1 row for a V2 identity is never collapsed silently.
                // The shared builder turns this into AMBIGUOUS_IDENTITY and confirm fails closed.
                IsV1Orphan = candidates is { Length: > 1 },
            });
            v1ByKey.Remove(key);
        }
        foreach (var orphan in v1ByKey.Values.SelectMany(x => x))
            merged.Add(orphan with { IsV1Orphan = true });

        var sourceDiagnostics = second.Diagnostics.ToList();
        if (secondV1.CourseCount == 0)
            sourceDiagnostics.Add("V1_COURSE_MISSING");
        return new CourseCompletionSourceScope(second.Course, merged, sourceDiagnostics);
    }

    private async Task<RawScope> ReadV2ScopeAsync(
        CourseIdentity identity,
        SourceRoute route,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(route.V2ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Snapshot, cancellationToken);
        try
        {
            using var grid = await connection.QueryMultipleAsync(Command(V2ReadSql, new
            {
                CourseKey = identity.SourceCourseKey,
                route.ExpectedMaCsdt,
            }, transaction, cancellationToken));
            var database = await grid.ReadSingleAsync<DatabaseIdentity>();
            ValidateDatabase(database, route.V2DatabaseName, route.ExpectedV2DatabaseGuid);
            var courses = (await grid.ReadAsync<SourceCourseRow>()).ToArray();
            if (courses.Length != 1)
                throw new CourseCompletionDomainException(CourseCompletionCodes.AmbiguousIdentity, "Không tìm thấy exact source course hoặc source course bị ambiguous.", 409);
            var learners = (await grid.ReadAsync<CourseCompletionLearnerDbRow>())
                .Select(row => row.ToSource())
                .ToArray();
            var diagnostics = await grid.ReadSingleAsync<SourceDiagnosticRow>();
            await transaction.CommitAsync(cancellationToken);
            return new RawScope(ToCourse(identity, courses[0], route, diagnostics), learners,
                diagnostics.ToWarnings());
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static CourseCompletionCourseSource ToCourse(
        CourseIdentity identity,
        SourceCourseRow row,
        SourceRoute route,
        SourceDiagnosticRow diagnostics) => new(
            identity.KhoaHocId,
            identity.SourceProfileCode,
            identity.SourceCourseKey,
            row.MaCsdt,
            row.MaSoGtvt,
            string.IsNullOrWhiteSpace(row.TrainingClass) ? row.LicenseClass : row.TrainingClass,
            row.TrainingForm,
            row.StartDate is null ? null : DateOnly.FromDateTime(row.StartDate.Value),
            row.EndDate is null ? null : DateOnly.FromDateTime(row.EndDate.Value),
            diagnostics.ReportICount > 0,
            diagnostics.TeacherCount > 0,
            diagnostics.VehicleCount > 0,
            !string.IsNullOrWhiteSpace(row.TrainingForm) &&
            (!string.IsNullOrWhiteSpace(row.TrainingClass) || !string.IsNullOrWhiteSpace(row.LicenseClass)));

    private async Task<RawV1Scope> ReadV1ScopeAsync(
        CourseIdentity identity,
        SourceRoute route,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(route.V1ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            using var grid = await connection.QueryMultipleAsync(Command(V1ReadSql,
                new { CourseKey = identity.SourceCourseKey, route.ExpectedMaCsdt },
                transaction, cancellationToken));
            var database = await grid.ReadSingleAsync<DatabaseIdentity>();
            ValidateDatabase(database, route.V1DatabaseName, null);
            var courseCount = await grid.ReadSingleAsync<int>();
            if (courseCount > 1)
                throw new CourseCompletionDomainException(CourseCompletionCodes.AmbiguousIdentity, "V1 course identity không exact.", 409);
            var learners = (await grid.ReadAsync<CourseCompletionLearnerDbRow>())
                .Select(row => row.ToSource())
                .ToArray();
            if (courseCount == 0 && learners.Length != 0)
                throw new CourseCompletionDomainException(
                    CourseCompletionCodes.AmbiguousIdentity,
                    "V1 learners exist without an exact V1 course identity.", 409);
            await transaction.CommitAsync(cancellationToken);
            return new RawV1Scope(courseCount, learners);
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<SourceRoute> ResolveSourceRouteAsync(
        string profile,
        CancellationToken cancellationToken)
    {
        var route = VehicleRealtimeRouteCatalog.GetRequired(profile);
        var v1Database = profile == VehicleRealtimeProfiles.Oto ? "CSDL_OTO_V1" : "CSDL_MOTO_V1";
        return new SourceRoute(
            route.SourceDatabaseName,
            v1Database,
            route.ExpectedProductionDatabaseGuid,
            route.ExpectedMaCsdt,
            await ResolveConnectionAsync(
                route.SourceDatabaseName, route.SourceDatabaseName, SourceSystem.V2,
                ["CSDL_OTO", "CSDL_MOTO"], cancellationToken),
            await ResolveConnectionAsync(
                v1Database, v1Database, SourceSystem.V1,
                ["CSDL_OTO_V1", "CSDL_MOTO_V1"], cancellationToken));
    }

    private async Task<string> ResolveConnectionAsync(
        string profileConnectionName,
        string expectedDatabase,
        SourceSystem fallbackSystem,
        IReadOnlyCollection<string> allowedFallbackDatabases,
        CancellationToken cancellationToken)
    {
        var value = _configuration.GetConnectionString(profileConnectionName);
        if (string.IsNullOrWhiteSpace(value) || value.Contains("__", StringComparison.Ordinal))
        {
            var fallback = await _connections.GetSourceConnectionAsync(fallbackSystem, cancellationToken);
            if (!fallback.IsUsable || string.IsNullOrWhiteSpace(fallback.ConnectionString))
                throw new CourseCompletionDomainException(
                    CourseCompletionCodes.Blocked,
                    $"Connection {profileConnectionName} và authority {fallbackSystem} chưa được cấu hình.",
                    503);
            var fallbackBuilder = new SqlConnectionStringBuilder(fallback.ConnectionString);
            if (string.IsNullOrWhiteSpace(fallbackBuilder.DataSource) ||
                !allowedFallbackDatabases.Any(database =>
                    string.Equals(database, fallbackBuilder.InitialCatalog, StringComparison.OrdinalIgnoreCase)))
                throw new CourseCompletionDomainException(
                    CourseCompletionCodes.AmbiguousIdentity,
                    "Source connection authority không thuộc fixed database-family allowlist.",
                    409);
            fallbackBuilder.InitialCatalog = expectedDatabase;
            value = fallbackBuilder.ConnectionString;
        }
        var builder = new SqlConnectionStringBuilder(value);
        if (!string.Equals(builder.InitialCatalog, expectedDatabase, StringComparison.OrdinalIgnoreCase))
            throw new CourseCompletionDomainException(CourseCompletionCodes.AmbiguousIdentity, "Source connection database không khớp fixed allowlist.", 409);
        builder.ApplicationIntent = ApplicationIntent.ReadOnly;
        return builder.ConnectionString;
    }

    private async Task<SqlConnection> OpenQlhvAsync(CancellationToken cancellationToken)
    {
        var resolved = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!resolved.IsUsable || string.IsNullOrWhiteSpace(resolved.ConnectionString))
            throw new CourseCompletionDomainException(CourseCompletionCodes.TimeAuthorityBlocked, "QLHV_APP database không sẵn sàng.", 503);
        var builder = new SqlConnectionStringBuilder(resolved.ConnectionString);
        if (!string.Equals(builder.InitialCatalog, "QLHV_APP", StringComparison.OrdinalIgnoreCase))
            throw new CourseCompletionDomainException(CourseCompletionCodes.AmbiguousIdentity, "Target database không phải QLHV_APP.", 409);
        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<CourseCompletionStoredMarker?> ReadMarkerAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        long courseId,
        CancellationToken cancellationToken)
    {
        var rows = (await connection.QueryAsync<MarkerRow>(Command(
            """
            SELECT CourseCompletionId,KhoaHocId,SourceProfileCode,SourceCourseKey,
                   ContractVersion,CONVERT(datetime2(7),CompletionBusinessDate) AS CompletionBusinessDate,
                   SourceSnapshotHash,LearnerCount,
                   CompletedAtUtc,CompletedBy,CompletionReason
            FROM dbo.App_CourseCompletion
            WHERE KhoaHocId=@CourseId;
            """,
            new { CourseId = courseId }, transaction, cancellationToken))).ToArray();
        if (rows.Length == 0) return null;
        if (rows.Length != 1)
            throw Conflict(CourseCompletionCodes.DuplicateIdentity, "Completion marker bị duplicate.");
        var learners = (await connection.QueryAsync<CourseCompletionStoredLearner>(Command(
            """
            SELECT s.ProtectedLearnerIdentity AS ProtectedIdentity,s.TT_XuLy AS Status,
                   s.LearnerClassification AS Classification,
                   s.ResultCompletenessClassification AS ResultCompleteness,
                   s.CanonicalLearnerRowHash AS CanonicalRowHash
            FROM dbo.App_CourseCompletionLearnerSnapshot s
            INNER JOIN dbo.App_CourseCompletion c ON c.CourseCompletionId=s.CourseCompletionId
            WHERE c.KhoaHocId=@CourseId
            ORDER BY s.ProtectedLearnerIdentity;
            """,
            new { CourseId = courseId }, transaction, cancellationToken))).ToArray();
        var row = rows[0];
        return new CourseCompletionStoredMarker(
            row.CourseCompletionId, row.KhoaHocId, row.SourceProfileCode,
            row.SourceCourseKey, row.ContractVersion, DateOnly.FromDateTime(row.CompletionBusinessDate),
            row.SourceSnapshotHash, row.LearnerCount, row.CompletedAtUtc,
            row.CompletedBy, row.CompletionReason, learners);
    }

    private static async Task<CourseCompletionConfirmResult?> ReadOperationReplayAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CourseCompletionConfirmCommand command,
        CancellationToken cancellationToken)
    {
        var rows = (await connection.QueryAsync<OperationReplayRow>(Command(
            """
            SELECT o.OperationId,o.RequestFingerprint,o.ResultCode,o.CourseCompletionId,
                   o.CompletedAtUtc,CONVERT(datetime2(7),c.CompletionBusinessDate) AS CompletionBusinessDate,
                   c.CompletedBy,c.LearnerCount,
                   c.ContractVersion,c.SourceSnapshotHash
            FROM dbo.App_CourseCompletionOperation o WITH (UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.App_CourseCompletion c ON c.CourseCompletionId=o.CourseCompletionId
            WHERE o.SourceProfileCode=@SourceProfileCode
              AND o.SourceCourseKey=@SourceCourseKey
              AND o.ActorId=@Actor
              AND o.IdempotencyKeyHash=CONVERT(binary(32),@IdempotencyKeyHash,2);
            """,
            new
            {
                command.Preview.SourceProfileCode,
                command.Preview.SourceCourseKey,
                command.Actor,
                command.IdempotencyKeyHash,
            }, transaction, cancellationToken))).ToArray();
        if (rows.Length == 0) return null;
        if (rows.Length != 1 || !string.Equals(rows[0].RequestFingerprint, command.RequestFingerprint, StringComparison.Ordinal))
            throw Conflict(CourseCompletionCodes.Conflict, "IdempotencyKey đã được dùng cho request khác.");
        var row = rows[0];
        if (row.CourseCompletionId is null || row.CompletedAtUtc is null || row.CompletionBusinessDate is null ||
            row.CompletedBy is null || row.ContractVersion is null || row.SourceSnapshotHash is null)
            throw Conflict(CourseCompletionCodes.Conflict, "Operation trước chưa có committed result hợp lệ.");
        return new CourseCompletionConfirmResult(
            row.OperationId, row.CourseCompletionId.Value, row.ResultCode,
            DateOnly.FromDateTime(row.CompletionBusinessDate.Value), row.CompletedAtUtc.Value, row.CompletedBy,
            row.LearnerCount ?? 0, row.ContractVersion, row.SourceSnapshotHash);
    }

    private static async Task<CourseCompletionConfirmResult> InsertNoChangeOperationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CourseCompletionConfirmCommand command,
        CourseCompletionStoredMarker marker,
        CancellationToken cancellationToken)
    {
        var completedAt = await connection.ExecuteScalarAsync<DateTime>(Command(
            """
            DECLARE @Now datetime2(7)=SYSUTCDATETIME();
            INSERT dbo.App_CourseCompletionOperation
              (OperationId,SourceProfileCode,SourceCourseKey,ActorId,IdempotencyKeyHash,
               RequestFingerprint,PreviewSnapshotHash,ResultCode,CourseCompletionId,
               CreatedAtUtc,CompletedAtUtc,ErrorCode)
            VALUES
              (@OperationId,@SourceProfileCode,@SourceCourseKey,@Actor,
               CONVERT(binary(32),@IdempotencyKeyHash,2),@RequestFingerprint,@PreviewSnapshotHash,
               N'NO_CHANGE',@CourseCompletionId,@Now,@Now,NULL);
            INSERT dbo.App_AuditLog
              (ChucNang,HanhDong,EntityType,EntityId,EntityKey,DuLieuTruoc,DuLieuSau,
               KetQua,Loi,CreatedAt,CreatedBy)
            VALUES
              (N'COURSE_COMPLETION',N'CONFIRM_NO_CHANGE',N'App_CourseCompletion',
               CONVERT(nvarchar(100),@CourseCompletionId),@SourceCourseKey,
               @AuditBefore,@AuditAfter,N'NO_CHANGE',NULL,@Now,@Actor);
            SELECT @Now;
            """,
            new
            {
                command.OperationId,
                command.Preview.SourceProfileCode,
                command.Preview.SourceCourseKey,
                command.Actor,
                command.IdempotencyKeyHash,
                command.RequestFingerprint,
                PreviewSnapshotHash = command.Preview.SnapshotHash,
                marker.CourseCompletionId,
                AuditBefore = JsonSerializer.Serialize(new { marker.CourseCompletionId, marker.SourceSnapshotHash }),
                AuditAfter = JsonSerializer.Serialize(new { Result = CourseCompletionCodes.NoChange, marker.LearnerCount }),
            }, transaction, cancellationToken));
        return new(command.OperationId, marker.CourseCompletionId, CourseCompletionCodes.NoChange,
            marker.CompletionBusinessDate, completedAt, marker.CompletedBy, marker.LearnerCount,
            marker.ContractVersion, marker.SourceSnapshotHash);
    }

    private static async Task<CourseCompletionConfirmResult> InsertCompletionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CourseIdentity identity,
        CourseCompletionCanonicalSnapshot snapshot,
        CourseCompletionConfirmCommand command,
        CancellationToken cancellationToken)
    {
        var snapshotJson = JsonSerializer.Serialize(snapshot.Learners.Select(x => new
        {
            x.ProtectedIdentity,
            x.SourceProfileCode,
            x.SourceCourseKey,
            x.LearnerCourseKey,
            x.Status,
            x.Classification,
            x.ResultCompleteness,
            x.DownstreamClassification,
            x.CanonicalRowHash,
        }));
        var created = await connection.QuerySingleAsync<CreatedMarkerRow>(Command(
            """
            DECLARE @Now datetime2(7)=SYSUTCDATETIME();
            INSERT dbo.App_CourseCompletion
              (KhoaHocId,SourceProfileCode,SourceCourseKey,ContractVersion,Status,
               CompletionBusinessDate,SourceSnapshotHash,LearnerCount,CompletedAtUtc,
               CompletedBy,CompletionReason,CreatedOperationId)
            OUTPUT inserted.CourseCompletionId,inserted.CompletedAtUtc
            VALUES
              (@KhoaHocId,@SourceProfileCode,@SourceCourseKey,@ContractVersion,N'COMPLETED',
               @CompletionBusinessDate,@SnapshotHash,@LearnerCount,@Now,@Actor,@Reason,@OperationId);
            """,
            new
            {
                identity.KhoaHocId,
                snapshot.SourceProfileCode,
                snapshot.SourceCourseKey,
                snapshot.ContractVersion,
                command.CompletionBusinessDate,
                SnapshotHash = snapshot.SnapshotHash,
                snapshot.LearnerCount,
                command.Actor,
                command.Reason,
                command.OperationId,
            }, transaction, cancellationToken));

        await connection.ExecuteAsync(Command(
            """
            DECLARE @Now datetime2(7)=SYSUTCDATETIME();
            INSERT dbo.App_CourseCompletionLearnerSnapshot
              (CourseCompletionId,SourceProfileCode,SourceCourseKey,ProtectedLearnerIdentity,
               MaKhoaHoc,TT_XuLy,LearnerClassification,ResultCompletenessClassification,
               DownstreamClassification,CanonicalLearnerRowHash,SnapshotAtUtc)
            SELECT @CourseCompletionId,j.SourceProfileCode,j.SourceCourseKey,j.ProtectedIdentity,
                   j.LearnerCourseKey,j.Status,j.Classification,j.ResultCompleteness,
                   j.DownstreamClassification,j.CanonicalRowHash,@Now
            FROM OPENJSON(@SnapshotJson)
            WITH
              (ProtectedIdentity char(64) '$.ProtectedIdentity',
               SourceProfileCode nvarchar(20) '$.SourceProfileCode',
               SourceCourseKey nvarchar(50) '$.SourceCourseKey',
               LearnerCourseKey nvarchar(50) '$.LearnerCourseKey',
               Status nvarchar(10) '$.Status',
               Classification nvarchar(40) '$.Classification',
               ResultCompleteness nvarchar(40) '$.ResultCompleteness',
               DownstreamClassification nvarchar(40) '$.DownstreamClassification',
               CanonicalRowHash char(64) '$.CanonicalRowHash') j;

            INSERT dbo.App_CourseCompletionOperation
              (OperationId,SourceProfileCode,SourceCourseKey,ActorId,IdempotencyKeyHash,
               RequestFingerprint,PreviewSnapshotHash,ResultCode,CourseCompletionId,
               CreatedAtUtc,CompletedAtUtc,ErrorCode)
            VALUES
              (@OperationId,@SourceProfileCode,@SourceCourseKey,@Actor,
               CONVERT(binary(32),@IdempotencyKeyHash,2),@RequestFingerprint,@SnapshotHash,
               N'COMPLETED',@CourseCompletionId,@Now,@Now,NULL);

            INSERT dbo.App_AuditLog
              (ChucNang,HanhDong,EntityType,EntityId,EntityKey,DuLieuTruoc,DuLieuSau,
               KetQua,Loi,CreatedAt,CreatedBy)
            VALUES
              (N'COURSE_COMPLETION',N'CONFIRM',N'App_CourseCompletion',
               CONVERT(nvarchar(100),@CourseCompletionId),@SourceCourseKey,NULL,@AuditAfter,
               N'COMPLETED',NULL,@Now,@Actor);
            """,
            new
            {
                CourseCompletionId = created.CourseCompletionId,
                SnapshotJson = snapshotJson,
                command.OperationId,
                snapshot.SourceProfileCode,
                snapshot.SourceCourseKey,
                command.Actor,
                command.IdempotencyKeyHash,
                command.RequestFingerprint,
                SnapshotHash = snapshot.SnapshotHash,
                AuditAfter = JsonSerializer.Serialize(new
                {
                    created.CourseCompletionId,
                    snapshot.SourceProfileCode,
                    snapshot.SourceCourseKey,
                    snapshot.ContractVersion,
                    snapshot.SnapshotHash,
                    snapshot.LearnerCount,
                    command.CompletionBusinessDate,
                    command.Reason,
                }),
            }, transaction, cancellationToken));

        var persisted = (await connection.QueryAsync<PersistedSnapshotRow>(Command(
            """
            SELECT ProtectedLearnerIdentity,CanonicalLearnerRowHash
            FROM dbo.App_CourseCompletionLearnerSnapshot
            WHERE CourseCompletionId=@CourseCompletionId
            ORDER BY ProtectedLearnerIdentity;
            """,
            new { created.CourseCompletionId }, transaction, cancellationToken))).ToArray();
        var expected = snapshot.Learners
            .OrderBy(x => x.ProtectedIdentity, StringComparer.Ordinal)
            .Select(x => (x.ProtectedIdentity, x.CanonicalRowHash)).ToArray();
        if (persisted.Length != expected.Length || persisted.Where((row, index) =>
                row.ProtectedLearnerIdentity != expected[index].ProtectedIdentity ||
                row.CanonicalLearnerRowHash != expected[index].CanonicalRowHash).Any())
            throw new CourseCompletionDomainException(CourseCompletionCodes.Blocked, "Verification snapshot count/hash thất bại; transaction bị rollback.", 500);

        return new CourseCompletionConfirmResult(
            command.OperationId, created.CourseCompletionId, CourseCompletionCodes.Completed,
            command.CompletionBusinessDate, created.CompletedAtUtc, command.Actor,
            snapshot.LearnerCount, snapshot.ContractVersion, snapshot.SnapshotHash);
    }

    private static string RawFingerprint(RawScope scope)
    {
        var values = scope.Learners.Select(x => JsonSerializer.Serialize(x))
            .OrderBy(x => x, StringComparer.Ordinal);
        return CourseCompletionCanonicalSnapshotBuilder.Sha256(
            JsonSerializer.Serialize(scope.Course) + "\n" + string.Join("\n", values));
    }

    private static string RawV1Fingerprint(RawV1Scope scope)
    {
        var values = scope.Learners.Select(x => JsonSerializer.Serialize(x))
            .OrderBy(x => x, StringComparer.Ordinal);
        return CourseCompletionCanonicalSnapshotBuilder.Sha256(
            scope.CourseCount + "\n" + string.Join("\n", values));
    }

    private static void ValidateDatabase(DatabaseIdentity actual, string expectedName, Guid? expectedGuid)
    {
        if (!string.Equals(actual.DatabaseName, expectedName, StringComparison.OrdinalIgnoreCase) ||
            (expectedGuid.HasValue && actual.DatabaseGuid != expectedGuid.Value))
            throw new CourseCompletionDomainException(CourseCompletionCodes.AmbiguousIdentity, "Source database identity không khớp production allowlist.", 409);
    }

    private static CourseCompletionDomainException Conflict(string code, string message) => new(code, message, 409);

    private static CommandDefinition Command(
        string sql,
        object? parameters = null,
        SqlTransaction? transaction = null,
        CancellationToken cancellationToken = default) => new(
            sql, parameters, transaction, CommandTimeoutSeconds, cancellationToken: cancellationToken);

    internal const string V2ReadSql = """
        SELECT DB_NAME() AS DatabaseName,drs.database_guid AS DatabaseGuid
        FROM sys.database_recovery_status drs WHERE drs.database_id=DB_ID();
        SELECT MaKH AS SourceCourseKey,MaCSDT AS MaCsdt,MaSoGTVT AS MaSoGtvt,
               CONVERT(nvarchar(30),HangDT) AS TrainingClass,
               CONVERT(nvarchar(30),HangGPLX) AS LicenseClass,
               CONVERT(nvarchar(50),HTDaoTao) AS TrainingForm,
               CONVERT(datetime2(7),NgayKG) AS StartDate,
               CONVERT(datetime2(7),NgayBG) AS EndDate
        FROM dbo.KhoaHoc
        WHERE MaKH=@CourseKey AND MaCSDT=@ExpectedMaCsdt;
        SELECT CONVERT(nvarchar(50),MaDK) AS RegistrationCode,
               CONVERT(nvarchar(50),MaKhoaHoc) AS CourseKey,
               CONVERT(nvarchar(10),TT_XuLy) AS V2Status,
               CONVERT(nvarchar(100),KetLuanCSDT) AS Conclusion,
               COALESCE(TRY_CONVERT(datetime2(7),NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),TGBatDau))),N''),126),
                        TRY_CONVERT(datetime2(7),NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),TGBatDau))),N''),121),
                        TRY_CONVERT(datetime2(7),NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),TGBatDau))),N''),103)) AS TrainingStartedAt,
               COALESCE(TRY_CONVERT(datetime2(7),NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),TGKetThuc))),N''),126),
                        TRY_CONVERT(datetime2(7),NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),TGKetThuc))),N''),121),
                        TRY_CONVERT(datetime2(7),NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),TGKetThuc))),N''),103)) AS TrainingCompletedAt,
               CONVERT(nvarchar(100),KQLyThuyet) AS TheoryResult,
               CONVERT(nvarchar(100),KQThucHanh) AS PracticeResult,
               CONVERT(nvarchar(100),DiemKQLyThuyet) AS TheoryScore,
               CONVERT(nvarchar(100),DiemKQThucHanh) AS PracticeScore,
               CONVERT(nvarchar(100),TGThucHanhHinh) AS FigurePracticeTime,
               CONVERT(nvarchar(100),TGThucHanhDuong) AS RoadPracticeTime,
               CONVERT(nvarchar(100),QDThucHanhHinh) AS FigureDistance,
               CONVERT(nvarchar(100),TongQDThucHanh) AS RoadDistance,
               CONVERT(bit,0) AS HasReportII,
               CONVERT(bit,0) AS HasExamLifecycle,
               CONVERT(bit,0) AS HasLicense
        FROM dbo.NguoiLX_HoSo
        WHERE MaKhoaHoc=@CourseKey;
        SELECT
          (SELECT COUNT_BIG(1) FROM dbo.BaoCaoI WHERE MaKH=@CourseKey) AS ReportICount,
          (SELECT COUNT_BIG(1) FROM dbo.KhoaHoc_GiaoVien WHERE MaKH=@CourseKey) AS TeacherCount,
          (SELECT COUNT_BIG(1) FROM dbo.KhoaHoc_XeTap WHERE MaKH=@CourseKey) AS VehicleCount;
        """;

    internal const string V1ReadSql = """
        SELECT DB_NAME() AS DatabaseName,drs.database_guid AS DatabaseGuid
        FROM sys.database_recovery_status drs WHERE drs.database_id=DB_ID();
        SELECT CONVERT(int,COUNT_BIG(1))
        FROM dbo.KhoaHoc WHERE MaKH=@CourseKey AND MaCSDT=@ExpectedMaCsdt;
        SELECT CONVERT(nvarchar(50),MaDK) AS RegistrationCode,
               CONVERT(nvarchar(50),MaKhoaHoc) AS CourseKey,
               CONVERT(nvarchar(10),TT_XuLy) AS V1Status,
               CONVERT(bit,CASE WHEN NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),MaBC2))),N'') IS NULL THEN 0 ELSE 1 END) AS HasReportII,
               CONVERT(bit,CASE WHEN NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),MaKySH))),N'') IS NULL THEN 0 ELSE 1 END) AS HasExamLifecycle,
               CONVERT(bit,CASE WHEN EXISTS
                 (SELECT 1 FROM dbo.NguoiLX_GPLX g WHERE g.MaDK=hs.MaDK
                    AND NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),g.SoGPLX))),N'') IS NOT NULL)
                 THEN 1 ELSE 0 END) AS HasLicense
        FROM dbo.NguoiLX_HoSo hs
        WHERE hs.MaKhoaHoc=@CourseKey;
        """;

    private sealed record CourseIdentity(long KhoaHocId, string SourceProfileCode, string SourceCourseKey);
    private sealed record SourceRoute(
        string V2DatabaseName, string V1DatabaseName, Guid ExpectedV2DatabaseGuid,
        string ExpectedMaCsdt, string V2ConnectionString, string V1ConnectionString);
    private sealed record DatabaseIdentity(string DatabaseName, Guid DatabaseGuid);
    private sealed record SourceCourseRow(
        string SourceCourseKey, string MaCsdt, string? MaSoGtvt, string? TrainingClass,
        string? LicenseClass, string? TrainingForm, DateTime? StartDate, DateTime? EndDate);
    private sealed record SourceDiagnosticRow(long ReportICount, long TeacherCount, long VehicleCount)
    {
        public IReadOnlyList<string> ToWarnings() => [];
    }
    private sealed class CourseCompletionLearnerDbRow
    {
        public string? RegistrationCode { get; set; }
        public string? CourseKey { get; set; }
        public string? V2Status { get; set; }
        public string? V1Status { get; set; }
        public string? Conclusion { get; set; }
        public DateTime? TrainingStartedAt { get; set; }
        public DateTime? TrainingCompletedAt { get; set; }
        public string? TheoryResult { get; set; }
        public string? PracticeResult { get; set; }
        public string? TheoryScore { get; set; }
        public string? PracticeScore { get; set; }
        public string? FigurePracticeTime { get; set; }
        public string? RoadPracticeTime { get; set; }
        public string? FigureDistance { get; set; }
        public string? RoadDistance { get; set; }
        public bool HasReportII { get; set; }
        public bool HasExamLifecycle { get; set; }
        public bool HasLicense { get; set; }

        public CourseCompletionLearnerSource ToSource() => new(
            RegistrationCode, CourseKey, V2Status, V1Status, Conclusion,
            TrainingStartedAt, TrainingCompletedAt, TheoryResult, PracticeResult,
            TheoryScore, PracticeScore, FigurePracticeTime, RoadPracticeTime,
            FigureDistance, RoadDistance, HasReportII, HasExamLifecycle, HasLicense);
    }
    private sealed record RawScope(
        CourseCompletionCourseSource Course,
        IReadOnlyList<CourseCompletionLearnerSource> Learners,
        IReadOnlyList<string> Diagnostics);
    private sealed record RawV1Scope(int CourseCount, IReadOnlyList<CourseCompletionLearnerSource> Learners);
    private sealed record MarkerRow(
        long CourseCompletionId, long KhoaHocId, string SourceProfileCode, string SourceCourseKey,
        string ContractVersion, DateTime CompletionBusinessDate, string SourceSnapshotHash,
        int LearnerCount, DateTime CompletedAtUtc, string CompletedBy, string CompletionReason);
    private sealed record OperationReplayRow(
        Guid OperationId, string RequestFingerprint, string ResultCode, long? CourseCompletionId,
        DateTime? CompletedAtUtc, DateTime? CompletionBusinessDate, string? CompletedBy,
        int? LearnerCount, string? ContractVersion, string? SourceSnapshotHash);
    private sealed record CreatedMarkerRow(long CourseCompletionId, DateTime CompletedAtUtc);
    private sealed record PersistedSnapshotRow(string ProtectedLearnerIdentity, string CanonicalLearnerRowHash);
}

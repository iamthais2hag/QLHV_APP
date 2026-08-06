using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Assignments;

namespace QLHV.Infrastructure.Assignments;

public sealed partial class SqlAssignmentRepository
{
    private const int IdempotencyRetentionDays = 180;

    public async Task<AssignmentImportConfirmResult?> TryReplayImportConfirmAsync(
        long courseId,
        string actor,
        string idempotencyKey,
        string previewToken,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var existing = await LoadAssignmentOperationLedgerAsync(
            connection, null, idempotencyKey, lockForUpdate: false, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        EnsureLedgerScope(
            existing,
            "IMPORT",
            courseId,
            sourceProfileCode: null,
            scopeId: null,
            actor,
            payloadSha256: null);
        if (!string.Equals(
                existing.PreviewTokenSha256,
                ComputePreviewTokenSha256(previewToken),
                StringComparison.Ordinal))
        {
            // A different token can still represent the same logical import plan. The
            // service will resolve that live preview and the transactional path below
            // will compare its payload hash before replaying.
            return null;
        }

        return ToImportReplay(existing);
    }

    public async Task<AssignmentConfirmReplay?> TryReplayAssignmentConfirmAsync(
        string kind,
        long? scopeId,
        string actor,
        string idempotencyKey,
        string previewToken,
        CancellationToken cancellationToken)
    {
        EnsureOperationKind(kind);
        await using var connection = await OpenAsync(cancellationToken);
        var existing = await LoadAssignmentOperationLedgerAsync(
            connection, null, idempotencyKey, lockForUpdate: false, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        EnsureLedgerScope(
            existing,
            kind,
            courseId: null,
            sourceProfileCode: null,
            scopeId,
            actor,
            payloadSha256: null);
        if (!string.Equals(
                existing.PreviewTokenSha256,
                ComputePreviewTokenSha256(previewToken),
                StringComparison.Ordinal))
        {
            return null;
        }

        return ToAssignmentReplay(existing);
    }

    private async Task AcquireAssignmentOperationLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // The key is intentionally global across actors and operation types. Reusing
        // one key for another actor/course/profile/file/operation must conflict, not
        // create a second mutation in another process.
        var resource = "QLHV:ASSIGNMENT:" + ComputeIdempotencyKeySha256(idempotencyKey);
        var result = await connection.ExecuteScalarAsync<int>(Command("""
            DECLARE @Result int;
            EXEC @Result=sys.sp_getapplock
                @Resource=@Resource,
                @LockMode=N'Exclusive',
                @LockOwner=N'Transaction',
                @LockTimeout=10000;
            SELECT @Result;
            """, new { Resource = resource }, cancellationToken, transaction));
        if (result < 0)
        {
            throw new AssignmentDomainException(
                Conflict, "Không thể khóa idempotency key; hãy thử lại.", 409);
        }
    }

    private async Task<AssignmentConfirmReplay?> TryReplaySealedAssignmentConfirmAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string kind,
        long courseId,
        string sourceProfileCode,
        long? scopeId,
        string actor,
        string idempotencyKey,
        string payloadSha256,
        CancellationToken cancellationToken)
    {
        EnsureOperationKind(kind);
        var existing = await LoadAssignmentOperationLedgerAsync(
            connection, transaction, idempotencyKey, lockForUpdate: true, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        EnsureLedgerScope(
            existing,
            kind,
            courseId,
            sourceProfileCode,
            scopeId,
            actor,
            payloadSha256);
        return ToAssignmentReplay(existing);
    }

    private async Task<AssignmentImportConfirmResult?> TryReplaySealedImportConfirmAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        AssignmentImportPlan plan,
        string actor,
        string idempotencyKey,
        string payloadSha256,
        CancellationToken cancellationToken)
    {
        var existing = await LoadAssignmentOperationLedgerAsync(
            connection, transaction, idempotencyKey, lockForUpdate: true, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        EnsureLedgerScope(
            existing,
            "IMPORT",
            plan.CourseId,
            plan.SourceProfileCode,
            scopeId: null,
            actor,
            payloadSha256);
        return ToImportReplay(existing);
    }

    private async Task<AssignmentOperationLedgerRow?> LoadAssignmentOperationLedgerAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string idempotencyKey,
        bool lockForUpdate,
        CancellationToken cancellationToken)
    {
        var lockHint = lockForUpdate ? " WITH (UPDLOCK,HOLDLOCK)" : string.Empty;
        var rows = (await connection.QueryAsync<AssignmentOperationLedgerRow>(Command($"""
            SELECT TOP(2)
                   AssignmentOperationId,
                   IdempotencyKeySha256,
                   OperationType,
                   CourseId,
                   SourceProfileCode,
                   ScopeId,
                   Actor,
                   PayloadSha256,
                   PreviewTokenSha256,
                   OperationId,
                   ImportBatchId,
                   ChangedCount,
                   NoChangeCount,
                   RequiresBulkPermission,
                   CompletedAtUtc,
                   RetainUntilUtc
            FROM dbo.App_AssignmentOperation{lockHint}
            WHERE IdempotencyKeySha256=@IdempotencyKeySha256
            ORDER BY AssignmentOperationId;
            """, new
        {
            IdempotencyKeySha256 = ComputeIdempotencyKeySha256(idempotencyKey),
        }, cancellationToken, transaction))).ToArray();
        if (rows.Length > 1)
        {
            throw new AssignmentDomainException(
                Conflict, "Idempotency ledger có nhiều kết quả; dừng fail-closed.", 409);
        }

        return rows.SingleOrDefault();
    }

    private async Task WriteAssignmentOperationLedgerAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string kind,
        long? scopeId,
        CourseIdentityRow course,
        string previewToken,
        string idempotencyKey,
        string payloadSha256,
        string operationId,
        string actor,
        bool requiresBulkPermission,
        int changedCount,
        int noChangeCount,
        long? importBatchId,
        DateTime completedAtUtc,
        CancellationToken cancellationToken)
    {
        EnsureOperationKind(kind);
        var affected = await connection.ExecuteAsync(Command("""
            INSERT dbo.App_AssignmentOperation
                (IdempotencyKeySha256,OperationType,CourseId,SourceProfileCode,ScopeId,Actor,
                 PayloadSha256,PreviewTokenSha256,OperationId,ImportBatchId,ChangedCount,
                 NoChangeCount,RequiresBulkPermission,CompletedAtUtc,RetainUntilUtc,CreatedAt)
            VALUES
                (@IdempotencyKeySha256,@OperationType,@CourseId,@SourceProfileCode,@ScopeId,@Actor,
                 @PayloadSha256,@PreviewTokenSha256,@OperationId,@ImportBatchId,@ChangedCount,
                 @NoChangeCount,@RequiresBulkPermission,@CompletedAtUtc,
                 DATEADD(day,@RetentionDays,@CompletedAtUtc),@CompletedAtUtc);
            """, new
        {
            IdempotencyKeySha256 = ComputeIdempotencyKeySha256(idempotencyKey),
            OperationType = kind,
            CourseId = course.KhoaHocId,
            course.SourceProfileCode,
            ScopeId = scopeId,
            Actor = actor,
            PayloadSha256 = payloadSha256,
            PreviewTokenSha256 = ComputePreviewTokenSha256(previewToken),
            OperationId = operationId,
            ImportBatchId = importBatchId,
            ChangedCount = changedCount,
            NoChangeCount = noChangeCount,
            RequiresBulkPermission = requiresBulkPermission,
            CompletedAtUtc = completedAtUtc,
            RetentionDays = IdempotencyRetentionDays,
        }, cancellationToken, transaction));
        AssertExactlyOne(affected, "Không thể ghi durable idempotency ledger.");
    }

    private static AssignmentConfirmReplay ToAssignmentReplay(
        AssignmentOperationLedgerRow row)
    {
        EnsureCompletedLedger(row);
        if (string.Equals(row.OperationType, "IMPORT", StringComparison.Ordinal))
        {
            throw new AssignmentDomainException(
                Conflict, "IdempotencyKey thuộc thao tác import.", 409);
        }

        return new AssignmentConfirmReplay(
            new AssignmentConfirmResult(
                row.OperationId,
                row.ChangedCount,
                row.NoChangeCount,
                row.CompletedAtUtc),
            row.RequiresBulkPermission);
    }

    private static AssignmentImportConfirmResult ToImportReplay(
        AssignmentOperationLedgerRow row)
    {
        EnsureCompletedLedger(row);
        if (!string.Equals(row.OperationType, "IMPORT", StringComparison.Ordinal) ||
            row.ImportBatchId is null)
        {
            throw new AssignmentDomainException(
                Conflict, "Durable import idempotency ledger không hợp lệ.", 409);
        }

        return new AssignmentImportConfirmResult(
            row.ImportBatchId.Value,
            row.OperationId,
            row.ChangedCount,
            row.NoChangeCount,
            row.CompletedAtUtc);
    }

    private static void EnsureCompletedLedger(AssignmentOperationLedgerRow row)
    {
        if (string.IsNullOrWhiteSpace(row.OperationId) ||
            string.IsNullOrWhiteSpace(row.PayloadSha256) ||
            string.IsNullOrWhiteSpace(row.PreviewTokenSha256) ||
            row.ChangedCount < 0 ||
            row.NoChangeCount < 0 ||
            row.CompletedAtUtc == default ||
            row.RetainUntilUtc <= row.CompletedAtUtc)
        {
            throw new AssignmentDomainException(
                Conflict, "Durable idempotency ledger không hợp lệ; dừng fail-closed.", 409);
        }
    }

    private static void EnsureLedgerScope(
        AssignmentOperationLedgerRow row,
        string kind,
        long? courseId,
        string? sourceProfileCode,
        long? scopeId,
        string actor,
        string? payloadSha256)
    {
        if (!string.Equals(row.OperationType, kind, StringComparison.Ordinal) ||
            (courseId.HasValue && row.CourseId != courseId.Value) ||
            (sourceProfileCode is not null &&
             !string.Equals(row.SourceProfileCode, sourceProfileCode, StringComparison.Ordinal)) ||
            row.ScopeId != scopeId ||
            !string.Equals(row.Actor, actor, StringComparison.OrdinalIgnoreCase) ||
            (payloadSha256 is not null &&
             !string.Equals(row.PayloadSha256, payloadSha256, StringComparison.Ordinal)))
        {
            throw new AssignmentDomainException(
                Conflict,
                "IdempotencyKey thuộc actor, thao tác, khóa, source profile, scope hoặc payload khác.",
                409);
        }
    }

    private static string ComputeImportPayloadSha256(AssignmentImportPlan plan) =>
        AssignmentRules.ComputeFingerprint(
        [
            "IMPORT",
            plan.CourseId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            plan.SourceProfileCode,
            plan.CourseCode,
            plan.FileSha256,
            AssignmentExcel.TemplateVersion,
            AssignmentExcel.NormalizationVersion,
            "ASSIGNMENT",
        ]);

    private static string ComputePreviewTokenSha256(string previewToken) =>
        AssignmentRules.ComputeFingerprint(["PREVIEW_TOKEN_V1", previewToken]);

    private static string ComputeIdempotencyKeySha256(string idempotencyKey) =>
        AssignmentRules.ComputeFingerprint(["IDEMPOTENCY_KEY_V1", idempotencyKey]);

    private static void EnsureOperationKind(string kind)
    {
        if (kind is not ("ASSIGNMENT" or "GROUP_DEFAULTS" or "IMPORT"))
        {
            throw new AssignmentDomainException(Invalid, "Loại operation ledger không hợp lệ.");
        }
    }

    private sealed class AssignmentOperationLedgerRow
    {
        public long AssignmentOperationId { get; init; }
        public string IdempotencyKeySha256 { get; init; } = string.Empty;
        public string OperationType { get; init; } = string.Empty;
        public long CourseId { get; init; }
        public string SourceProfileCode { get; init; } = string.Empty;
        public long? ScopeId { get; init; }
        public string Actor { get; init; } = string.Empty;
        public string PayloadSha256 { get; init; } = string.Empty;
        public string PreviewTokenSha256 { get; init; } = string.Empty;
        public string OperationId { get; init; } = string.Empty;
        public long? ImportBatchId { get; init; }
        public int ChangedCount { get; init; }
        public int NoChangeCount { get; init; }
        public bool RequiresBulkPermission { get; init; }
        public DateTime CompletedAtUtc { get; init; }
        public DateTime RetainUntilUtc { get; init; }
    }
}

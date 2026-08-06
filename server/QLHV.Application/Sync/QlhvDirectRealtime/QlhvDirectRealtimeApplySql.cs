namespace QLHV.Application.Sync.QlhvDirectRealtime;

/// <summary>
/// Review-only command text for a future isolated adapter. RT-02A does not
/// register or execute these commands. Identifiers are fixed; values are
/// parameterized. There is intentionally no delete/deactivation/profile writer.
/// </summary>
public static class QlhvDirectRealtimeApplySql
{
    public const string RecheckInsertIdentity = """
        SELECT Id, IsDeleted, SourceProfileCode
        FROM dbo.HocVien WITH (UPDLOCK, HOLDLOCK)
        WHERE SourceIdentityHash = @SourceIdentityHash;
        """;

    public const string InsertLearner = """
        INSERT INTO dbo.HocVien
            (SourceProfileCode, SourceMaDK, HoTen, V2RowHash)
        VALUES
            (@SourceProfileCode, @SourceMaDK, @HoTen, @SourceRowHash);
        """;

    public const string UpdateHoTen = """
        UPDATE dbo.HocVien
        SET HoTen = @HoTen,
            V2RowHash = @SourceRowHash
        WHERE Id = @TargetId
          AND SourceProfileCode = @SourceProfileCode
          AND V2RowHash = @ExpectedMappedHash;
        """;

    public const string InsertManualReviewEvidence = """
        INSERT INTO dbo.App_QlhvDirectRealtimeManualReview
            (CycleId, OperationId, IdentityHmac, Disposition, DispositionHash,
             TargetRetainedActive, TargetMutated)
        VALUES
            (@CycleId, @OperationId, @IdentityHmac, @Disposition, @DispositionHash,
             @TargetRetainedActive, @TargetMutated);
        """;

    public const string InsertApplyMarker = """
        INSERT INTO dbo.App_QlhvDirectRealtimeApplyMarker
            (CycleId, PlanHash, DispositionHash, InsertedRows, UpdatedRows,
             RetainedRows, PreservedQlhvOwnedHash, CommittedAtUtc)
        VALUES
            (@CycleId, @PlanHash, @DispositionHash, @InsertedRows, @UpdatedRows,
             @RetainedRows, @PreservedQlhvOwnedHash, @CommittedAtUtc);
        """;

    public static IReadOnlyList<string> ReviewOnlyCommands { get; } =
    [
        RecheckInsertIdentity,
        InsertLearner,
        UpdateHoTen,
        InsertManualReviewEvidence,
        InsertApplyMarker,
    ];
}

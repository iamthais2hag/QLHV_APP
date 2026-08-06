using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Tests.Sync;

public sealed class QlhvAutoSyncStateModelTests
{
    private static readonly DateTime Now = new(2026, 7, 28, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("QUEUED", true, -1, null, "CONNECTING", false, true, "ACTIVE")]
    [InlineData("RUNNING", true, -1, "OTO", null, false, true, "ACTIVE")]
    [InlineData("RUNNING", true, -1, null, "SYNC_OTO", false, true, "ACTIVE")]
    [InlineData("QUEUED", true, -121, "OTO", "CONNECTING", false, false, "INACTIVE_STALE_RUN")]
    [InlineData("RUNNING", true, -121, "OTO", "SYNC_OTO", false, false, "INACTIVE_STALE_RUN")]
    [InlineData("SUCCEEDED", false, -1, null, "COMPLETED", true, false, "HISTORY")]
    [InlineData("PARTIAL_SUCCESS", false, -1, null, "COMPLETED", true, false, "HISTORY")]
    [InlineData("PARTIAL_FAILED", false, -1, null, "FAILED", true, false, "HISTORY")]
    [InlineData("FAILED", false, -1, null, "FAILED", true, false, "HISTORY")]
    [InlineData("QUEUED", false, -1, null, "CONNECTING", false, false, "INACTIVE_STALE_RUN")]
    [InlineData("RUNNING", false, -1, "OTO", "SYNC_OTO", false, false, "INACTIVE_STALE_RUN")]
    [InlineData("QUEUED", true, -1, null, "CONNECTING", true, false, "INACTIVE_STALE_RUN")]
    [InlineData("RUNNING", true, -1, "OTO", "SYNC_OTO", true, false, "INACTIVE_STALE_RUN")]
    [InlineData("QUEUED", true, -1, null, null, false, false, "INACTIVE_STALE_RUN")]
    [InlineData("RUNNING", true, -1, null, null, false, false, "INACTIVE_STALE_RUN")]
    [InlineData("UNKNOWN", true, -1, "OTO", "SYNC_OTO", false, false, "INACTIVE_STALE_RUN")]
    [InlineData("", true, -1, "OTO", "SYNC_OTO", false, false, "INACTIVE_STALE_RUN")]
    [InlineData("QUEUED", true, -120, null, "CONNECTING", false, true, "ACTIVE")]
    [InlineData("QUEUED", true, -121, null, "CONNECTING", false, false, "INACTIVE_STALE_RUN")]
    [InlineData("RUNNING", true, 5, "MOTO", "SYNC_MOTO", false, true, "ACTIVE")]
    public void Twenty_case_runtime_classification_matrix(
        string status,
        bool activeSlot,
        int heartbeatOffsetSeconds,
        string? source,
        string? step,
        bool completed,
        bool expectedActive,
        string expectedClassification)
    {
        var result = QlhvAutoSyncRunClassifier.Classify(new QlhvAutoSyncRunRecord
        {
            RunId = Guid.NewGuid(),
            Status = status,
            ActiveSlot = activeSlot,
            CurrentSourceType = source,
            CurrentStage = step,
            CreatedAtUtc = Now.AddMinutes(-10),
            UpdatedAtUtc = Now.AddSeconds(heartbeatOffsetSeconds),
            CompletedAtUtc = completed ? Now : null,
        }, Now, 120);

        Assert.Equal(expectedActive, result.IsActive);
        Assert.Equal(expectedClassification, result.Classification);
    }
}

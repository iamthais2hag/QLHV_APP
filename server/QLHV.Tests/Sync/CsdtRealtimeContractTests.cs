using System.Text.Json;
using QLHV.Application.Sync.Realtime;

namespace QLHV.Tests.Sync;

public sealed class CsdtRealtimeContractTests
{
    [Theory]
    [MemberData(nameof(ClientContracts))]
    public void Application_dto_json_properties_match_client_contract(
        Type dtoType,
        string[] expectedProperties)
    {
        var actual = dtoType
            .GetProperties()
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedProperties.Order(StringComparer.Ordinal), actual);
    }

    public static TheoryData<Type, string[]> ClientContracts()
        => new()
        {
            {
                typeof(CsdtRealtimeDomainStatusDto),
                [
                    "domain", "state", "sourceRows", "targetRows", "insertedRows",
                    "updatedRows", "skippedRows", "errorRows", "lastError",
                ]
            },
            {
                typeof(CsdtRealtimeStreamStatusDto),
                [
                    "streamCode", "vehicleType", "sourceProfileCode", "targetProfileCode",
                    "sourceDatabaseName", "targetDatabaseName", "maCSDT", "enabled", "state",
                    "baselineStatus", "baselineVersion", "lastSuccessfulVersion",
                    "currentSourceVersion", "minimumValidVersion", "lagVersions", "activeRunId",
                    "retryCount", "nextRetryAtUtc", "lastStartedAtUtc", "lastCompletedAtUtc",
                    "lastSuccessAtUtc", "insertedRows", "updatedRows", "skippedRows",
                    "errorRows", "deleteTombstoneCount", "unresolvedConflictCount", "lastError",
                    "currentUserRole", "writeAuthorized", "stateToken", "actionBlockers", "domains",
                ]
            },
            {
                typeof(CsdtRealtimeStreamsResponseDto),
                ["observedAtUtc", "streams"]
            },
            {
                typeof(CsdtRealtimeRunDomainDto),
                [
                    "domain", "state", "attemptCount", "lastAttemptAtUtc", "succeededAtUtc",
                    "insertedRows", "updatedRows", "skippedRows", "errorRows", "message",
                ]
            },
            {
                typeof(CsdtRealtimeHistoryItemDto),
                [
                    "runId", "streamCode", "runType", "status", "fromVersion", "toVersion",
                    "startedAtUtc", "completedAtUtc", "insertedRows", "updatedRows",
                    "skippedRows", "errorRows", "actor", "errorMessage", "canRetry", "domains",
                ]
            },
            {
                typeof(CsdtRealtimeTombstoneDto),
                [
                    "id", "streamCode", "domain", "sourceKey", "changeVersion",
                    "detectedAtUtc", "status", "message",
                ]
            },
            {
                typeof(CsdtRealtimeActionResultDto),
                ["accepted", "joinedExisting", "runId", "status", "message"]
            },
            {
                typeof(CsdtRealtimeEnableRequest),
                ["enabled", "expectedStateToken"]
            },
            {
                typeof(CsdtRealtimeBaselineRequest),
                ["expectedStateToken"]
            },
            {
                typeof(CsdtRealtimeRetryRequest),
                ["expectedStateToken"]
            },
            {
                typeof(CsdtReverseDomainPlanDto),
                [
                    "domain", "sourceRows", "safeInsertRows", "safeUpdateRows",
                    "skippedRows", "reviewRows",
                ]
            },
            {
                typeof(CsdtReversePlanDto),
                [
                    "isReadOnly", "vehicleType", "direction", "sourceDatabaseName",
                    "targetDatabaseName", "maKhoaHoc", "generatedAtUtc", "expiresAtUtc",
                    "planToken", "sourceRows", "safeInsertRows", "safeUpdateRows",
                    "skippedRows", "v1OnlyRequiresReview", "identityChanged",
                    "conflictRequiresReview", "executable", "blockers", "warnings", "domains",
                ]
            },
            {
                typeof(CsdtReverseExecuteRequest),
                ["vehicleType", "maKhoaHoc", "expectedPlanToken"]
            },
            {
                typeof(CsdtReverseExecuteResultDto),
                ["accepted", "joinedExisting", "runId", "status", "message", "plan"]
            },
        };
}

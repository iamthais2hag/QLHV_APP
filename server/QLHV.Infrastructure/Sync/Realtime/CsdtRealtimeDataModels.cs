using System.Data;
using QLHV.Application.Sync.Realtime;

namespace QLHV.Infrastructure.Sync.Realtime;

internal sealed record CsdtRealtimeColumnMetadata(
    string Name,
    string SqlType,
    short MaxLength,
    byte Precision,
    byte Scale,
    bool IsNullable,
    bool IsIdentity,
    bool IsComputed,
    bool HasDefault,
    int ColumnId,
    int? PrimaryKeyOrdinal)
{
    public bool IsPrimaryKey => PrimaryKeyOrdinal.HasValue;

    public bool IsText =>
        SqlType is "varchar" or "nvarchar" or "char" or "nchar";

    public bool IsUnicode => SqlType is "nvarchar" or "nchar";

    public int? MaximumCharacters => MaxLength < 0
        ? null
        : IsUnicode
            ? MaxLength / 2
            : IsText
                ? MaxLength
                : null;

    public string ToSqlDeclaration()
    {
        var type = SqlType switch
        {
            "varchar" or "char" => $"{SqlType}({LengthToken(MaxLength)})",
            "nvarchar" or "nchar" => $"{SqlType}({LengthToken(MaxLength < 0 ? MaxLength : (short)(MaxLength / 2))})",
            "decimal" or "numeric" => $"{SqlType}({Precision},{Scale})",
            "datetime2" or "datetimeoffset" or "time" => $"{SqlType}({Scale})",
            _ => SqlType,
        };
        return $"{Quote(Name)} {type} {(IsNullable ? "NULL" : "NOT NULL")}";
    }

    private static string LengthToken(short length) => length < 0 ? "max" : length.ToString();

    internal static string Quote(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}

internal sealed record CsdtRealtimeTableMetadata(
    CsdtRealtimeDomainDefinition Domain,
    IReadOnlyList<CsdtRealtimeColumnMetadata> Columns)
{
    public IReadOnlyList<CsdtRealtimeColumnMetadata> PrimaryKey =>
        Columns.Where(item => item.IsPrimaryKey)
            .OrderBy(item => item.PrimaryKeyOrdinal)
            .ToArray();

    public IReadOnlyList<CsdtRealtimeColumnMetadata> WritableColumns =>
        Columns.Where(item => !item.IsComputed).OrderBy(item => item.ColumnId).ToArray();
}

internal sealed record CsdtRealtimeSnapshot(
    CsdtRealtimeTableMetadata SourceMetadata,
    DataTable Rows);

internal sealed record CsdtRealtimeChange(
    long Version,
    string Operation,
    string KeyJson,
    bool CurrentRowIsInPartition);

internal sealed record CsdtRealtimeEntitySnapshot(
    string KeyJson,
    byte[] SourceHash,
    byte[] TargetHash);

internal sealed record CsdtRealtimeConflictRecord(
    string KeyJson,
    string Code,
    string Message,
    IReadOnlyList<string>? Columns = null);

internal sealed record CsdtRealtimeForwardPlanningContext(
    IReadOnlySet<string> RelationshipLockedKeys,
    IReadOnlySet<string> MissingParentKeys)
{
    internal static CsdtRealtimeForwardPlanningContext Empty { get; } =
        new(
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));
}

internal sealed record CsdtRealtimePlannedSnapshot(
    DataTable Rows,
    IReadOnlyList<CsdtRealtimeConflictRecord> Conflicts);

internal sealed record CsdtRealtimePlannedRow(
    DataRow Row,
    bool Include,
    CsdtRealtimeConflictRecord? Conflict);

internal sealed record CsdtRealtimeWriteResult(
    long SourceRows,
    long TargetRows,
    long InsertedRows,
    long UpdatedRows,
    long SkippedRows,
    IReadOnlyList<CsdtRealtimeEntitySnapshot> Entities,
    IReadOnlyList<CsdtRealtimeConflictRecord> Conflicts);

internal class CsdtRealtimeSchemaException : InvalidOperationException
{
    public CsdtRealtimeSchemaException(string message)
        : base(message)
    {
    }
}

internal sealed record CsdtRealtimeRuntimeStream(
    long StreamId,
    string StreamCode,
    bool IsEnabled,
    string StreamStatus,
    string BaselineStatus,
    long? BaselineVersion,
    long? LastSuccessfulVersion,
    DateTimeOffset? LastReconciledAtUtc,
    DateTimeOffset? NextRetryAtUtc,
    int RetryCount);

internal sealed record CsdtRealtimeRuntimeDomain(
    long StreamId,
    string DomainCode,
    bool IsOptional,
    string DomainStatus,
    string BaselineStatus,
    long? BaselineVersion,
    long? LastSuccessfulVersion,
    DateTimeOffset? NextRetryAtUtc,
    int RetryCount);

internal sealed record CsdtRealtimeClaimedCommand(
    Guid CommandId,
    long StreamId,
    string StreamCode,
    string CommandType,
    string RequestedBy,
    string? RequestJson,
    byte[]? ExpectedRowVersion,
    byte[] CurrentRowVersion);

internal sealed record CsdtRealtimeRunHandle(
    Guid RunId,
    long StreamId,
    string RunType,
    long? FromVersion,
    long? ToVersion);

internal sealed record CsdtRealtimeEntityLedgerRow(
    string DomainCode,
    string EntityKey,
    byte[] EntityKeyHash,
    byte[]? SourceHash,
    byte[]? TargetHash,
    long? SourceVersion);

internal sealed record CsdtRealtimeSourceIdentityInventoryRow(
    string SourceIdentity,
    string IdentityStatus,
    long LastSeenVersion);

internal sealed record CsdtReverseDomainIntent(
    string Domain,
    bool IsOptional,
    long SourceRows,
    string SourceDigest,
    int AttemptCount);

internal sealed record CsdtReverseRecovery(
    Guid RunId,
    IReadOnlyDictionary<string, CsdtReverseDomainIntent> Domains);

internal sealed record CsdtReverseDomainWrite(
    CsdtRealtimeDomainDefinition Domain,
    CsdtRealtimeSnapshot Snapshot,
    IReadOnlyDictionary<string, byte[]> ExpectedTargetHashes,
    long SourceRows);

internal sealed record CsdtReverseAtomicWriteDomainResult(
    string Domain,
    string Status,
    long SourceRows,
    long UpdatedRows,
    long SkippedRows,
    string? ErrorCode = null,
    string? ErrorMessage = null);

internal sealed record CsdtReverseAtomicWriteResult(
    IReadOnlyList<CsdtReverseAtomicWriteDomainResult> Domains)
{
    internal long UpdatedRows => Domains.Sum(domain => domain.UpdatedRows);

    internal bool HasOptionalSkips => Domains.Any(domain =>
        string.Equals(domain.Status, "SKIPPED", StringComparison.Ordinal));
}

internal sealed class CsdtReverseAtomicWriteException : InvalidOperationException
{
    internal CsdtReverseAtomicWriteException(
        string failedDomain,
        IReadOnlyList<string> attemptedDomains,
        IReadOnlyList<CsdtReverseAtomicWriteDomainResult> optionalSkips,
        Exception innerException)
        : base(
            $"Atomic reverse target transaction rolled back after domain {failedDomain} failed.",
            innerException)
    {
        FailedDomain = failedDomain;
        AttemptedDomains = attemptedDomains;
        OptionalSkips = optionalSkips;
    }

    internal string FailedDomain { get; }

    internal IReadOnlyList<string> AttemptedDomains { get; }

    internal IReadOnlyList<CsdtReverseAtomicWriteDomainResult> OptionalSkips { get; }
}

internal sealed class CsdtRealtimeTargetConflictException : CsdtRealtimeSchemaException
{
    internal CsdtRealtimeTargetConflictException(string message)
        : base(message)
    {
    }
}

internal static class CsdtReverseAtomicExecutionPolicy
{
    internal static IReadOnlyDictionary<string, string> BuildRollbackStatuses(
        IReadOnlyList<string> orderedDomains,
        IReadOnlyList<string> attemptedDomains,
        IReadOnlyList<CsdtReverseAtomicWriteDomainResult> optionalSkips)
    {
        var attempted = attemptedDomains.ToHashSet(StringComparer.Ordinal);
        var skipped = optionalSkips
            .Select(domain => domain.Domain)
            .ToHashSet(StringComparer.Ordinal);
        return orderedDomains.ToDictionary(
            domain => domain,
            domain => skipped.Contains(domain)
                ? "SKIPPED"
                : attempted.Contains(domain)
                    ? "FAILED"
                    : "PENDING",
            StringComparer.Ordinal);
    }

    internal static void EnsureMandatoryDomainsCompleted(
        IReadOnlyList<CsdtReverseDomainExecutionResult> results)
    {
        var byDomain = results.ToDictionary(result => result.Domain, StringComparer.Ordinal);
        foreach (var domain in CsdtRealtimeDomainCatalog.Ordered.Where(domain => !domain.IsOptional))
        {
            if (!byDomain.TryGetValue(domain.Name, out var result) ||
                !string.Equals(result.Status, "SUCCEEDED", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Reverse command cannot complete while mandatory domain {domain.Name} is incomplete.");
            }
        }
    }
}

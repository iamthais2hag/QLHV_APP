using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace QLHV.Application.Sync.Realtime.ControlPlane;

public static class CsdtAtomicCoreDomains
{
    public static IReadOnlyList<string> ApplyOrder { get; } =
        Array.AsReadOnly(
        [
            "DM_DonViGTVT",
            "KhoaHoc",
            "BaoCaoI",
            "NguoiLX",
            "NguoiLX_HoSo",
            "NguoiLXHS_GiayTo",
        ]);

    public static IReadOnlySet<string> Names { get; } =
        new HashSet<string>(ApplyOrder, StringComparer.Ordinal);

    public static void RequireExactScope(IEnumerable<string> domains)
    {
        ArgumentNullException.ThrowIfNull(domains);
        var requested = domains.ToArray();
        if (!requested.SequenceEqual(ApplyOrder, StringComparer.Ordinal))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.AtomicCoreScopeMismatch);
        }
    }
}

public static class CsdtAtomicCycleErrorCodes
{
    public const string FeatureDisabled = "ATOMIC_CYCLE_FEATURE_DISABLED";
    public const string SnapshotNotEnabled = "SNAPSHOT_NOT_ENABLED";
    public const string CtNotEnabled = "CT_NOT_ENABLED";
    public const string CtTableNotTracked = "CT_TABLE_NOT_TRACKED";
    public const string CtCheckpointExpired = "CT_CHECKPOINT_EXPIRED";
    public const string SchemaMismatch = "SCHEMA_MISMATCH";
    public const string ProfileMismatch = "PROFILE_MISMATCH";
    public const string SourceStageIncomplete = "SOURCE_STAGE_INCOMPLETE";
    public const string AtomicCoreScopeMismatch = "ATOMIC_CORE_SCOPE_MISMATCH";
    public const string DeleteExecutionNotEnabled = "DELETE_EXECUTION_NOT_ENABLED";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string ParentMissing = "PARENT_MISSING";
    public const string UnknownColumn = "UNKNOWN_COLUMN";
    public const string InvalidTrainingState = "INVALID_TT_XULY";
    public const string TargetLockTimeout = "TARGET_LOCK_TIMEOUT";
    public const string TargetCommitNotVerified = "TARGET_COMMIT_NOT_VERIFIED";
    public const string DomainResultHashMismatch = "DOMAIN_RESULT_HASH_MISMATCH";
    public const string CheckpointMismatch = "CHECKPOINT_MISMATCH";
    public const string CheckpointConflict = "CHECKPOINT_CONFLICT";
    public const string CheckpointStale = "CHECKPOINT_STALE";
    public const string CoverageIncomplete = "COVERAGE_INCOMPLETE";
    public const string FingerprintMismatch = "MAPPING_FINGERPRINT_MISMATCH";
}

public enum CsdtSourceCapabilityStatus
{
    Ready,
    SnapshotNotEnabled,
    CtNotEnabled,
    CtTableNotTracked,
    CtCheckpointExpired,
    SchemaMismatch,
    ProfileMismatch,
}

public enum CsdtAtomicOperationMode
{
    FullSnapshot,
    Incremental,
}

public enum CsdtStagedChangeOperation
{
    Insert,
    Update,
    Delete,
}

public enum CsdtAtomicCycleOutcome
{
    Complete,
    Failed,
    Conflict,
    RebuildRequired,
}

public sealed record CsdtAtomicCycleRequest(
    Guid CycleId,
    CsdtRealtimeRouteDefinition Route,
    long StartSourceVersion,
    CsdtAtomicOperationMode OperationMode,
    ControlPlaneFingerprint MappingFingerprint,
    ControlPlaneFingerprint RouteFingerprint,
    ControlPlaneFingerprint SourceSchemaFingerprint,
    ControlPlaneFingerprint TargetSchemaFingerprint,
    IReadOnlyList<string> RequestedDomains)
{
    public static CsdtAtomicCycleRequest ForCore(
        Guid cycleId,
        CsdtRealtimeRouteDefinition route,
        long startSourceVersion,
        CsdtAtomicOperationMode operationMode,
        ControlPlaneFingerprint mappingFingerprint,
        ControlPlaneFingerprint routeFingerprint,
        ControlPlaneFingerprint sourceSchemaFingerprint,
        ControlPlaneFingerprint targetSchemaFingerprint)
        => new(
            cycleId,
            route,
            startSourceVersion,
            operationMode,
            mappingFingerprint,
            routeFingerprint,
            sourceSchemaFingerprint,
            targetSchemaFingerprint,
            CsdtAtomicCoreDomains.ApplyOrder);
}

public sealed record CsdtSourceCapabilityResult(
    CsdtSourceCapabilityStatus Status,
    long? CurrentVersion,
    IReadOnlyDictionary<string, long> MinimumValidVersions,
    ControlPlaneFingerprint? ObservedSchemaFingerprint)
{
    public bool IsReady => Status == CsdtSourceCapabilityStatus.Ready;

    public string ErrorCode => Status switch
    {
        CsdtSourceCapabilityStatus.Ready => string.Empty,
        CsdtSourceCapabilityStatus.SnapshotNotEnabled =>
            CsdtAtomicCycleErrorCodes.SnapshotNotEnabled,
        CsdtSourceCapabilityStatus.CtNotEnabled =>
            CsdtAtomicCycleErrorCodes.CtNotEnabled,
        CsdtSourceCapabilityStatus.CtTableNotTracked =>
            CsdtAtomicCycleErrorCodes.CtTableNotTracked,
        CsdtSourceCapabilityStatus.CtCheckpointExpired =>
            CsdtAtomicCycleErrorCodes.CtCheckpointExpired,
        CsdtSourceCapabilityStatus.SchemaMismatch =>
            CsdtAtomicCycleErrorCodes.SchemaMismatch,
        CsdtSourceCapabilityStatus.ProfileMismatch =>
            CsdtAtomicCycleErrorCodes.ProfileMismatch,
        _ => throw new ArgumentOutOfRangeException(),
    };

    public override string ToString()
        => $"CsdtSourceCapabilityResult(Status={Status}, CurrentVersion={CurrentVersion?.ToString() ?? "none"}, Domains={MinimumValidVersions.Count})";
}

public sealed class CsdtStagedRow
{
    private readonly byte[] _canonicalKey;
    private readonly ReadOnlyDictionary<string, object?> _values;

    public CsdtStagedRow(
        ReadOnlySpan<byte> canonicalKey,
        IReadOnlyDictionary<string, object?> values)
    {
        if (canonicalKey.IsEmpty)
        {
            throw new ArgumentException("A staged row requires a canonical key.", nameof(canonicalKey));
        }

        ArgumentNullException.ThrowIfNull(values);
        _canonicalKey = canonicalKey.ToArray();
        _values = new ReadOnlyDictionary<string, object?>(
            values.OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(
                    item => item.Key,
                    item => CopyValue(item.Value),
                    StringComparer.Ordinal));
    }

    public int ColumnCount => _values.Count;

    public byte[] CopyCanonicalKey() => _canonicalKey.ToArray();

    public IReadOnlyDictionary<string, object?> CopyValues()
        => new ReadOnlyDictionary<string, object?>(
            _values.ToDictionary(
                item => item.Key,
                item => CopyValue(item.Value),
                StringComparer.Ordinal));

    public object? ReadValue(string column)
        => _values.TryGetValue(column, out var value)
            ? CopyValue(value)
            : throw new ArgumentException(
                "The requested staged column is absent.",
                nameof(column));

    public bool ContainsColumn(string column) => _values.ContainsKey(column);

    public override string ToString()
        => $"CsdtStagedRow(Columns={ColumnCount}, Key=redacted, Values=redacted)";

    private static object? CopyValue(object? value)
        => value switch
        {
            null => null,
            DBNull => null,
            byte[] bytes => bytes.ToArray(),
            char[] chars => chars.ToArray(),
            _ => value,
        };
}

public sealed class CsdtStagedChange
{
    private readonly byte[] _canonicalKey;

    public CsdtStagedChange(
        long sourceVersion,
        CsdtStagedChangeOperation operation,
        ReadOnlySpan<byte> canonicalKey)
    {
        if (sourceVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceVersion));
        }

        if (canonicalKey.IsEmpty)
        {
            throw new ArgumentException("A staged change requires a canonical key.", nameof(canonicalKey));
        }

        SourceVersion = sourceVersion;
        Operation = operation;
        _canonicalKey = canonicalKey.ToArray();
    }

    public long SourceVersion { get; }

    public CsdtStagedChangeOperation Operation { get; }

    public byte[] CopyCanonicalKey() => _canonicalKey.ToArray();

    public override string ToString()
        => $"CsdtStagedChange(Version={SourceVersion}, Operation={Operation}, Key=redacted)";
}

public sealed class CsdtStagedDomain
{
    private readonly ReadOnlyCollection<CsdtStagedRow> _rows;
    private readonly ReadOnlyCollection<CsdtStagedChange> _changes;
    private readonly ReadOnlyCollection<byte[]> _completeKeys;

    public CsdtStagedDomain(
        string domainName,
        CsdtAtomicOperationMode operationMode,
        IEnumerable<CsdtStagedRow> rows,
        IEnumerable<CsdtStagedChange> changes,
        IEnumerable<byte[]> completeKeys,
        ControlPlaneFingerprint sourceKeySetHash,
        ControlPlaneFingerprint stageResultHash,
        IEnumerable<string>? unknownColumns = null)
    {
        if (!CsdtAtomicCoreDomains.Names.Contains(domainName))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.AtomicCoreScopeMismatch);
        }

        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(completeKeys);
        DomainName = domainName;
        OperationMode = operationMode;
        _rows = Array.AsReadOnly(
            rows.OrderBy(
                    row => Convert.ToHexString(row.CopyCanonicalKey()),
                    StringComparer.Ordinal)
                .ToArray());
        _changes = Array.AsReadOnly(
            changes.OrderBy(change => change.SourceVersion)
                .ThenBy(change => (int)change.Operation)
                .ThenBy(
                    change => Convert.ToHexString(change.CopyCanonicalKey()),
                    StringComparer.Ordinal)
                .ToArray());
        _completeKeys = Array.AsReadOnly(
            completeKeys.Select(key => key.ToArray())
                .OrderBy(Convert.ToHexString, StringComparer.Ordinal)
                .ToArray());
        SourceKeySetHash = sourceKeySetHash;
        StageResultHash = stageResultHash;
        UnknownColumns = Array.AsReadOnly(
            (unknownColumns ?? [])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());

        EnsureUnique(_rows.Select(row => row.CopyCanonicalKey()), "row");
        EnsureUnique(_completeKeys, "complete key");
        if (operationMode == CsdtAtomicOperationMode.FullSnapshot &&
            _completeKeys.Count != _rows.Count)
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.SourceStageIncomplete);
        }
    }

    public string DomainName { get; }

    public CsdtAtomicOperationMode OperationMode { get; }

    public IReadOnlyList<CsdtStagedRow> Rows => _rows;

    public IReadOnlyList<CsdtStagedChange> Changes => _changes;

    public IReadOnlyList<byte[]> CompleteKeys =>
        _completeKeys.Select(key => key.ToArray()).ToArray();

    public long SourceRowCount => _rows.Count;

    public long DeleteCount =>
        _changes.LongCount(change => change.Operation == CsdtStagedChangeOperation.Delete);

    public ControlPlaneFingerprint SourceKeySetHash { get; }

    public ControlPlaneFingerprint StageResultHash { get; }

    public IReadOnlyList<string> UnknownColumns { get; }

    public override string ToString()
        => $"CsdtStagedDomain(Name={DomainName}, Mode={OperationMode}, Rows={SourceRowCount}, Changes={Changes.Count}, Deletes={DeleteCount})";

    private static void EnsureUnique(IEnumerable<byte[]> keys, string kind)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (!unique.Add(Convert.ToHexString(key)))
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.ValidationFailed,
                    $"A staged domain contains a duplicate {kind}.");
            }
        }
    }
}

public sealed class CsdtStagedCycle
{
    private readonly ReadOnlyCollection<CsdtStagedDomain> _domains;

    public CsdtStagedCycle(
        Guid cycleId,
        string sourceProfile,
        string targetProfile,
        string streamCode,
        string maCsdt,
        long startSourceVersion,
        long endSourceVersion,
        ControlPlaneFingerprint mappingFingerprint,
        ControlPlaneFingerprint routeFingerprint,
        ControlPlaneFingerprint sourceSchemaFingerprint,
        ControlPlaneFingerprint targetSchemaFingerprint,
        DateTimeOffset stageCreatedAtUtc,
        ushort keySchemaVersion,
        string targetEqualityProofId,
        IEnumerable<CsdtStagedDomain> domains,
        ControlPlaneFingerprint stagedKeySetHash)
    {
        if (cycleId == Guid.Empty)
        {
            throw new ArgumentException("CycleId cannot be empty.", nameof(cycleId));
        }

        if (startSourceVersion < 0 || endSourceVersion < startSourceVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(endSourceVersion));
        }

        ArgumentNullException.ThrowIfNull(domains);
        var materialized = domains.ToArray();
        CsdtAtomicCoreDomains.RequireExactScope(
            materialized.Select(domain => domain.DomainName));
        if (materialized.Any(domain => domain.OperationMode != materialized[0].OperationMode))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.SourceStageIncomplete);
        }

        CycleId = cycleId;
        SourceProfile = RequireToken(sourceProfile, nameof(sourceProfile));
        TargetProfile = RequireToken(targetProfile, nameof(targetProfile));
        StreamCode = RequireToken(streamCode, nameof(streamCode));
        MaCsdt = RequireToken(maCsdt, nameof(maCsdt));
        StartSourceVersion = startSourceVersion;
        EndSourceVersion = endSourceVersion;
        MappingFingerprint = mappingFingerprint;
        RouteFingerprint = routeFingerprint;
        SourceSchemaFingerprint = sourceSchemaFingerprint;
        TargetSchemaFingerprint = targetSchemaFingerprint;
        StageCreatedAtUtc = stageCreatedAtUtc;
        KeySchemaVersion = keySchemaVersion;
        TargetEqualityProofId = RequireToken(
            targetEqualityProofId,
            nameof(targetEqualityProofId));
        _domains = Array.AsReadOnly(materialized);
        StagedKeySetHash = stagedKeySetHash;
    }

    public Guid CycleId { get; }

    public string SourceProfile { get; }

    public string TargetProfile { get; }

    public string StreamCode { get; }

    public string MaCsdt { get; }

    public long StartSourceVersion { get; }

    public long EndSourceVersion { get; }

    public CsdtAtomicOperationMode OperationMode => _domains[0].OperationMode;

    public ControlPlaneFingerprint MappingFingerprint { get; }

    public ControlPlaneFingerprint RouteFingerprint { get; }

    public ControlPlaneFingerprint SourceSchemaFingerprint { get; }

    public ControlPlaneFingerprint TargetSchemaFingerprint { get; }

    public DateTimeOffset StageCreatedAtUtc { get; }

    public ushort KeySchemaVersion { get; }

    public string TargetEqualityProofId { get; }

    public IReadOnlyList<CsdtStagedDomain> Domains => _domains;

    public ControlPlaneFingerprint StagedKeySetHash { get; }

    public long DeleteCount => Domains.Sum(domain => domain.DeleteCount);

    public override string ToString()
        => $"CsdtStagedCycle(CycleId={CycleId:D}, Stream={StreamCode}, Watermark={EndSourceVersion}, Domains={Domains.Count}, Rows={Domains.Sum(domain => domain.SourceRowCount)}, Deletes={DeleteCount})";

    private static string RequireToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A staged cycle identity token is required.", parameterName);
        }

        return value;
    }
}

public sealed record CsdtAtomicDomainCommitResult(
    string DomainName,
    long SourceRowCount,
    long InsertCount,
    long UpdateCount,
    long SkippedCount,
    ControlPlaneFingerprint SourceKeySetHash,
    ControlPlaneFingerprint ResultHash);

public sealed record CsdtTargetCycleCommitMarker(
    Guid CycleId,
    string SourceProfile,
    string TargetProfile,
    string StreamCode,
    string MaCsdt,
    long StartSourceVersion,
    long EndSourceVersion,
    SyncCycleStatus Status,
    int EnabledDomainCount,
    ControlPlaneFingerprint MappingFingerprint,
    ControlPlaneFingerprint RouteFingerprint,
    ControlPlaneFingerprint? StagedKeySetHash,
    IReadOnlyList<CsdtAtomicDomainCommitResult> Domains,
    ControlPlaneFingerprint? SourceSchemaFingerprint = null,
    ControlPlaneFingerprint? TargetSchemaFingerprint = null);

public enum CsdtCheckpointStatus
{
    Active,
    Conflict,
    Disabled,
}

public sealed record CsdtGlobalCheckpoint(
    Guid CycleId,
    string SourceProfile,
    string TargetProfile,
    string StreamCode,
    long SourceWatermark,
    ControlPlaneFingerprint MappingFingerprint,
    ControlPlaneFingerprint RouteFingerprint,
    ControlPlaneFingerprint StagedKeySetHash,
    ControlPlaneFingerprint? SourceSchemaFingerprint = null,
    ControlPlaneFingerprint? TargetSchemaFingerprint = null,
    CsdtCheckpointStatus Status = CsdtCheckpointStatus.Active,
    DateTimeOffset? PublishedAtUtc = null,
    DateTimeOffset? VerifiedAtUtc = null)
{
    public override string ToString()
        => $"CsdtGlobalCheckpoint(Target={TargetProfile}, Source={SourceProfile}, Stream={StreamCode}, Version={SourceWatermark}, Status={Status}, Keys=redacted)";
}

public sealed record CsdtAtomicCycleResult(
    Guid CycleId,
    CsdtAtomicCycleOutcome Outcome,
    SyncCycleStatus? Status,
    long? PublishedWatermark,
    string? ErrorCode);

public interface ICsdtSourceCycleReader
{
    Task<CsdtSourceCapabilityResult> PreflightAsync(
        CsdtAtomicCycleRequest request,
        CancellationToken cancellationToken = default);

    Task<ICsdtMappedTableSourceSnapshot> OpenSnapshotAsync(
        CsdtAtomicCycleRequest request,
        CsdtSourceCapabilityResult preflight,
        CancellationToken cancellationToken = default);
}

public interface ICsdtMappedTableSourceSnapshot : IAsyncDisposable
{
    long Watermark { get; }

    Task<CsdtStagedCycle> StageCoreAsync(
        CancellationToken cancellationToken = default);
}

public interface ICsdtAtomicCycleJournal
{
    Task CreatePreparingAsync(
        CsdtAtomicCycleRequest request,
        long watermark,
        CancellationToken cancellationToken = default);

    Task MarkStagedAsync(
        CsdtStagedCycle stagedCycle,
        CancellationToken cancellationToken = default);

    Task MarkValidatedAsync(
        Guid cycleId,
        CancellationToken cancellationToken = default);

    Task MarkFailedOrConflictAsync(
        Guid cycleId,
        SyncCycleStatus status,
        string errorCode,
        CancellationToken cancellationToken = default);

    Task<CsdtTargetCycleCommitMarker?> ReadMarkerAsync(
        Guid cycleId,
        CancellationToken cancellationToken = default);

    Task MarkCheckpointPublishedAsync(
        Guid cycleId,
        CancellationToken cancellationToken = default);

    Task MarkCompleteAsync(
        Guid cycleId,
        CancellationToken cancellationToken = default);
}

public interface ICsdtTargetCycleApplier
{
    Task<CsdtTargetCycleCommitMarker> ApplyAsync(
        CsdtStagedCycle stagedCycle,
        CancellationToken cancellationToken = default);
}

public interface ICsdtGlobalCheckpointStore
{
    Task PublishAsync(
        CsdtGlobalCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    Task<CsdtGlobalCheckpoint?> ReadAsync(
        string sourceProfile,
        string targetProfile,
        string streamCode,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyAsync(
        CsdtGlobalCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    Task MarkConflictAsync(
        string sourceProfile,
        string targetProfile,
        string streamCode,
        CancellationToken cancellationToken = default);
}

public sealed class CsdtAtomicCycleException : InvalidOperationException
{
    public CsdtAtomicCycleException(string errorCode, string? safeDetail = null)
        : base(safeDetail is null ? errorCode : $"{errorCode}: {safeDetail}")
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException("An allowlisted error code is required.", nameof(errorCode));
        }

        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

public static class CsdtAtomicHash
{
    public static ControlPlaneFingerprint Compute(params ReadOnlyMemory<byte>[] components)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var component in components)
        {
            hash.AppendData(BitConverter.GetBytes(component.Length));
            hash.AppendData(component.Span);
        }

        return new ControlPlaneFingerprint(hash.GetHashAndReset());
    }

    public static ControlPlaneFingerprint ComputeText(params string[] components)
        => Compute(components.Select(value =>
            (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(value)).ToArray());
}

public static class CsdtAtomicRouteFingerprint
{
    public static ControlPlaneFingerprint Compute(CsdtRealtimeRouteDefinition route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return CsdtAtomicHash.ComputeText(
            route.SourceProfileCode,
            route.TargetProfileCode,
            route.StreamCode,
            route.MaCSDT,
            route.SourceDatabaseName,
            route.TargetDatabaseName,
            route.IsBackup ? "BAK" : "LIVE",
            route.Direction);
    }
}

public static class CsdtAtomicStageFactory
{
    public static CsdtStagedDomain CreateDomain(
        string domainName,
        CsdtAtomicOperationMode operationMode,
        IEnumerable<CsdtStagedRow> rows,
        IEnumerable<CsdtStagedChange>? changes = null,
        IEnumerable<byte[]>? completeKeys = null,
        IEnumerable<string>? unknownColumns = null)
    {
        var materializedRows = rows.ToArray();
        var materializedChanges = (changes ?? []).ToArray();
        var materializedKeys = (completeKeys ??
                materializedRows.Select(row => row.CopyCanonicalKey()))
            .Select(key => key.ToArray())
            .ToArray();
        return new CsdtStagedDomain(
            domainName,
            operationMode,
            materializedRows,
            materializedChanges,
            materializedKeys,
            ComputeKeySetHash(materializedKeys),
            ComputeDomainResultHash(
                domainName,
                operationMode,
                materializedRows,
                materializedChanges,
                materializedKeys),
            unknownColumns);
    }

    public static ControlPlaneFingerprint ComputeKeySetHash(
        IEnumerable<byte[]> keys)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var key in keys
                     .Select(value => value.ToArray())
                     .OrderBy(Convert.ToHexString, StringComparer.Ordinal))
        {
            Append(hash, key);
        }

        return new ControlPlaneFingerprint(hash.GetHashAndReset());
    }

    public static ControlPlaneFingerprint ComputeDomainResultHash(
        string domainName,
        CsdtAtomicOperationMode operationMode,
        IEnumerable<CsdtStagedRow> rows,
        IEnumerable<CsdtStagedChange> changes,
        IEnumerable<byte[]> completeKeys)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, Encoding.UTF8.GetBytes(domainName));
        Append(hash, BitConverter.GetBytes((int)operationMode));
        foreach (var row in rows.OrderBy(
                     value => Convert.ToHexString(value.CopyCanonicalKey()),
                     StringComparer.Ordinal))
        {
            Append(hash, row.CopyCanonicalKey());
            foreach (var value in row.CopyValues())
            {
                Append(hash, Encoding.UTF8.GetBytes(value.Key));
                AppendValue(hash, value.Value);
            }
        }

        foreach (var change in changes
                     .OrderBy(value => value.SourceVersion)
                     .ThenBy(value => (int)value.Operation)
                     .ThenBy(
                         value => Convert.ToHexString(value.CopyCanonicalKey()),
                         StringComparer.Ordinal))
        {
            Append(hash, BitConverter.GetBytes(change.SourceVersion));
            Append(hash, BitConverter.GetBytes((int)change.Operation));
            Append(hash, change.CopyCanonicalKey());
        }

        foreach (var key in completeKeys
                     .Select(value => value.ToArray())
                     .OrderBy(Convert.ToHexString, StringComparer.Ordinal))
        {
            Append(hash, key);
        }

        return new ControlPlaneFingerprint(hash.GetHashAndReset());
    }

    public static ControlPlaneFingerprint ComputeCycleKeySetHash(
        IEnumerable<CsdtStagedDomain> domains)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var domain in domains)
        {
            Append(hash, Encoding.UTF8.GetBytes(domain.DomainName));
            Append(hash, domain.SourceKeySetHash.ToArray());
            Append(hash, domain.StageResultHash.ToArray());
        }

        return new ControlPlaneFingerprint(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        hash.AppendData(BitConverter.GetBytes(value.Length));
        hash.AppendData(value);
    }

    private static void AppendValue(IncrementalHash hash, object? value)
    {
        switch (value)
        {
            case null:
                Append(hash, "<NULL>"u8);
                return;
            case byte[] bytes:
                Append(hash, "<BINARY>"u8);
                Append(hash, bytes);
                return;
            case DateTime dateTime:
                Append(hash, Encoding.UTF8.GetBytes(
                    dateTime.ToUniversalTime().ToString("O")));
                return;
            case DateTimeOffset dateTimeOffset:
                Append(hash, Encoding.UTF8.GetBytes(
                    dateTimeOffset.ToUniversalTime().ToString("O")));
                return;
            case bool boolean:
                Append(hash, boolean ? "1"u8 : "0"u8);
                return;
            case IFormattable formattable:
                Append(hash, Encoding.UTF8.GetBytes(
                    formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ??
                    string.Empty));
                return;
            default:
                Append(hash, Encoding.UTF8.GetBytes(value.ToString() ?? string.Empty));
                return;
        }
    }
}

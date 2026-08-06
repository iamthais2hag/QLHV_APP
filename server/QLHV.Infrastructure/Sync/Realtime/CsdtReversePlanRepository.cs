using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using QLHV.Application.Sync.Realtime;

namespace QLHV.Infrastructure.Sync.Realtime;

internal sealed class CsdtReversePlanRepository :
    ICsdtReversePlanRepository,
    ICsdtReverseCommandExecutor
{
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(5);

    private readonly CsdtRealtimeConnectionResolver _connections;
    private readonly CsdtRealtimeStateRepository _state;
    private readonly CsdtRealtimeSourceReader _reader;
    private readonly CsdtRealtimeTargetWriter _writer;

    public CsdtReversePlanRepository(
        CsdtRealtimeConnectionResolver connections,
        CsdtRealtimeStateRepository state,
        CsdtRealtimeSourceReader reader,
        CsdtRealtimeTargetWriter writer)
    {
        _connections = connections;
        _state = state;
        _reader = reader;
        _writer = writer;
    }

    public async Task<CsdtReversePlanDto> BuildPlanAsync(
        CsdtRealtimeRouteDefinition route,
        string? maKhoaHoc,
        CancellationToken cancellationToken = default)
        => (await ComputeAsync(route, maKhoaHoc, cancellationToken)).Plan;

    public async Task<CsdtReverseCommandExecutionResult> ExecuteAsync(
        CsdtReverseExecutionContext context,
        CsdtRealtimeRouteDefinition reverseRoute,
        string? maKhoaHoc,
        string expectedPlanToken,
        CancellationToken cancellationToken = default)
    {
        CsdtRealtimeIdentityRules.RequirePlanToken(
            expectedPlanToken,
            nameof(expectedPlanToken));

        var recovery = await _state.GetLatestReverseRecoveryAsync(
            context.StreamId,
            maKhoaHoc,
            expectedPlanToken,
            cancellationToken);
        var computation = await ComputeAsync(
            reverseRoute,
            maKhoaHoc,
            cancellationToken);
        if (recovery is null &&
            !string.Equals(
                computation.Plan.PlanToken,
                expectedPlanToken,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Reverse plan is stale; reload the plan before executing.");
        }

        if (!computation.Plan.Executable)
        {
            throw new InvalidOperationException(
                "Reverse plan contains review items or blockers.");
        }

        var intents = CsdtReversePlanEngine.BuildExecutionIntents(
            computation.Domains,
            recovery);
        await _state.InitializeReverseRunAsync(
            context,
            expectedPlanToken,
            maKhoaHoc,
            intents,
            cancellationToken);

        var writes = computation.Domains.Select(domain =>
            new CsdtReverseDomainWrite(
                domain.Domain,
                domain.SafeSnapshot,
                domain.ExpectedTargetHashes,
                domain.Entities.Count)).ToArray();
        var attemptedDomains = writes
            .Where(write => write.Snapshot.Rows.Rows.Count > 0)
            .Select(write => write.Domain.Name)
            .ToArray();
        if (attemptedDomains.Length > 0)
        {
            await _state.MarkReverseDomainsRunningAsync(
                context.RunId,
                attemptedDomains,
                cancellationToken);
        }

        var writeResult = await _writer.UpdateExistingAtomicallyAsync(
            computation.Resolved.TargetConnectionString,
            writes,
            reverseRoute.MaCSDT,
            cancellationToken);
        var intentByDomain = intents.ToDictionary(
            intent => intent.Domain,
            StringComparer.Ordinal);
        var attemptSet = attemptedDomains.ToHashSet(StringComparer.Ordinal);
        return new CsdtReverseCommandExecutionResult(
            writeResult.UpdatedRows,
            expectedPlanToken,
            writeResult.Domains.Select(domain =>
            {
                var intent = intentByDomain[domain.Domain];
                return new CsdtReverseDomainExecutionResult(
                    domain.Domain,
                    domain.Status,
                    domain.SourceRows,
                    domain.UpdatedRows,
                    domain.SkippedRows,
                    intent.AttemptCount + (attemptSet.Contains(domain.Domain) ? 1 : 0),
                    domain.ErrorCode,
                    domain.ErrorMessage);
            }).ToArray(),
            recovery is not null,
            writeResult.HasOptionalSkips);
    }

    private async Task<CsdtReverseComputation> ComputeAsync(
        CsdtRealtimeRouteDefinition reverseRoute,
        string? maKhoaHoc,
        CancellationToken cancellationToken)
    {
        ValidateReverseRoute(reverseRoute);
        ValidateCourseFilter(maKhoaHoc, reverseRoute.MaCSDT);

        var resolved = await _connections.ResolveAsync(reverseRoute, cancellationToken);
        var runtime = await _state.GetRuntimeStreamAsync(
            reverseRoute.StreamCode,
            cancellationToken);
        var ledger = await _state.GetEntityLedgerAsync(
            runtime.StreamId,
            cancellationToken);

        var rawPairs = new List<CsdtReverseSnapshotPair>(CsdtRealtimeDomainCatalog.Ordered.Count);
        foreach (var domain in CsdtRealtimeDomainCatalog.Ordered)
        {
            var sourceTask = _reader.ReadPartitionSnapshotAsync(
                resolved.SourceConnectionString,
                domain,
                reverseRoute.MaCSDT,
                cancellationToken);
            var targetTask = _reader.ReadPartitionSnapshotAsync(
                resolved.TargetConnectionString,
                domain,
                reverseRoute.MaCSDT,
                cancellationToken);
            await Task.WhenAll(sourceTask, targetTask);
            rawPairs.Add(new CsdtReverseSnapshotPair(
                domain,
                await sourceTask,
                await targetTask));
        }

        var pairs = CsdtReversePlanEngine.ApplyCourseFilter(rawPairs, maKhoaHoc);
        var assessments = pairs.Select(pair =>
            CsdtReversePlanEngine.AssessDomain(
                pair,
                ledger,
                reverseRoute.MaCSDT)).ToArray();
        var generatedAt = DateTimeOffset.UtcNow;
        var expiresAt = NextExpiry(generatedAt);
        var plan = CsdtReversePlanEngine.BuildPlan(
            reverseRoute,
            maKhoaHoc,
            assessments,
            generatedAt,
            expiresAt);

        return new CsdtReverseComputation(resolved, plan, assessments);
    }

    private static DateTimeOffset NextExpiry(DateTimeOffset now)
    {
        var windowTicks = PlanLifetime.Ticks;
        var nextTicks = ((now.UtcTicks / windowTicks) + 1) * windowTicks;
        return new DateTimeOffset(nextTicks, TimeSpan.Zero);
    }

    private static void ValidateReverseRoute(CsdtRealtimeRouteDefinition route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!string.Equals(
                route.Direction,
                CsdtRealtimeDirections.V1ToV2,
                StringComparison.Ordinal) ||
            !CsdtRealtimeStreamCatalog.TryResolveAllowedRoute(
                route.StreamCode,
                route.TargetProfileCode,
                route.SourceProfileCode,
                out var forward) ||
            forward.Reverse() != route)
        {
            throw new ArgumentException(
                "Reverse route is not in the fixed server allowlist.",
                nameof(route));
        }
    }

    private static void ValidateCourseFilter(string? maKhoaHoc, string maCsdt)
    {
        if (maKhoaHoc is not null &&
            !CsdtRealtimeIdentityRules.IsRawCourseCodeOrStorableLegacy(
                maKhoaHoc,
                maCsdt))
        {
            throw new ArgumentException(
                "MaKhoaHoc must be an exact raw course identity.",
                nameof(maKhoaHoc));
        }
    }
}

internal static class CsdtReversePlanEngine
{
    internal static IReadOnlyList<CsdtReverseDomainIntent> BuildExecutionIntents(
        IReadOnlyList<CsdtReverseDomainAssessment> assessments,
        CsdtReverseRecovery? recovery)
    {
        var result = new List<CsdtReverseDomainIntent>(assessments.Count);
        foreach (var assessment in assessments)
        {
            var digest = BuildSourceDigest(assessment);
            var previousAttempts = 0;
            if (recovery is not null)
            {
                if (!recovery.Domains.TryGetValue(assessment.Domain.Name, out var previous) ||
                    !string.Equals(previous.SourceDigest, digest, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"REVERSE_SOURCE_CHANGED: {assessment.Domain.Name} changed after the approved plan.");
                }

                previousAttempts = previous.AttemptCount;
            }

            result.Add(new CsdtReverseDomainIntent(
                assessment.Domain.Name,
                assessment.Domain.IsOptional,
                assessment.Entities.Count,
                digest,
                previousAttempts));
        }

        if (recovery is not null && recovery.Domains.Count != result.Count)
        {
            throw new InvalidOperationException(
                "REVERSE_RECOVERY_STATE_INVALID: approved domain intent is incomplete.");
        }

        return result;
    }

    internal static string BuildSourceDigest(CsdtReverseDomainAssessment assessment)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "QLHV_CSDT_REVERSE_DOMAIN_SOURCE_V1");
        Append(hash, assessment.Domain.Name);
        foreach (var entity in assessment.Entities
                     .OrderBy(item => Convert.ToHexString(item.KeyHash), StringComparer.Ordinal))
        {
            Append(hash, Convert.ToHexString(entity.KeyHash));
            Append(hash, Convert.ToHexString(entity.SourceHash));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static IReadOnlyList<CsdtReverseSnapshotPair> ApplyCourseFilter(
        IReadOnlyList<CsdtReverseSnapshotPair> pairs,
        string? maKhoaHoc)
    {
        if (maKhoaHoc is null)
        {
            return pairs;
        }

        var byDomain = pairs.ToDictionary(pair => pair.Domain.Name, StringComparer.Ordinal);
        var learnerKeys = ExactValues(
            byDomain["NguoiLX_HoSo"].Source.Rows,
            "MaDK",
            row => ExactEquals(row, "MaKhoaHoc", maKhoaHoc));
        var teacherKeys = ExactValues(
            byDomain["KhoaHoc_GiaoVien"].Source.Rows,
            "MaGV",
            row => ExactEquals(row, "MaKH", maKhoaHoc));

        return pairs.Select(pair =>
        {
            var filteredRows = FilterRows(
                pair.Source.Rows,
                row => IsInCourseScope(
                    pair.Domain.Name,
                    row,
                    maKhoaHoc,
                    learnerKeys,
                    teacherKeys));
            return pair with
            {
                Source = new CsdtRealtimeSnapshot(
                    pair.Source.SourceMetadata,
                    filteredRows),
            };
        }).ToArray();
    }

    internal static CsdtReverseDomainAssessment AssessDomain(
        CsdtReverseSnapshotPair pair,
        IReadOnlyList<CsdtRealtimeEntityLedgerRow> ledger,
        string maCsdt)
    {
        EnsureSourceCanBeHashedAsTarget(pair);
        var targetByKey = pair.Target.Rows.Rows.Cast<DataRow>().ToDictionary(
            row => CsdtRealtimeTargetWriter.BuildKeyJson(
                row,
                pair.Target.SourceMetadata),
            StringComparer.Ordinal);
        var ledgerByHash = ledger
            .Where(item => string.Equals(
                item.DomainCode,
                pair.Domain.Name,
                StringComparison.Ordinal))
            .GroupBy(item => Convert.ToHexString(item.EntityKeyHash), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var entities = new List<CsdtReverseEntityAssessment>(pair.Source.Rows.Rows.Count);
        foreach (DataRow sourceRow in pair.Source.Rows.Rows)
        {
            var keyJson = CsdtRealtimeTargetWriter.BuildKeyJson(
                sourceRow,
                pair.Source.SourceMetadata);
            var keyHash = CsdtRealtimeTargetWriter.HashKey(keyJson);
            var keyHashText = Convert.ToHexString(keyHash);
            var sourceHash = CsdtRealtimeTargetWriter.HashRow(
                sourceRow,
                pair.Target.SourceMetadata.WritableColumns);
            targetByKey.TryGetValue(keyJson, out var targetRow);
            var targetHash = targetRow is null
                ? null
                : CsdtRealtimeTargetWriter.HashRow(
                    targetRow,
                    pair.Target.SourceMetadata.WritableColumns);

            ledgerByHash.TryGetValue(keyHashText, out var ledgerCandidates);
            var ledgerRow = ledgerCandidates?.SingleOrDefault(item =>
                string.Equals(item.EntityKey, keyJson, StringComparison.Ordinal));
            var classification = Classify(
                IsValidRawIdentity(pair.Domain, sourceRow, maCsdt),
                targetRow is not null,
                sourceHash,
                targetHash,
                ledgerRow);
            entities.Add(new CsdtReverseEntityAssessment(
                pair.Domain.Name,
                keyJson,
                keyHash,
                sourceHash,
                targetHash,
                ledgerRow?.SourceHash,
                ledgerRow?.TargetHash,
                classification,
                sourceRow));
        }

        var safeRows = pair.Source.Rows.Clone();
        var expectedTargetHashes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entity in entities.Where(item =>
                     item.Classification == CsdtReverseClassification.SafeUpdate))
        {
            safeRows.ImportRow(entity.SourceRow);
            expectedTargetHashes.Add(entity.KeyJson, entity.TargetHash!);
        }

        return new CsdtReverseDomainAssessment(
            pair.Domain,
            entities,
            new CsdtRealtimeSnapshot(pair.Source.SourceMetadata, safeRows),
            expectedTargetHashes);
    }

    internal static CsdtReverseClassification Classify(
        bool identityIsValid,
        bool targetExists,
        byte[] sourceHash,
        byte[]? targetHash,
        CsdtRealtimeEntityLedgerRow? ledger)
    {
        if (!identityIsValid)
        {
            return CsdtReverseClassification.IdentityChanged;
        }

        if (!targetExists || targetHash is null)
        {
            return CsdtReverseClassification.V1OnlyRequiresReview;
        }

        if (ledger?.SourceHash is null ||
            !CryptographicOperations.FixedTimeEquals(targetHash, ledger.SourceHash))
        {
            return CsdtReverseClassification.ConflictRequiresReview;
        }

        return CryptographicOperations.FixedTimeEquals(sourceHash, targetHash)
            ? CsdtReverseClassification.Skipped
            : CsdtReverseClassification.SafeUpdate;
    }

    internal static CsdtReversePlanDto BuildPlan(
        CsdtRealtimeRouteDefinition route,
        string? maKhoaHoc,
        IReadOnlyList<CsdtReverseDomainAssessment> assessments,
        DateTimeOffset generatedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        var all = assessments.SelectMany(item => item.Entities).ToArray();
        var v1Only = Count(all, CsdtReverseClassification.V1OnlyRequiresReview);
        var identityChanged = Count(all, CsdtReverseClassification.IdentityChanged);
        var conflicts = Count(all, CsdtReverseClassification.ConflictRequiresReview);
        var blockers = new List<string>();
        if (all.Length == 0)
        {
            blockers.Add("NO_SOURCE_ROWS: Khong co dong V1 nao trong pham vi da chon.");
        }

        if (v1Only > 0)
        {
            blockers.Add($"V1_ONLY_REQUIRES_REVIEW: {v1Only} dong can Admin review.");
        }

        if (identityChanged > 0)
        {
            blockers.Add($"IDENTITY_CHANGED: {identityChanged} dong can Admin review.");
        }

        if (conflicts > 0)
        {
            blockers.Add($"CONFLICT_REQUIRES_REVIEW: {conflicts} dong can Admin review.");
        }

        return new CsdtReversePlanDto
        {
            IsReadOnly = true,
            VehicleType = route.VehicleType,
            Direction = CsdtRealtimeDirections.V1ToV2,
            SourceDatabaseName = route.SourceDatabaseName,
            TargetDatabaseName = route.TargetDatabaseName,
            MaKhoaHoc = maKhoaHoc,
            GeneratedAtUtc = generatedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            PlanToken = BuildPlanToken(route, maKhoaHoc, assessments, expiresAtUtc),
            SourceRows = all.LongLength,
            SafeInsertRows = 0,
            SafeUpdateRows = Count(all, CsdtReverseClassification.SafeUpdate),
            SkippedRows = Count(all, CsdtReverseClassification.Skipped),
            V1OnlyRequiresReview = v1Only,
            IdentityChanged = identityChanged,
            ConflictRequiresReview = conflicts,
            Executable = blockers.Count == 0,
            Blockers = blockers,
            Warnings =
            [
                "V1 -> V2 chi cap nhat dong da ton tai va an toan; khong insert, delete hoac doi khoa.",
            ],
            Domains = assessments.Select(item => new CsdtReverseDomainPlanDto
            {
                Domain = item.Domain.Name,
                SourceRows = item.Entities.Count,
                SafeInsertRows = 0,
                SafeUpdateRows = Count(
                    item.Entities,
                    CsdtReverseClassification.SafeUpdate),
                SkippedRows = Count(
                    item.Entities,
                    CsdtReverseClassification.Skipped),
                ReviewRows = item.Entities.LongCount(entity =>
                    entity.Classification is
                        CsdtReverseClassification.V1OnlyRequiresReview or
                        CsdtReverseClassification.IdentityChanged or
                        CsdtReverseClassification.ConflictRequiresReview),
            }).ToArray(),
        };
    }

    internal static string BuildPlanToken(
        CsdtRealtimeRouteDefinition route,
        string? maKhoaHoc,
        IReadOnlyList<CsdtReverseDomainAssessment> assessments,
        DateTimeOffset expiresAtUtc)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "QLHV_CSDT_REVERSE_PLAN_V1");
        Append(hash, route.StreamCode);
        Append(hash, route.VehicleType);
        Append(hash, route.SourceProfileCode);
        Append(hash, route.TargetProfileCode);
        Append(hash, route.SourceDatabaseName);
        Append(hash, route.TargetDatabaseName);
        Append(hash, route.MaCSDT);
        Append(hash, maKhoaHoc ?? "<ALL>");
        Append(hash, expiresAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture));

        foreach (var entity in assessments
                     .SelectMany(item => item.Entities)
                     .OrderBy(item => item.Domain, StringComparer.Ordinal)
                     .ThenBy(item => Convert.ToHexString(item.KeyHash), StringComparer.Ordinal))
        {
            Append(hash, entity.Domain);
            Append(hash, Convert.ToHexString(entity.KeyHash));
            Append(hash, Convert.ToHexString(entity.SourceHash));
            Append(hash, entity.TargetHash is null
                ? "<MISSING>"
                : Convert.ToHexString(entity.TargetHash));
            Append(hash, entity.LedgerSourceHash is null
                ? "<NO_LEDGER_SOURCE>"
                : Convert.ToHexString(entity.LedgerSourceHash));
            Append(hash, entity.LedgerTargetHash is null
                ? "<NO_LEDGER_TARGET>"
                : Convert.ToHexString(entity.LedgerTargetHash));
            Append(hash, entity.Classification.ToString());
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static long Count(
        IEnumerable<CsdtReverseEntityAssessment> entities,
        CsdtReverseClassification classification)
        => entities.LongCount(item => item.Classification == classification);

    private static bool IsValidRawIdentity(
        CsdtRealtimeDomainDefinition domain,
        DataRow row,
        string maCsdt)
    {
        foreach (var key in domain.KeyColumns)
        {
            var value = row[key];
            if (value is DBNull)
            {
                return false;
            }

            if (value is string text &&
                (text.Length == 0 ||
                 text.Length > 450 ||
                 char.IsWhiteSpace(text[0]) ||
                 char.IsWhiteSpace(text[^1]) ||
                 text.IndexOfAny(['\0', '\r', '\n']) >= 0))
            {
                return false;
            }

            if (value is IConvertible convertible &&
                value is not string &&
                value is not DateTime &&
                value is not DateTimeOffset &&
                Convert.ToDecimal(convertible, CultureInfo.InvariantCulture) <= 0)
            {
                return false;
            }
        }

        if (domain.Name == "DM_DonViGTVT")
        {
            return ExactEquals(row, "MaDV", maCsdt) &&
                   CsdtRealtimeIdentityRules.IsCurrentMaCsdt(maCsdt);
        }

        if (domain.Name is "KhoaHoc" or "KhoaHoc_GiaoVien")
        {
            return (domain.Name != "KhoaHoc" || ExactEquals(row, "MaCSDT", maCsdt)) &&
                   TryGetString(row, "MaKH", out var maKh) &&
                   CsdtRealtimeIdentityRules.IsRawCourseCodeOrStorableLegacy(maKh, maCsdt);
        }

        if (domain.Name is
            "NguoiLX" or
            "NguoiLX_HoSo" or
            "NguoiLX_GPLX" or
            "NguoiLXHS_GiayTo")
        {
            if (!TryGetString(row, "MaDK", out var maDk) ||
                !CsdtRealtimeIdentityRules.IsRawStudentCodeOrStorableLegacy(
                    maDk,
                    maCsdt))
            {
                return false;
            }

            if (domain.Name == "NguoiLX_HoSo")
            {
                if (!ExactEquals(row, "MaCSDT", maCsdt))
                {
                    return false;
                }

                if (TryGetString(row, "SoGiayCNTN", out var certificate) &&
                    (!TryGetString(row, "HangDaoTao", out var trainingClass) ||
                     !CsdtRealtimeIdentityRules.IsExactCompletionCertificate(
                         certificate,
                         maDk,
                         trainingClass)))
                {
                    return false;
                }
            }

            return true;
        }

        if (domain.Name is "BaoCaoI" or "GiaoVien")
        {
            return ExactEquals(row, "MaCSDT", maCsdt);
        }

        return true;
    }

    private static void EnsureSourceCanBeHashedAsTarget(CsdtReverseSnapshotPair pair)
    {
        var sourceNames = pair.Source.Rows.Columns
            .Cast<DataColumn>()
            .Select(column => column.ColumnName)
            .ToHashSet(StringComparer.Ordinal);
        var missing = pair.Target.SourceMetadata.WritableColumns
            .Where(column => !sourceNames.Contains(column.Name))
            .Select(column => column.Name)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new CsdtRealtimeSchemaException(
                $"dbo.{pair.Domain.TableName} V1 is missing columns required for a safe reverse hash: " +
                string.Join(", ", missing));
        }
    }

    private static bool IsInCourseScope(
        string domain,
        DataRow row,
        string maKhoaHoc,
        IReadOnlySet<string> learnerKeys,
        IReadOnlySet<string> teacherKeys)
        => domain switch
        {
            "DM_DonViGTVT" => false,
            "GiaoVien" => ContainsExact(row, "MaGV", teacherKeys),
            "KhoaHoc" => ExactEquals(row, "MaKH", maKhoaHoc),
            "KhoaHoc_GiaoVien" => ExactEquals(row, "MaKH", maKhoaHoc),
            "BaoCaoI" => ExactEquals(row, "MaKH", maKhoaHoc),
            "NguoiLX" => ContainsExact(row, "MaDK", learnerKeys),
            "NguoiLX_HoSo" => ExactEquals(row, "MaKhoaHoc", maKhoaHoc),
            "NguoiLX_GPLX" => ContainsExact(row, "MaDK", learnerKeys),
            "NguoiLXHS_GiayTo" => ContainsExact(row, "MaDK", learnerKeys),
            _ => false,
        };

    private static HashSet<string> ExactValues(
        DataTable rows,
        string column,
        Func<DataRow, bool> predicate)
        => rows.Rows.Cast<DataRow>()
            .Where(predicate)
            .Select(row => row[column])
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    private static DataTable FilterRows(
        DataTable source,
        Func<DataRow, bool> predicate)
    {
        var filtered = source.Clone();
        foreach (var row in source.Rows.Cast<DataRow>().Where(predicate))
        {
            filtered.ImportRow(row);
        }

        return filtered;
    }

    private static bool ExactEquals(DataRow row, string column, string expected)
        => TryGetString(row, column, out var actual) &&
           string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool ContainsExact(
        DataRow row,
        string column,
        IReadOnlySet<string> expected)
        => TryGetString(row, column, out var actual) && expected.Contains(actual);

    private static bool TryGetString(
        DataRow row,
        string column,
        out string value)
    {
        value = string.Empty;
        if (!row.Table.Columns.Contains(column) || row[column] is not string actual)
        {
            return false;
        }

        value = actual;
        return true;
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }
}

internal enum CsdtReverseClassification
{
    SafeUpdate,
    Skipped,
    V1OnlyRequiresReview,
    IdentityChanged,
    ConflictRequiresReview,
}

internal sealed record CsdtReverseSnapshotPair(
    CsdtRealtimeDomainDefinition Domain,
    CsdtRealtimeSnapshot Source,
    CsdtRealtimeSnapshot Target);

internal sealed record CsdtReverseEntityAssessment(
    string Domain,
    string KeyJson,
    byte[] KeyHash,
    byte[] SourceHash,
    byte[]? TargetHash,
    byte[]? LedgerSourceHash,
    byte[]? LedgerTargetHash,
    CsdtReverseClassification Classification,
    DataRow SourceRow);

internal sealed record CsdtReverseDomainAssessment(
    CsdtRealtimeDomainDefinition Domain,
    IReadOnlyList<CsdtReverseEntityAssessment> Entities,
    CsdtRealtimeSnapshot SafeSnapshot,
    IReadOnlyDictionary<string, byte[]> ExpectedTargetHashes);

internal sealed record CsdtReverseComputation(
    CsdtRealtimeResolvedRoute Resolved,
    CsdtReversePlanDto Plan,
    IReadOnlyList<CsdtReverseDomainAssessment> Domains);

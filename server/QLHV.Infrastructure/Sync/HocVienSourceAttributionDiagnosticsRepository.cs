using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Globalization;
using QLHV.Application.CsdtConnections;
using QLHV.Application.CsdtConnections.Dtos;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

public sealed class HocVienSourceAttributionDiagnosticsRepository : IHocVienSourceAttributionDiagnosticsRepository
{
    private const string AuthModeSqlLogin = "SqlLogin";

    private readonly IConnectionSettingsProvider _connections;
    private readonly ICsdtConnectionProfileRepository _profileRepository;
    private readonly IConnectionPasswordProtector _passwordProtector;
    private readonly SyncOptions _options;

    public HocVienSourceAttributionDiagnosticsRepository(
        IConnectionSettingsProvider connections,
        ICsdtConnectionProfileRepository profileRepository,
        IConnectionPasswordProtector passwordProtector,
        IOptions<SyncOptions> options)
    {
        _connections = connections;
        _profileRepository = profileRepository;
        _passwordProtector = passwordProtector;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<HocVienComparableAttributionRowDto>> ReadTargetRowsAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);

        return await SyncRetryPolicyFactory.CreateDefault(_options.MaxRetryAttempts).ExecuteAsync(async ct =>
        {
            await using var connection = new SqlConnection(connectionString);
            var rows = await connection.QueryAsync<TargetComparableRow>(new CommandDefinition(
                TargetRowsSql,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct));

            return (IReadOnlyList<HocVienComparableAttributionRowDto>)rows
                .Where(row => !string.IsNullOrWhiteSpace(row.MaDK))
                .Select(row => new HocVienComparableAttributionRowDto
                {
                    MaDK = row.MaDK.Trim(),
                    SourceProfileCode = DbTrim(row.SourceProfileCode),
                    HoTenNormalized = NormalizeText(row.HoTen),
                    NgaySinh = row.NgaySinh?.Date,
                    GioiTinh = NormalizeValue(row.GioiTinh),
                    MaKhoa = NormalizeValue(row.MaKhoa),
                    TenKhoa = NormalizeValue(row.TenKhoa),
                    MaHangDT = NormalizeValue(row.MaHangDT),
                    HangGPLXHoc = NormalizeValue(row.HangGPLXHoc),
                    V2RowHash = NormalizeHash(row.V2RowHash),
                })
                .ToList();
        }, cancellationToken);
    }

    public async Task<HocVienSourceComparableReadResultDto> ReadSourceRowsAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = CsdtConnectionProfileCodes.Normalize(sourceProfileCode);
        try
        {
            var profile = await _profileRepository.GetByCodeAsync(normalized, cancellationToken);
            var resolved = ResolveProfileConnectionString(profile, normalized);
            if (!resolved.CanRead)
            {
                return Failed(normalized, resolved.Issue, "SOURCE_PROFILE_NOT_CONFIGURED");
            }

            var comparableRows = await SyncRetryPolicyFactory.CreateDefault(_options.MaxRetryAttempts).ExecuteAsync(async ct =>
            {
                await using var connection = new SqlConnection(resolved.ConnectionString);
                var rows = await connection.QueryAsync<SourceComparableRow>(new CommandDefinition(
                    SourceRowsSql,
                    commandTimeout: _options.TimeoutSeconds,
                    cancellationToken: ct));

                var sourceRows = rows.ToList();
                var comparableRows = sourceRows
                    .Select(ToComparableSourceRow)
                    .Where(row => !string.IsNullOrWhiteSpace(row.MaDK))
                    .GroupBy(row => row.MaDK, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();

                var duplicateCount = sourceRows
                    .Where(row => !string.IsNullOrWhiteSpace(row.MaDK))
                    .GroupBy(row => row.MaDK.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Count(group => group.Count() > 1);
                var invalidNgaySinhCount = sourceRows.Count(HasInvalidNgaySinh);

                return new SourceReadPayload(
                    sourceRows.Count,
                    comparableRows.Count,
                    duplicateCount,
                    invalidNgaySinhCount,
                    comparableRows);
            }, cancellationToken);

            var issue = BuildSourceIssue(normalized, comparableRows);

            return new HocVienSourceComparableReadResultDto
            {
                SourceProfileCode = normalized,
                CanRead = true,
                SourceRows = comparableRows.SourceRows,
                DistinctSourceMaDk = comparableRows.DistinctSourceMaDk,
                DuplicateSourceMaDkCount = comparableRows.DuplicateSourceMaDkCount,
                InvalidNgaySinhCount = comparableRows.InvalidNgaySinhCount,
                Rows = comparableRows.Rows,
                Issue = issue,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SqlException ex)
        {
            return Failed(
                normalized,
                BuildSafeSqlExceptionIssue(normalized, ex),
                "SOURCE_PROFILE_SQL_FAILED");
        }
        catch (Exception ex)
        {
            return Failed(
                normalized,
                $"Khong doc duoc MaDK tu profile {normalized}. Chi tiet: {ex.GetType().Name}.",
                "SOURCE_PROFILE_READ_FAILED");
        }
    }

    private async Task<string> ResolveQlhvAppAsync(CancellationToken cancellationToken)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new InvalidOperationException(
                "QLHV_APP chua co cau hinh ket noi dung duoc (thieu hoac dang la placeholder).");
        }

        return target.ConnectionString;
    }

    private ProfileConnectionResolution ResolveProfileConnectionString(
        CsdtConnectionProfileRecord? profile,
        string sourceProfileCode)
    {
        if (profile is null)
        {
            return ProfileConnectionResolution.Failed($"Profile {sourceProfileCode} khong ton tai.");
        }

        if (string.IsNullOrWhiteSpace(profile.ServerName) || string.IsNullOrWhiteSpace(profile.DatabaseName))
        {
            return ProfileConnectionResolution.Failed(
                $"Profile {sourceProfileCode} chua co ServerName hoac DatabaseName.");
        }

        try
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = profile.ServerName,
                InitialCatalog = profile.DatabaseName,
                ConnectTimeout = Math.Min(Math.Max(_options.TimeoutSeconds, 5), 30),
                TrustServerCertificate = true,
                MultipleActiveResultSets = false,
            };

            if (string.Equals(profile.AuthMode, AuthModeSqlLogin, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(profile.UserName) ||
                    profile.PasswordCipherText is null ||
                    !profile.IsPasswordConfigured)
                {
                    return ProfileConnectionResolution.Failed(
                        $"Profile {sourceProfileCode} SQL Login chua du UserName/password da ma hoa.");
                }

                if (!_passwordProtector.IsAvailable)
                {
                    return ProfileConnectionResolution.Failed(
                        $"Profile {sourceProfileCode} can giai ma password nhung password protector chua san sang.");
                }

                builder.IntegratedSecurity = false;
                builder.UserID = profile.UserName;
                builder.Password = _passwordProtector.Unprotect(profile.PasswordCipherText);
            }
            else
            {
                builder.IntegratedSecurity = true;
            }

            return ProfileConnectionResolution.Success(builder.ConnectionString);
        }
        catch (Exception ex)
        {
            return ProfileConnectionResolution.Failed(
                $"Khong tao duoc ket noi an toan cho profile {sourceProfileCode}. Chi tiet: {ex.GetType().Name}.");
        }
    }

    private static HocVienComparableAttributionRowDto ToComparableSourceRow(SourceComparableRow source) => new()
    {
        MaDK = DbTrim(source.MaDK) ?? string.Empty,
        HoTenNormalized = NormalizeText(source.HoTen),
        NgaySinh = ParseNgaySinh(source.NgaySinhRaw),
        GioiTinh = NormalizeValue(source.GioiTinh),
        MaKhoa = NormalizeValue(source.MaKhoa),
        TenKhoa = NormalizeValue(source.TenKhoa),
        MaHangDT = NormalizeValue(source.MaHangDT),
        HangGPLXHoc = NormalizeValue(source.HangGPLXHoc),
    };

    private static string? BuildSourceIssue(string sourceProfileCode, SourceReadPayload payload)
    {
        var issues = new List<string>();
        if (payload.DuplicateSourceMaDkCount > 0)
        {
            issues.Add($"Profile {sourceProfileCode} co {payload.DuplicateSourceMaDkCount} MaDK bi trung " +
                       $"(sourceRows={payload.SourceRows}, distinctSourceMaDk={payload.DistinctSourceMaDk}). " +
                       "Diagnostics chi lay dong dau tien moi MaDK.");
        }

        if (payload.InvalidNgaySinhCount > 0)
        {
            issues.Add($"Profile {sourceProfileCode} co {payload.InvalidNgaySinhCount} dong NgaySinh khong parse duoc " +
                       "theo dinh dang yyyyMMdd; diagnostics de null cho cac dong nay.");
        }

        return issues.Count == 0 ? null : string.Join(" ", issues);
    }

    private static DateTime? ParseNgaySinh(string? raw)
    {
        var value = DbTrim(raw);
        if (value is null)
        {
            return null;
        }

        return DateTime.TryParseExact(
            value,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed.Date
            : null;
    }

    private static bool HasInvalidNgaySinh(SourceComparableRow source)
        => !string.IsNullOrWhiteSpace(source.NgaySinhRaw) && ParseNgaySinh(source.NgaySinhRaw) is null;

    private static string BuildSafeSqlExceptionIssue(string sourceProfileCode, SqlException ex)
    {
        var message = SanitizeSqlMessage(ex.Message);
        return $"Khong doc duoc attribution source profile {sourceProfileCode}. " +
               $"SqlException.Number={ex.Number}; LineNumber={ex.LineNumber}; Message={message}";
    }

    private static string SanitizeSqlMessage(string message)
    {
        var sanitized = message
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return sanitized.Length <= 500 ? sanitized : sanitized[..500];
    }

    private static string? DbTrim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeHash(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).ToUpperInvariant();
    }

    private static HocVienSourceComparableReadResultDto Failed(
        string sourceProfileCode,
        string issue,
        string code) => new()
    {
        SourceProfileCode = sourceProfileCode,
        CanRead = false,
        Issue = issue,
        Error = new SyncErrorDto
        {
            Code = code,
            Message = issue,
        },
    };

    private const string TargetRowsSql = @"
SELECT
    LTRIM(RTRIM(MaDK)) AS MaDK,
    NULLIF(LTRIM(RTRIM(SourceProfileCode)), '') AS SourceProfileCode,
    HoTen,
    NgaySinh,
    GioiTinh,
    MaKhoa,
    TenKhoa,
    MaHangDT,
    HangGPLXHoc,
    V2RowHash
FROM dbo.App_HocVien
WHERE IsDeleted = 0
  AND NULLIF(LTRIM(RTRIM(MaDK)), '') IS NOT NULL;";

    private const string SourceRowsSql = @"
SELECT
    hs.MaDK                               AS MaDK,
    nlx.HoVaTen                           AS HoTen,
    nlx.NgaySinh                          AS NgaySinhRaw,
    nlx.GioiTinh                          AS GioiTinh,
    hs.MaKhoaHoc                          AS MaKhoa,
    kh.TenKH                              AS TenKhoa,
    hs.HangDaoTao                         AS MaHangDT,
    hdt.TenHangDT                         AS HangGPLXHoc
FROM dbo.NguoiLX AS nlx
INNER JOIN dbo.NguoiLX_HoSo AS hs ON hs.MaDK = nlx.MaDK
LEFT JOIN dbo.KhoaHoc AS kh ON kh.MaKH = hs.MaKhoaHoc
LEFT JOIN dbo.DM_HangDT AS hdt ON hdt.MaHangDT = hs.HangDaoTao
WHERE NULLIF(LTRIM(RTRIM(hs.MaDK)), '') IS NOT NULL;";

    private sealed class ProfileConnectionResolution
    {
        public bool CanRead { get; private init; }

        public string ConnectionString { get; private init; } = string.Empty;

        public string Issue { get; private init; } = string.Empty;

        public static ProfileConnectionResolution Success(string connectionString) => new()
        {
            CanRead = true,
            ConnectionString = connectionString,
        };

        public static ProfileConnectionResolution Failed(string issue) => new()
        {
            CanRead = false,
            Issue = issue,
        };
    }

    private sealed class TargetComparableRow
    {
        public string MaDK { get; init; } = string.Empty;
        public string? SourceProfileCode { get; init; }
        public string? HoTen { get; init; }
        public DateTime? NgaySinh { get; init; }
        public string? GioiTinh { get; init; }
        public string? MaKhoa { get; init; }
        public string? TenKhoa { get; init; }
        public string? MaHangDT { get; init; }
        public string? HangGPLXHoc { get; init; }
        public string? V2RowHash { get; init; }
    }

    private sealed class SourceComparableRow
    {
        public string MaDK { get; init; } = string.Empty;
        public string? HoTen { get; init; }
        public string? NgaySinhRaw { get; init; }
        public string? GioiTinh { get; init; }
        public string? MaKhoa { get; init; }
        public string? TenKhoa { get; init; }
        public string? MaHangDT { get; init; }
        public string? HangGPLXHoc { get; init; }
    }

    private sealed record SourceReadPayload(
        int SourceRows,
        int DistinctSourceMaDk,
        int DuplicateSourceMaDkCount,
        int InvalidNgaySinhCount,
        IReadOnlyCollection<HocVienComparableAttributionRowDto> Rows);
}

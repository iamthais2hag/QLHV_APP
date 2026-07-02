using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using System.Text.RegularExpressions;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Infrastructure.Sync;

public sealed class MotoSyncRunHistoryRepository : IMotoSyncRunHistoryRepository
{
    internal const int DefaultTake = 50;
    internal const int MaxTake = 200;
    internal const string ListOrderBySql = "ORDER BY StartedAt DESC, Id DESC";

    private static readonly Regex SecretTokenRegex = new(
        @"\b(Password|Pwd|User ID|User Id|UID)\s*=\s*[^;]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IConnectionSettingsProvider _connections;
    private readonly AppSyncOptions _options;

    public MotoSyncRunHistoryRepository(
        IConnectionSettingsProvider connections,
        IOptions<AppSyncOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task<long> CreateAsync(
        MotoSyncRunHistoryCreateDto entry,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var command = new CommandDefinition(
            InsertSql,
            new
            {
                Direction = entry.Direction.ToString(),
                SyncMode = entry.SyncMode.ToString(),
                entry.SourceProfileCode,
                entry.TargetProfileCode,
                MaKhoaHoc = DbTrim(entry.MaKhoaHoc),
                entry.ConfirmTextMatched,
                entry.Executed,
                entry.Status,
                Message = Limit(Sanitize(entry.Message), 1000),
                entry.InsertedKhoaHoc,
                entry.InsertedBaoCaoI,
                entry.InsertedNguoiLX,
                entry.InsertedNguoiLXGPLX,
                entry.InsertedNguoiLXHoSo,
                entry.InsertedGiayTo,
                entry.UpdatedNguoiLX,
                entry.UpdatedNguoiLXHoSo,
                entry.UpdatedRows,
                entry.DeletedRows,
                entry.DurationMs,
                entry.StartedAt,
                entry.EndedAt,
                entry.HasRemainingWork,
                BeforePlanJson = Sanitize(entry.BeforePlanJson),
                AfterPlanJson = Sanitize(entry.AfterPlanJson),
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<long>(command);
    }

    public async Task<IReadOnlyList<MotoSyncRunHistoryListItemDto>> SearchAsync(
        MotoSyncRunHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        query ??= new MotoSyncRunHistoryQuery();
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<RunHistoryRow>(new CommandDefinition(
            SelectListSql,
            new
            {
                MaKhoaHoc = DbTrim(query.MaKhoaHoc),
                Direction = query.Direction?.ToString(),
                SyncMode = query.SyncMode?.ToString(),
                Take = NormalizeTake(query.Take),
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        return rows.Select(ToListItem).ToList();
    }

    public async Task<MotoSyncRunHistoryDetailDto?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var row = await connection.QuerySingleOrDefaultAsync<RunHistoryRow>(new CommandDefinition(
            SelectDetailSql,
            new { Id = id },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        return row is null ? null : ToDetail(row);
    }

    internal static int NormalizeTake(int take)
        => take <= 0 ? DefaultTake : Math.Min(take, MaxTake);

    internal static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return SecretTokenRegex.Replace(
            value,
            match => $"{match.Groups[1].Value}=<masked>");
    }

    private async Task<string> ResolveQlhvAppAsync(CancellationToken cancellationToken)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new InvalidOperationException(
                "QLHV_APP chua co cau hinh ket noi dung duoc de ghi lich su Moto sync.");
        }

        return target.ConnectionString;
    }

    private static MotoSyncRunHistoryListItemDto ToListItem(RunHistoryRow row) => new()
    {
        Id = row.Id,
        CreatedAt = row.CreatedAt,
        Direction = ParseDirection(row.Direction),
        SyncMode = ParseSyncMode(row.SyncMode),
        SourceProfileCode = row.SourceProfileCode,
        TargetProfileCode = row.TargetProfileCode,
        MaKhoaHoc = row.MaKhoaHoc,
        Executed = row.Executed,
        Status = row.Status,
        Message = row.Message,
        InsertedTotal = row.InsertedKhoaHoc +
                        row.InsertedBaoCaoI +
                        row.InsertedNguoiLX +
                        row.InsertedNguoiLXGPLX +
                        row.InsertedNguoiLXHoSo +
                        row.InsertedGiayTo,
        UpdatedRows = row.UpdatedRows,
        DeletedRows = row.DeletedRows,
        DurationMs = row.DurationMs,
        HasRemainingWork = row.HasRemainingWork,
    };

    private static MotoSyncRunHistoryDetailDto ToDetail(RunHistoryRow row) => new()
    {
        Id = row.Id,
        CreatedAt = row.CreatedAt,
        Direction = ParseDirection(row.Direction),
        SyncMode = ParseSyncMode(row.SyncMode),
        SourceProfileCode = row.SourceProfileCode,
        TargetProfileCode = row.TargetProfileCode,
        MaKhoaHoc = row.MaKhoaHoc,
        Executed = row.Executed,
        Status = row.Status,
        Message = row.Message,
        InsertedTotal = row.InsertedKhoaHoc +
                        row.InsertedBaoCaoI +
                        row.InsertedNguoiLX +
                        row.InsertedNguoiLXGPLX +
                        row.InsertedNguoiLXHoSo +
                        row.InsertedGiayTo,
        UpdatedRows = row.UpdatedRows,
        DeletedRows = row.DeletedRows,
        DurationMs = row.DurationMs,
        HasRemainingWork = row.HasRemainingWork,
        ConfirmTextMatched = row.ConfirmTextMatched,
        InsertedKhoaHoc = row.InsertedKhoaHoc,
        InsertedBaoCaoI = row.InsertedBaoCaoI,
        InsertedNguoiLX = row.InsertedNguoiLX,
        InsertedNguoiLXGPLX = row.InsertedNguoiLXGPLX,
        InsertedNguoiLXHoSo = row.InsertedNguoiLXHoSo,
        InsertedGiayTo = row.InsertedGiayTo,
        UpdatedNguoiLX = row.UpdatedNguoiLX,
        UpdatedNguoiLXHoSo = row.UpdatedNguoiLXHoSo,
        StartedAt = row.StartedAt,
        EndedAt = row.EndedAt,
        BeforePlanJson = row.BeforePlanJson,
        AfterPlanJson = row.AfterPlanJson,
    };

    private static MotoSyncDirection ParseDirection(string value)
        => Enum.TryParse<MotoSyncDirection>(value, ignoreCase: true, out var parsed)
            ? parsed
            : default;

    private static MotoSyncMode ParseSyncMode(string value)
        => Enum.TryParse<MotoSyncMode>(value, ignoreCase: true, out var parsed)
            ? parsed
            : default;

    private static string? DbTrim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Limit(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];

    private const string InsertSql = @"
INSERT INTO dbo.App_MotoSyncRunHistory
(
    Direction,
    SyncMode,
    SourceProfileCode,
    TargetProfileCode,
    MaKhoaHoc,
    ConfirmTextMatched,
    Executed,
    Status,
    Message,
    InsertedKhoaHoc,
    InsertedBaoCaoI,
    InsertedNguoiLX,
    InsertedNguoiLXGPLX,
    InsertedNguoiLXHoSo,
    InsertedGiayTo,
    UpdatedNguoiLX,
    UpdatedNguoiLXHoSo,
    UpdatedRows,
    DeletedRows,
    DurationMs,
    StartedAt,
    EndedAt,
    HasRemainingWork,
    BeforePlanJson,
    AfterPlanJson
)
OUTPUT INSERTED.Id
VALUES
(
    @Direction,
    @SyncMode,
    @SourceProfileCode,
    @TargetProfileCode,
    @MaKhoaHoc,
    @ConfirmTextMatched,
    @Executed,
    @Status,
    @Message,
    @InsertedKhoaHoc,
    @InsertedBaoCaoI,
    @InsertedNguoiLX,
    @InsertedNguoiLXGPLX,
    @InsertedNguoiLXHoSo,
    @InsertedGiayTo,
    @UpdatedNguoiLX,
    @UpdatedNguoiLXHoSo,
    @UpdatedRows,
    @DeletedRows,
    @DurationMs,
    @StartedAt,
    @EndedAt,
    @HasRemainingWork,
    @BeforePlanJson,
    @AfterPlanJson
);";

    private const string SelectColumns = @"
Id,
CreatedAt,
Direction,
SyncMode,
SourceProfileCode,
TargetProfileCode,
MaKhoaHoc,
ConfirmTextMatched,
Executed,
Status,
Message,
InsertedKhoaHoc,
InsertedBaoCaoI,
InsertedNguoiLX,
InsertedNguoiLXGPLX,
InsertedNguoiLXHoSo,
InsertedGiayTo,
UpdatedNguoiLX,
UpdatedNguoiLXHoSo,
UpdatedRows,
DeletedRows,
DurationMs,
StartedAt,
EndedAt,
HasRemainingWork,
BeforePlanJson,
AfterPlanJson";

    private const string SelectListSql = $@"
SELECT TOP (@Take)
{SelectColumns}
FROM dbo.App_MotoSyncRunHistory
WHERE (@MaKhoaHoc IS NULL OR MaKhoaHoc = @MaKhoaHoc)
  AND (@Direction IS NULL OR Direction = @Direction)
  AND (@SyncMode IS NULL OR SyncMode = @SyncMode)
{ListOrderBySql};";

    private const string SelectDetailSql = $@"
SELECT {SelectColumns}
FROM dbo.App_MotoSyncRunHistory
WHERE Id = @Id;";

    private sealed class RunHistoryRow
    {
        public long Id { get; init; }
        public DateTime CreatedAt { get; init; }
        public string Direction { get; init; } = string.Empty;
        public string SyncMode { get; init; } = string.Empty;
        public string SourceProfileCode { get; init; } = string.Empty;
        public string TargetProfileCode { get; init; } = string.Empty;
        public string? MaKhoaHoc { get; init; }
        public bool ConfirmTextMatched { get; init; }
        public bool Executed { get; init; }
        public string Status { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public long InsertedKhoaHoc { get; init; }
        public long InsertedBaoCaoI { get; init; }
        public long InsertedNguoiLX { get; init; }
        public long InsertedNguoiLXGPLX { get; init; }
        public long InsertedNguoiLXHoSo { get; init; }
        public long InsertedGiayTo { get; init; }
        public long UpdatedNguoiLX { get; init; }
        public long UpdatedNguoiLXHoSo { get; init; }
        public long UpdatedRows { get; init; }
        public long DeletedRows { get; init; }
        public long DurationMs { get; init; }
        public DateTime StartedAt { get; init; }
        public DateTime EndedAt { get; init; }
        public bool HasRemainingWork { get; init; }
        public string? BeforePlanJson { get; init; }
        public string? AfterPlanJson { get; init; }
    }
}

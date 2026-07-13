using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using System.Text.RegularExpressions;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Infrastructure.Sync;

public sealed class MotoCenterTransferRunHistoryRepository : IMotoCenterTransferRunHistoryRepository
{
    internal const int DefaultTake = 50;
    internal const int MaxTake = 200;
    internal const string ListOrderBySql = "ORDER BY StartedAt DESC, Id DESC";

    private static readonly Regex SecretTokenRegex = new(
        @"\b(Password|Pwd|User ID|User Id|UID)\s*=\s*[^;]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IConnectionSettingsProvider _connections;
    private readonly AppSyncOptions _options;

    public MotoCenterTransferRunHistoryRepository(
        IConnectionSettingsProvider connections,
        IOptions<AppSyncOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task<long> CreateAsync(
        MotoCenterTransferRunHistoryCreateDto entry,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var command = new CommandDefinition(
            InsertSql,
            new
            {
                entry.SourceProfileCode,
                entry.TargetProfileCode,
                MaKhoaHocCu = DbTrim(entry.MaKhoaHocCu) ?? string.Empty,
                MaKhoaHocMoi = DbTrim(entry.MaKhoaHocMoi),
                MaCSDTCu = DbTrim(entry.MaCSDTCu) ?? string.Empty,
                MaCSDTMoi = DbTrim(entry.MaCSDTMoi) ?? string.Empty,
                MaSoGTVTMoi = DbTrim(entry.MaSoGTVTMoi),
                entry.ConfirmTextMatched,
                entry.Executed,
                entry.Status,
                Message = Limit(Sanitize(entry.Message), 1000),
                entry.CopiedKhoaHoc,
                entry.CopiedBaoCaoI,
                entry.CopiedNguoiLX,
                entry.CopiedNguoiLXHoSo,
                entry.CopiedNguoiLXHSGiayTo,
                entry.UpdatedNguoiLXHoSo,
                entry.UpdatedNguoiLX,
                entry.UpdatedKhoaHoc,
                entry.UpdatedBaoCaoI,
                entry.UpdatedNguoiLXHSGiayTo,
                entry.TargetKhoaHocMoiCountAfter,
                entry.TargetBaoCaoIMoiCountAfter,
                entry.TargetNguoiLXHoSoMoiCountAfter,
                entry.TargetNguoiLXHSGiayToMoiCountAfter,
                entry.TargetNguoiLXMoiCountAfter,
                entry.DurationMs,
                entry.StartedAt,
                entry.EndedAt,
                PlanJson = Sanitize(entry.PlanJson),
                SummaryJson = Sanitize(entry.SummaryJson),
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<long>(command);
    }

    public async Task<IReadOnlyList<MotoCenterTransferRunHistoryListItemDto>> SearchAsync(
        MotoCenterTransferRunHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        query ??= new MotoCenterTransferRunHistoryQuery();
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<CenterTransferRunHistoryRow>(new CommandDefinition(
            SelectListSql,
            new { Take = NormalizeTake(query.Take) },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        return rows.Select(ToListItem).ToList();
    }

    public async Task<MotoCenterTransferRunHistoryDetailDto?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var row = await connection.QuerySingleOrDefaultAsync<CenterTransferRunHistoryRow>(new CommandDefinition(
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
                "QLHV_APP chua co cau hinh ket noi dung duoc de ghi lich su chuyen MaCSDT Moto.");
        }

        return target.ConnectionString;
    }

    private static MotoCenterTransferRunHistoryListItemDto ToListItem(CenterTransferRunHistoryRow row) => new()
    {
        Id = row.Id,
        SourceProfileCode = row.SourceProfileCode,
        TargetProfileCode = row.TargetProfileCode,
        MaKhoaHocCu = row.MaKhoaHocCu,
        MaKhoaHocMoi = row.MaKhoaHocMoi,
        MaCSDTCu = row.MaCSDTCu,
        MaCSDTMoi = row.MaCSDTMoi,
        MaSoGTVTMoi = row.MaSoGTVTMoi,
        ConfirmTextMatched = row.ConfirmTextMatched,
        Executed = row.Executed,
        Status = row.Status,
        Message = row.Message,
        CopiedTotal = row.CopiedKhoaHoc +
                      row.CopiedBaoCaoI +
                      row.CopiedNguoiLX +
                      row.CopiedNguoiLXHoSo +
                      row.CopiedNguoiLXHSGiayTo,
        UpdatedTotal = row.UpdatedNguoiLXHoSo +
                       row.UpdatedNguoiLX +
                       row.UpdatedKhoaHoc +
                       row.UpdatedBaoCaoI +
                       row.UpdatedNguoiLXHSGiayTo,
        DurationMs = row.DurationMs ?? 0,
        StartedAt = row.StartedAt,
        EndedAt = row.EndedAt,
    };

    private static MotoCenterTransferRunHistoryDetailDto ToDetail(CenterTransferRunHistoryRow row) => new()
    {
        Id = row.Id,
        SourceProfileCode = row.SourceProfileCode,
        TargetProfileCode = row.TargetProfileCode,
        MaKhoaHocCu = row.MaKhoaHocCu,
        MaKhoaHocMoi = row.MaKhoaHocMoi,
        MaCSDTCu = row.MaCSDTCu,
        MaCSDTMoi = row.MaCSDTMoi,
        MaSoGTVTMoi = row.MaSoGTVTMoi,
        ConfirmTextMatched = row.ConfirmTextMatched,
        Executed = row.Executed,
        Status = row.Status,
        Message = row.Message,
        CopiedTotal = row.CopiedKhoaHoc +
                      row.CopiedBaoCaoI +
                      row.CopiedNguoiLX +
                      row.CopiedNguoiLXHoSo +
                      row.CopiedNguoiLXHSGiayTo,
        UpdatedTotal = row.UpdatedNguoiLXHoSo +
                       row.UpdatedNguoiLX +
                       row.UpdatedKhoaHoc +
                       row.UpdatedBaoCaoI +
                       row.UpdatedNguoiLXHSGiayTo,
        DurationMs = row.DurationMs ?? 0,
        StartedAt = row.StartedAt,
        EndedAt = row.EndedAt,
        CopiedKhoaHoc = row.CopiedKhoaHoc,
        CopiedBaoCaoI = row.CopiedBaoCaoI,
        CopiedNguoiLX = row.CopiedNguoiLX,
        CopiedNguoiLXHoSo = row.CopiedNguoiLXHoSo,
        CopiedNguoiLXHSGiayTo = row.CopiedNguoiLXHSGiayTo,
        UpdatedNguoiLXHoSo = row.UpdatedNguoiLXHoSo,
        UpdatedNguoiLX = row.UpdatedNguoiLX,
        UpdatedKhoaHoc = row.UpdatedKhoaHoc,
        UpdatedBaoCaoI = row.UpdatedBaoCaoI,
        UpdatedNguoiLXHSGiayTo = row.UpdatedNguoiLXHSGiayTo,
        TargetKhoaHocMoiCountAfter = row.TargetKhoaHocMoiCountAfter,
        TargetBaoCaoIMoiCountAfter = row.TargetBaoCaoIMoiCountAfter,
        TargetNguoiLXHoSoMoiCountAfter = row.TargetNguoiLXHoSoMoiCountAfter,
        TargetNguoiLXHSGiayToMoiCountAfter = row.TargetNguoiLXHSGiayToMoiCountAfter,
        TargetNguoiLXMoiCountAfter = row.TargetNguoiLXMoiCountAfter,
        PlanJson = row.PlanJson,
        SummaryJson = row.SummaryJson,
    };

    private static string? DbTrim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Limit(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];

    private const string InsertSql = @"
INSERT INTO dbo.App_MotoCenterTransferRunHistory
(
    SourceProfileCode,
    TargetProfileCode,
    MaKhoaHocCu,
    MaKhoaHocMoi,
    MaCSDTCu,
    MaCSDTMoi,
    MaSoGTVTMoi,
    ConfirmTextMatched,
    Executed,
    Status,
    Message,
    CopiedKhoaHoc,
    CopiedBaoCaoI,
    CopiedNguoiLX,
    CopiedNguoiLXHoSo,
    CopiedNguoiLXHSGiayTo,
    UpdatedNguoiLXHoSo,
    UpdatedNguoiLX,
    UpdatedKhoaHoc,
    UpdatedBaoCaoI,
    UpdatedNguoiLXHSGiayTo,
    TargetKhoaHocMoiCountAfter,
    TargetBaoCaoIMoiCountAfter,
    TargetNguoiLXHoSoMoiCountAfter,
    TargetNguoiLXHSGiayToMoiCountAfter,
    TargetNguoiLXMoiCountAfter,
    DurationMs,
    StartedAt,
    EndedAt,
    PlanJson,
    SummaryJson
)
OUTPUT INSERTED.Id
VALUES
(
    @SourceProfileCode,
    @TargetProfileCode,
    @MaKhoaHocCu,
    @MaKhoaHocMoi,
    @MaCSDTCu,
    @MaCSDTMoi,
    @MaSoGTVTMoi,
    @ConfirmTextMatched,
    @Executed,
    @Status,
    @Message,
    @CopiedKhoaHoc,
    @CopiedBaoCaoI,
    @CopiedNguoiLX,
    @CopiedNguoiLXHoSo,
    @CopiedNguoiLXHSGiayTo,
    @UpdatedNguoiLXHoSo,
    @UpdatedNguoiLX,
    @UpdatedKhoaHoc,
    @UpdatedBaoCaoI,
    @UpdatedNguoiLXHSGiayTo,
    @TargetKhoaHocMoiCountAfter,
    @TargetBaoCaoIMoiCountAfter,
    @TargetNguoiLXHoSoMoiCountAfter,
    @TargetNguoiLXHSGiayToMoiCountAfter,
    @TargetNguoiLXMoiCountAfter,
    @DurationMs,
    @StartedAt,
    @EndedAt,
    @PlanJson,
    @SummaryJson
);";

private const string SelectColumns = @"
Id,
SourceProfileCode,
TargetProfileCode,
MaKhoaHocCu,
MaKhoaHocMoi,
MaCSDTCu,
MaCSDTMoi,
MaSoGTVTMoi,
ConfirmTextMatched,
Executed,
Status,
Message,
CopiedKhoaHoc,
CopiedBaoCaoI,
CopiedNguoiLX,
CopiedNguoiLXHoSo,
CopiedNguoiLXHSGiayTo,
UpdatedNguoiLXHoSo,
UpdatedNguoiLX,
UpdatedKhoaHoc,
UpdatedBaoCaoI,
UpdatedNguoiLXHSGiayTo,
TargetKhoaHocMoiCountAfter,
TargetBaoCaoIMoiCountAfter,
TargetNguoiLXHoSoMoiCountAfter,
TargetNguoiLXHSGiayToMoiCountAfter,
TargetNguoiLXMoiCountAfter,
DurationMs,
StartedAt,
EndedAt,
PlanJson,
SummaryJson";

    private const string SelectListSql = $@"
SELECT TOP (@Take)
{SelectColumns}
FROM dbo.App_MotoCenterTransferRunHistory
{ListOrderBySql};";

    private const string SelectDetailSql = $@"
SELECT {SelectColumns}
FROM dbo.App_MotoCenterTransferRunHistory
WHERE Id = @Id;";

    private sealed class CenterTransferRunHistoryRow
    {
        public long Id { get; init; }
        public string SourceProfileCode { get; init; } = string.Empty;
        public string TargetProfileCode { get; init; } = string.Empty;
        public string MaKhoaHocCu { get; init; } = string.Empty;
        public string? MaKhoaHocMoi { get; init; }
        public string MaCSDTCu { get; init; } = string.Empty;
        public string MaCSDTMoi { get; init; } = string.Empty;
        public string? MaSoGTVTMoi { get; init; }
        public bool ConfirmTextMatched { get; init; }
        public bool Executed { get; init; }
        public string Status { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public long CopiedKhoaHoc { get; init; }
        public long CopiedBaoCaoI { get; init; }
        public long CopiedNguoiLX { get; init; }
        public long CopiedNguoiLXHoSo { get; init; }
        public long CopiedNguoiLXHSGiayTo { get; init; }
        public long UpdatedNguoiLXHoSo { get; init; }
        public long UpdatedNguoiLX { get; init; }
        public long UpdatedKhoaHoc { get; init; }
        public long UpdatedBaoCaoI { get; init; }
        public long UpdatedNguoiLXHSGiayTo { get; init; }
        public long? TargetKhoaHocMoiCountAfter { get; init; }
        public long? TargetBaoCaoIMoiCountAfter { get; init; }
        public long? TargetNguoiLXHoSoMoiCountAfter { get; init; }
        public long? TargetNguoiLXHSGiayToMoiCountAfter { get; init; }
        public long? TargetNguoiLXMoiCountAfter { get; init; }
        public long? DurationMs { get; init; }
        public DateTime StartedAt { get; init; }
        public DateTime? EndedAt { get; init; }
        public string? PlanJson { get; init; }
        public string? SummaryJson { get; init; }
    }
}

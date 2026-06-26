using QLHV.Application.Sync.Dtos;
using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class HocVienV2SqlBuilderTests
{
    [Fact]
    public void Page_query_joins_dm_hangdt_for_hang_hoc()
    {
        var query = HocVienV2SqlBuilder.BuildPage(HocVienSourceFilter.Empty, offset: 0, pageSize: 20);

        Assert.Contains("LEFT JOIN dbo.DM_HangDT AS hdt ON hdt.MaHangDT = hs.HangDaoTao", query.Sql, StringComparison.Ordinal);
        Assert.Contains("hs.HangDaoTao", query.Sql, StringComparison.Ordinal);
        Assert.Contains("hdt.TenHangDT", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("hs.HangGPLX                           AS HangGPLX", query.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Hang_hoc_filter_uses_hang_dao_tao_or_ten_hang_dt()
    {
        var query = HocVienV2SqlBuilder.BuildPage(
            new HocVienSourceFilter { HangGPLX = "B2" },
            offset: 0,
            pageSize: 20);

        Assert.Contains("(hs.HangDaoTao = @HangGPLX OR hdt.TenHangDT = @HangGPLX)", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("hs.HangGPLX = @HangGPLX", query.Sql, StringComparison.Ordinal);
        Assert.Contains("HangGPLX", query.Parameters.ParameterNames);
    }

    [Fact]
    public void Page_query_joins_dm_dvhc_by_combined_noi_tt_codes()
    {
        var query = HocVienV2SqlBuilder.BuildPage(HocVienSourceFilter.Empty, offset: 0, pageSize: 20);

        Assert.Contains("LEFT JOIN dbo.DM_DVHC AS dvhc", query.Sql, StringComparison.Ordinal);
        Assert.Contains("dvhc.MaDV", query.Sql, StringComparison.Ordinal);
        Assert.Contains("nlx.NoiTT_MaDVQL", query.Sql, StringComparison.Ordinal);
        Assert.Contains("nlx.NoiTT_MaDVHC", query.Sql, StringComparison.Ordinal);
        Assert.Contains("dvhc.TenDayDu", query.Sql, StringComparison.Ordinal);
        Assert.Contains("nlx.NoiTT", query.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostics_query_is_read_only_and_checks_source_data_quality()
    {
        var sql = HocVienV2SqlBuilder.BuildDiagnostics();

        Assert.Contains("DuplicateMaDkCount", sql, StringComparison.Ordinal);
        Assert.Contains("MissingMaDkCount", sql, StringComparison.Ordinal);
        Assert.Contains("MissingHoTenCount", sql, StringComparison.Ordinal);
        Assert.Contains("GioiTinh", sql, StringComparison.Ordinal);
        Assert.Contains("LEN(LTRIM(RTRIM(SoCMT))) = 9", sql, StringComparison.Ordinal);
        Assert.Contains("LEN(LTRIM(RTRIM(SoCMT))) = 12", sql, StringComparison.Ordinal);
        Assert.Contains("TRY_CONVERT(date, NgaySinhRaw, 112)", sql, StringComparison.Ordinal);
        Assert.Contains("HangDaoTaoUnmatchedDmHangDtCount", sql, StringComparison.Ordinal);
        Assert.Contains("NoiTTUnmatchedDmDvhcCount", sql, StringComparison.Ordinal);
        Assert.Contains("MissingMaKhoaHocCount", sql, StringComparison.Ordinal);
        Assert.Contains("MaKhoaHocUnmatchedKhoaHocCount", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE ", sql, StringComparison.OrdinalIgnoreCase);
    }
}

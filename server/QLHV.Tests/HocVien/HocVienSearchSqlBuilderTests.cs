using QLHV.Application.HocVien.Dtos;
using QLHV.Infrastructure.HocVien;

namespace QLHV.Tests.HocVien;

public sealed class HocVienSearchSqlBuilderTests
{
    [Fact]
    public void Build_page_reads_only_app_hocvien_and_excludes_deleted_rows()
    {
        var query = HocVienSearchSqlBuilder.BuildPage(new HocVienSearchRequest
        {
            Page = 2,
            PageSize = 20,
        });

        Assert.Contains("FROM dbo.App_HocVien", query.Sql, StringComparison.Ordinal);
        Assert.Contains("IsDeleted = 0", query.Sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY HocVienId ASC", query.Sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CSDT_V2", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RowVersion", query.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_count_adds_keyword_search_for_madk_hoten_and_socccd()
    {
        var query = HocVienSearchSqlBuilder.BuildCount(new HocVienSearchRequest
        {
            Keyword = "HV001",
            Page = 1,
            PageSize = 20,
        });

        Assert.Contains("MaDK LIKE @Keyword", query.Sql, StringComparison.Ordinal);
        Assert.Contains("HoTen LIKE @Keyword", query.Sql, StringComparison.Ordinal);
        Assert.Contains("SoCCCD LIKE @Keyword", query.Sql, StringComparison.Ordinal);
        Assert.Contains("Keyword", query.Parameters.ParameterNames);
    }

    [Fact]
    public void Build_page_adds_supported_filters()
    {
        var query = HocVienSearchSqlBuilder.BuildPage(new HocVienSearchRequest
        {
            MaKhoa = "K001",
            HangGplx = "B2",
            GioiTinh = "1",
            Page = 1,
            PageSize = 20,
        });

        Assert.Contains("MaKhoa = @MaKhoa", query.Sql, StringComparison.Ordinal);
        Assert.Contains("HangGPLXHoc = @HangGplx", query.Sql, StringComparison.Ordinal);
        Assert.Contains("GioiTinh = @GioiTinh", query.Sql, StringComparison.Ordinal);
        Assert.Contains("MaKhoa", query.Parameters.ParameterNames);
        Assert.Contains("HangGplx", query.Parameters.ParameterNames);
        Assert.Contains("GioiTinh", query.Parameters.ParameterNames);
    }

    [Fact]
    public void Build_count_escapes_like_wildcards_in_keyword()
    {
        var query = HocVienSearchSqlBuilder.BuildCount(new HocVienSearchRequest
        {
            Keyword = @"HV%_[]\",
            Page = 1,
            PageSize = 20,
        });

        Assert.Equal(@"%HV\%\_\[]\\%", query.Parameters.Get<string>("Keyword"));
    }
}

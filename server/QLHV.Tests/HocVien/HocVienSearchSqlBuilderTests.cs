using QLHV.Application.HocVien.Dtos;
using QLHV.Infrastructure.HocVien;

namespace QLHV.Tests.HocVien;

public sealed class HocVienSearchSqlBuilderTests
{
    [Fact]
    public void No_filter_query_reads_active_app_hocvien_only()
    {
        var query = HocVienSearchSqlBuilder.BuildPage(new HocVienSearchRequest
        {
            Page = 1,
            PageSize = 20,
        });

        Assert.Contains("FROM dbo.App_HocVien", query.Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE IsDeleted = 0", query.Sql, StringComparison.Ordinal);
        Assert.Contains("AnhRelativePath", query.Sql, StringComparison.Ordinal);
        Assert.Contains("LastSyncStatus", query.Sql, StringComparison.Ordinal);
        Assert.Contains("HangGPLXHoc", query.Sql, StringComparison.Ordinal);
        Assert.Contains("HangGPLXDaCo", query.Sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY HocVienId ASC", query.Sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CSDT_V2", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RowVersion", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@Keyword", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@MaKhoa", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@HangGplx", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@GioiTinh", query.Sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Tất cả")]
    [InlineData("Tat ca")]
    public void Gioi_tinh_empty_or_all_does_not_filter(string? value)
    {
        var query = HocVienSearchSqlBuilder.BuildCount(new HocVienSearchRequest
        {
            GioiTinh = value,
            Page = 1,
            PageSize = 20,
        });

        Assert.DoesNotContain("GioiTinh = @GioiTinh", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("GioiTinh", query.Parameters.ParameterNames);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ma_khoa_empty_does_not_filter(string? value)
    {
        var query = HocVienSearchSqlBuilder.BuildCount(new HocVienSearchRequest
        {
            MaKhoa = value,
            Page = 1,
            PageSize = 20,
        });

        Assert.DoesNotContain("MaKhoa = @MaKhoa", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("MaKhoa", query.Parameters.ParameterNames);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Hang_gplx_empty_does_not_filter(string? value)
    {
        var query = HocVienSearchSqlBuilder.BuildCount(new HocVienSearchRequest
        {
            HangGplx = value,
            Page = 1,
            PageSize = 20,
        });

        Assert.DoesNotContain("HangGPLXHoc = @HangGplx", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("HangGplx", query.Parameters.ParameterNames);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Keyword_empty_does_not_filter(string? value)
    {
        var query = HocVienSearchSqlBuilder.BuildCount(new HocVienSearchRequest
        {
            Keyword = value,
            Page = 1,
            PageSize = 20,
        });

        Assert.DoesNotContain("@Keyword", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Keyword", query.Parameters.ParameterNames);
    }

    [Fact]
    public void Keyword_search_uses_madk_hoten_and_socccd()
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
    public void Supported_filters_are_parameterized_when_present()
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
}

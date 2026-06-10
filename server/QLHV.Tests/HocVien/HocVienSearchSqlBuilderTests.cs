using QLHV.Application.HocVien.Dtos;
using QLHV.Application.HocVien;
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
        Assert.Contains("MaHangDT", query.Sql, StringComparison.Ordinal);
        Assert.Contains("HangGPLXHoc", query.Sql, StringComparison.Ordinal);
        Assert.Contains("HangGPLXDaCo", query.Sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY HocVienId ASC", query.Sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CSDT_V2", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RowVersion", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@Keyword", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@MaKhoa", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@HangGplx", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@MaHangDT", query.Sql, StringComparison.Ordinal);
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
    [InlineData("M", "Nam")]
    [InlineData("F", "Nữ")]
    public void Gioi_tinh_source_value_displays_as_vietnamese_label(string sourceValue, string expected)
    {
        Assert.Equal(expected, HocVienGender.ToDisplayValue(sourceValue));
    }

    [Theory]
    [InlineData("Nam", "M")]
    [InlineData("Nữ", "F")]
    [InlineData("Nu", "F")]
    public void Gioi_tinh_filter_maps_display_label_to_source_value(string displayValue, string expectedSourceValue)
    {
        var query = HocVienSearchSqlBuilder.BuildCount(new HocVienSearchRequest
        {
            GioiTinh = displayValue,
            Page = 1,
            PageSize = 20,
        });

        Assert.Contains("GioiTinh = @GioiTinh", query.Sql, StringComparison.Ordinal);
        Assert.Equal(expectedSourceValue, query.Parameters.Get<string>("GioiTinh"));
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
    [InlineData("Tất cả")]
    [InlineData("Tat ca")]
    public void All_filter_values_do_not_filter(string value)
    {
        var query = HocVienSearchSqlBuilder.BuildCount(new HocVienSearchRequest
        {
            MaKhoa = value,
            HangGplx = value,
            MaHangDT = value,
            GioiTinh = value,
            Page = 1,
            PageSize = 20,
        });

        Assert.DoesNotContain("@MaKhoa", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@HangGplx", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@MaHangDT", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@GioiTinh", query.Sql, StringComparison.Ordinal);
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
            MaHangDT = "B2",
            GioiTinh = "1",
            Page = 1,
            PageSize = 20,
        });

        Assert.Contains("MaKhoa = @MaKhoa", query.Sql, StringComparison.Ordinal);
        Assert.Contains("(MaHangDT = @HangGplx OR HangGPLXHoc = @HangGplx)", query.Sql, StringComparison.Ordinal);
        Assert.Contains("MaHangDT = @MaHangDT", query.Sql, StringComparison.Ordinal);
        Assert.Contains("GioiTinh = @GioiTinh", query.Sql, StringComparison.Ordinal);
        Assert.Contains("MaKhoa", query.Parameters.ParameterNames);
        Assert.Contains("HangGplx", query.Parameters.ParameterNames);
        Assert.Contains("MaHangDT", query.Parameters.ParameterNames);
        Assert.Contains("GioiTinh", query.Parameters.ParameterNames);
    }

    [Fact]
    public void Khoa_lookup_ranks_tenkhoa_prefix_then_makhoa_prefix_then_contains()
    {
        var query = HocVienSearchSqlBuilder.BuildKhoaLookup("A", 20);

        Assert.Contains("FROM dbo.App_HocVien", query.Sql, StringComparison.Ordinal);
        Assert.Contains("TenKhoa LIKE @KeywordPrefix", query.Sql, StringComparison.Ordinal);
        Assert.Contains("MaKhoa LIKE @KeywordPrefix", query.Sql, StringComparison.Ordinal);
        Assert.Contains("TenKhoa LIKE @KeywordContains", query.Sql, StringComparison.Ordinal);
        Assert.Contains("MaKhoa LIKE @KeywordContains", query.Sql, StringComparison.Ordinal);
        Assert.Equal("A%", query.Parameters.Get<string>("KeywordPrefix"));
        Assert.Equal("%A%", query.Parameters.Get<string>("KeywordContains"));
    }

    [Fact]
    public void Khoa_lookup_keyword_can_match_makhoa_prefix()
    {
        var query = HocVienSearchSqlBuilder.BuildKhoaLookup("66016K26A", 20);

        Assert.Contains("MaKhoa LIKE @KeywordPrefix", query.Sql, StringComparison.Ordinal);
        Assert.Equal("66016K26A%", query.Parameters.Get<string>("KeywordPrefix"));
    }

    [Fact]
    public void Hang_hoc_lookup_keyword_can_match_mahangdt_prefix()
    {
        var query = HocVienSearchSqlBuilder.BuildHangHocLookup("Am", 20);

        Assert.Contains("MaHangDT LIKE @KeywordPrefix", query.Sql, StringComparison.Ordinal);
        Assert.Contains("HangGPLXHoc LIKE @KeywordContains", query.Sql, StringComparison.Ordinal);
        Assert.Equal("Am%", query.Parameters.Get<string>("KeywordPrefix"));
        Assert.Equal("%Am%", query.Parameters.Get<string>("KeywordContains"));
    }
}

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
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ma_hang_dt_empty_does_not_filter(string? value)
    {
        var query = HocVienSearchSqlBuilder.BuildCount(new HocVienSearchRequest
        {
            MaHangDT = value,
            Page = 1,
            PageSize = 20,
        });

        Assert.DoesNotContain("MaHangDT = @MaHangDT", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("MaHangDT", query.Parameters.ParameterNames);
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
            MaHangDT = "A1m",
            HangGplx = "B2",
            GioiTinh = "1",
            Page = 1,
            PageSize = 20,
        });

        Assert.Contains("MaKhoa = @MaKhoa", query.Sql, StringComparison.Ordinal);
        Assert.Contains("MaHangDT = @MaHangDT", query.Sql, StringComparison.Ordinal);
        Assert.Contains("(MaHangDT = @HangGplx OR HangGPLXHoc = @HangGplx)", query.Sql, StringComparison.Ordinal);
        Assert.Contains("GioiTinh = @GioiTinh", query.Sql, StringComparison.Ordinal);
        Assert.Contains("MaKhoa", query.Parameters.ParameterNames);
        Assert.Contains("MaHangDT", query.Parameters.ParameterNames);
        Assert.Contains("HangGplx", query.Parameters.ParameterNames);
        Assert.Contains("GioiTinh", query.Parameters.ParameterNames);
        Assert.Equal("A1m", query.Parameters.Get<string>("MaHangDT"));
    }

    [Fact]
    public void Export_query_uses_filters_without_paging()
    {
        var query = HocVienSearchSqlBuilder.BuildExport(new HocVienSearchRequest
        {
            Keyword = "HV001",
            MaKhoa = "K001",
            MaHangDT = "A1m",
            HangGplx = "B2",
            GioiTinh = "Nam",
            Page = 30,
            PageSize = 20,
        }, maxRows: 10_000);

        Assert.Contains("SELECT TOP (@MaxRows)", query.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.App_HocVien", query.Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE IsDeleted = 0", query.Sql, StringComparison.Ordinal);
        Assert.Contains("MaDK LIKE @Keyword", query.Sql, StringComparison.Ordinal);
        Assert.Contains("HoTen LIKE @Keyword", query.Sql, StringComparison.Ordinal);
        Assert.Contains("SoCCCD LIKE @Keyword", query.Sql, StringComparison.Ordinal);
        Assert.Contains("MaHangDT", query.Sql, StringComparison.Ordinal);
        Assert.Contains("MaKhoa = @MaKhoa", query.Sql, StringComparison.Ordinal);
        Assert.Contains("MaHangDT = @MaHangDT", query.Sql, StringComparison.Ordinal);
        Assert.Contains("(MaHangDT = @HangGplx OR HangGPLXHoc = @HangGplx)", query.Sql, StringComparison.Ordinal);
        Assert.Contains("GioiTinh = @GioiTinh", query.Sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY HocVienId ASC", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("OFFSET", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FETCH NEXT", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Offset", query.Parameters.ParameterNames);
        Assert.DoesNotContain("PageSize", query.Parameters.ParameterNames);
        Assert.Equal(10_000, query.Parameters.Get<int>("MaxRows"));
        Assert.Equal("A1m", query.Parameters.Get<string>("MaHangDT"));
        Assert.Equal("M", query.Parameters.Get<string>("GioiTinh"));
    }

    [Fact]
    public void Khoa_lookup_searches_ten_khoa_and_ma_khoa_with_expected_ranking()
    {
        var query = HocVienSearchSqlBuilder.BuildKhoaLookup("66016K26A", limit: 20);

        Assert.Contains("WITH DistinctKhoa", query.Sql, StringComparison.Ordinal);
        Assert.Contains("SELECT TOP (@Limit)", query.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.App_HocVien", query.Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE IsDeleted = 0", query.Sql, StringComparison.Ordinal);
        Assert.Contains("TenKhoa", query.Sql, StringComparison.Ordinal);
        Assert.Contains("MaKhoa", query.Sql, StringComparison.Ordinal);
        Assert.Contains("UPPER(TenKhoa) LIKE UPPER(@LookupPrefix)", query.Sql, StringComparison.Ordinal);
        Assert.Contains("UPPER(MaKhoa) LIKE UPPER(@LookupPrefix)", query.Sql, StringComparison.Ordinal);
        Assert.Contains("UPPER(TenKhoa) LIKE UPPER(@LookupContains)", query.Sql, StringComparison.Ordinal);
        Assert.Contains("UPPER(MaKhoa) LIKE UPPER(@LookupContains)", query.Sql, StringComparison.Ordinal);
        Assert.Contains("CONCAT(TenKhoa, N' - ', MaKhoa)", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("OFFSET", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(20, query.Parameters.Get<int>("Limit"));
        Assert.Equal("66016K26A%", query.Parameters.Get<string>("LookupPrefix"));
        Assert.Equal("%66016K26A%", query.Parameters.Get<string>("LookupContains"));
    }

    [Fact]
    public void Hang_hoc_lookup_searches_ma_hang_dt_and_ten_hang_with_expected_ranking()
    {
        var query = HocVienSearchSqlBuilder.BuildHangHocLookup("AM", limit: 20);

        Assert.Contains("WITH DistinctHangHoc", query.Sql, StringComparison.Ordinal);
        Assert.Contains("SELECT TOP (@Limit)", query.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.App_HocVien", query.Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE IsDeleted = 0", query.Sql, StringComparison.Ordinal);
        Assert.Contains("MaHangDT", query.Sql, StringComparison.Ordinal);
        Assert.Contains("HangGPLXHoc", query.Sql, StringComparison.Ordinal);
        Assert.Contains("HangGplxHoc AS TenHangDT", query.Sql, StringComparison.Ordinal);
        Assert.Contains("UPPER(MaHangDT) LIKE UPPER(@LookupPrefix)", query.Sql, StringComparison.Ordinal);
        Assert.Contains("UPPER(HangGplxHoc) LIKE UPPER(@LookupContains)", query.Sql, StringComparison.Ordinal);
        Assert.Contains("CONCAT(MaHangDT, N' - ', HangGplxHoc)", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("OFFSET", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(20, query.Parameters.Get<int>("Limit"));
        Assert.Equal("AM%", query.Parameters.Get<string>("LookupPrefix"));
        Assert.Equal("%AM%", query.Parameters.Get<string>("LookupContains"));
    }
}

using QLHV.Application.Sync.Dtos;
using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class QlhvImportSqlBuilderTests
{
    [Fact]
    public void Source_read_uses_parameterized_center_and_course_filters()
    {
        var request = new QlhvImportRequest
        {
            SourceProfileCode = "CSDT_OTO",
            MaCSDT = "66029",
            MaKhoaHoc = "66029K01",
        };

        var query = QlhvImportSqlBuilder.BuildSourceRead(request, hasKhoaHocMaCsdt: true);

        Assert.Contains("LTRIM(RTRIM(nlx.MaDK)) LIKE @MaDkPrefix", query.Sql, StringComparison.Ordinal);
        Assert.Contains("LTRIM(RTRIM(kh.MaCSDT)) = @MaCSDT", query.Sql, StringComparison.Ordinal);
        Assert.Contains("LTRIM(RTRIM(hs.MaKhoaHoc)) = @MaKhoaHoc", query.Sql, StringComparison.Ordinal);
        Assert.Contains("LTRIM(RTRIM(kh.MaKH)) = @MaKhoaHoc", query.Sql, StringComparison.Ordinal);
        Assert.Equal("66029%", query.Parameters.Get<string>("MaDkPrefix"));
        Assert.Equal("66029", query.Parameters.Get<string>("MaCSDT"));
        Assert.Equal("66029K01", query.Parameters.Get<string>("MaKhoaHoc"));
        Assert.DoesNotContain("66029K01", query.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.GiaoVien AS gv", query.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.KhoaHoc_GiaoVien AS relation", query.Sql, StringComparison.Ordinal);
        Assert.Contains("relation.IsKhoaHocGiaoVien", query.Sql, StringComparison.Ordinal);
        Assert.Contains("gv.NoiCT_MaDVHC", query.Sql, StringComparison.Ordinal);
        Assert.Contains("gv.MaFileTiepNhanXML", query.Sql, StringComparison.Ordinal);
        AssertReadOnly(query.Sql);
    }

    [Fact]
    public void Source_read_falls_back_to_code_prefix_when_khoa_hoc_lacks_ma_csdt()
    {
        var request = new QlhvImportRequest
        {
            SourceProfileCode = "CSDT_MOTO",
            MaCSDT = "66030",
        };

        var query = QlhvImportSqlBuilder.BuildSourceRead(request, hasKhoaHocMaCsdt: false);

        Assert.Contains("LTRIM(RTRIM(nlx.MaDK)) LIKE @MaDkPrefix", query.Sql, StringComparison.Ordinal);
        Assert.Contains("LTRIM(RTRIM(kh.MaKH)) LIKE @MaDkPrefix", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "LTRIM(RTRIM(kh.MaCSDT)) = @MaCSDT",
            query.Sql,
            StringComparison.Ordinal);
        Assert.Equal("66030%", query.Parameters.Get<string>("MaDkPrefix"));
        AssertReadOnly(query.Sql);
    }

    [Fact]
    public void Target_khoa_hoc_count_uses_parameterized_center_and_course_scope()
    {
        var request = new QlhvImportRequest
        {
            SourceProfileCode = "CSDT_OTO",
            MaCSDT = "66029",
        };

        var query = QlhvImportSqlBuilder.BuildTargetKhoaHocCount(
            request,
            appKhoaHocExists: true,
            appKhoaHocHasMaKhoa: true,
            appKhoaHocHasIsDeleted: true);

        Assert.Contains("FROM dbo.App_KhoaHoc", query.Sql, StringComparison.Ordinal);
        Assert.Contains("MaKhoa)) LIKE @MaDkPrefix", query.Sql, StringComparison.Ordinal);
        Assert.Equal("66029%", query.Parameters.Get<string>("MaDkPrefix"));
        AssertReadOnly(query.Sql);
    }

    private static void AssertReadOnly(string sql)
    {
        Assert.DoesNotContain("INSERT ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE ", sql, StringComparison.OrdinalIgnoreCase);
    }
}

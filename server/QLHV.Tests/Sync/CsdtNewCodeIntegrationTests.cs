using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Tests.Sync;

public sealed class CsdtNewCodeIntegrationTests
{
    [Fact]
    public void Import_mapper_preserves_current_course_and_student_codes()
    {
        var result = QlhvImportHocVienMapper.MapAndValidate(
            Source("66029-20260722-000001", "66029K260001"),
            new HocVienSourceIdentityContext("CSDT_OTO", "V2"));

        Assert.False(result.ShouldSkip);
        Assert.False(result.HasBlockers);
        Assert.NotNull(result.Model);
        Assert.Equal("66029-20260722-000001", result.Model!.MaDK);
        Assert.Equal("66029-20260722-000001", result.Model.SourceMaDK);
        Assert.Equal("66029K260001", result.Model.MaKhoa);
    }

    [Fact]
    public void Import_mapper_preserves_legacy_codes_without_migration()
    {
        var result = QlhvImportHocVienMapper.MapAndValidate(
            Source("660290001234", "KHOA-A1-2024"),
            new HocVienSourceIdentityContext("CSDT_OTO", "V2"));

        Assert.False(result.ShouldSkip);
        Assert.False(result.HasBlockers);
        Assert.NotNull(result.Model);
        Assert.Equal("660290001234", result.Model!.MaDK);
        Assert.Equal("KHOA-A1-2024", result.Model.MaKhoa);
    }

    private static V2HocVienSourceRow Source(string maDk, string maKhoa)
        => new()
        {
            MaDK = maDk,
            MaKhoaHoc = maKhoa,
            TenKH = "Khoa tuong thich ma",
            HangDaoTao = "B2",
            HoVaTen = "Hoc vien fixture",
            NgaySinh = new DateTime(1990, 1, 2),
            SoCMT = "001234567890",
            GioiTinh = "M",
        };
}

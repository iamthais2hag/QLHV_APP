using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Tests.Sync;

public sealed class HocVienMappingRulesTests
{
    [Fact]
    public void SoCCCD_is_preserved_and_trimmed_only()
    {
        var result = HocVienSyncMapper.MapAndValidate(Source(soCmt: "  001234567890  "));

        Assert.NotNull(result.Model);
        Assert.Equal("001234567890", result.Model!.SoCCCD);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Nine_digit_cmnd_is_not_converted_and_creates_warning()
    {
        var result = HocVienSyncMapper.MapAndValidate(Source(soCmt: " 123456789 "));

        Assert.NotNull(result.Model);
        Assert.Equal("123456789", result.Model!.SoCCCD);
        Assert.Contains(result.Warnings, w => w.Code == "CCCD_LENGTH" && w.Field == "SoCCCD");
    }

    [Fact]
    public void Non_12_digit_identity_value_creates_warning()
    {
        var result = HocVienSyncMapper.MapAndValidate(Source(soCmt: "ABC123"));

        Assert.NotNull(result.Model);
        Assert.Equal("ABC123", result.Model!.SoCCCD);
        Assert.Contains(result.Warnings, w => w.Code == "CCCD_LENGTH");
    }

    [Fact]
    public void GioiTinh_raw_value_is_preserved()
    {
        var result = HocVienSyncMapper.MapAndValidate(Source(gioiTinh: " 1 "));

        Assert.NotNull(result.Model);
        Assert.Equal("1", result.Model!.GioiTinh);
    }

    private static V2HocVienSourceRow Source(string soCmt = "001234567890", string gioiTinh = "M") => new()
    {
        MaDK = "DK001",
        HoVaTen = "Nguyen Van A",
        NgaySinh = new DateTime(1990, 1, 2),
        SoCMT = soCmt,
        GioiTinh = gioiTinh,
        MaKhoaHoc = "K001",
        TenKH = "Khoa 1",
        HangGPLX = "B2",
        DiaChiThuongTru = "Dia chi",
        SoGPLXDaCo = "GPLX1",
        HangGPLXDaCo = "A1",
        NguoiNhanHoSo = "Nhan vien",
    };
}

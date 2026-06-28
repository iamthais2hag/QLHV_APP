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

    [Fact]
    public void HangGPLXHoc_uses_ten_hang_dt_not_hang_gplx()
    {
        var result = HocVienSyncMapper.MapAndValidate(Source(tenHangDT: " Hạng B2 ", hangGplx: "A1"));

        Assert.NotNull(result.Model);
        Assert.Equal("B2", result.Model!.MaHangDT);
        Assert.Equal("Hạng B2", result.Model!.HangGPLXHoc);
    }

    [Fact]
    public void DiaChiThuongTru_uses_ten_day_du_when_available()
    {
        var result = HocVienSyncMapper.MapAndValidate(Source(noiTT: "Dia chi nhap tay", noiTTTenDayDu: " Phuong X, Quan Y "));

        Assert.NotNull(result.Model);
        Assert.Equal("Phuong X, Quan Y", result.Model!.DiaChiThuongTru);
    }

    [Fact]
    public void DiaChiThuongTru_falls_back_to_noi_tt_when_ten_day_du_is_null()
    {
        var result = HocVienSyncMapper.MapAndValidate(Source(noiTT: " Dia chi nhap tay ", noiTTTenDayDu: null));

        Assert.NotNull(result.Model);
        Assert.Equal("Dia chi nhap tay", result.Model!.DiaChiThuongTru);
    }

    [Fact]
    public void V2RowHash_reflects_mapped_hang_hoc_and_dia_chi()
    {
        var first = HocVienSyncMapper.MapAndValidate(Source(tenHangDT: "Hang A1", noiTTTenDayDu: "Dia chi 1"));
        var second = HocVienSyncMapper.MapAndValidate(Source(tenHangDT: "Hang B2", noiTTTenDayDu: "Dia chi 1"));
        var third = HocVienSyncMapper.MapAndValidate(Source(tenHangDT: "Hang A1", noiTTTenDayDu: "Dia chi 2"));

        Assert.NotNull(first.Model);
        Assert.NotNull(second.Model);
        Assert.NotNull(third.Model);
        Assert.NotEqual(first.Model!.V2RowHash, second.Model!.V2RowHash);
        Assert.NotEqual(first.Model.V2RowHash, third.Model!.V2RowHash);
    }

    [Fact]
    public void Default_mapping_sets_data_v2_source_identity()
    {
        var result = HocVienSyncMapper.MapAndValidate(Source());

        Assert.NotNull(result.Model);
        Assert.Equal("DATA_V2", result.Model!.SourceProfileCode);
        Assert.Equal("DK001", result.Model.SourceMaDK);
        Assert.Equal("V2", result.Model.SourceSystem);
        Assert.Null(result.Model.SourceVersion);
    }

    [Fact]
    public void Mapping_can_set_data_v1_source_identity_from_context()
    {
        var result = HocVienSyncMapper.MapAndValidate(Source(), HocVienSourceIdentityContext.DataV1);

        Assert.NotNull(result.Model);
        Assert.Equal("DATA_V1", result.Model!.SourceProfileCode);
        Assert.Equal("DK001", result.Model.SourceMaDK);
        Assert.Equal("V1", result.Model.SourceSystem);
        Assert.Null(result.Model.SourceVersion);
    }

    private static V2HocVienSourceRow Source(
        string soCmt = "001234567890",
        string gioiTinh = "M",
        string tenHangDT = "B2",
        string hangGplx = "B2",
        string noiTT = "Dia chi",
        string? noiTTTenDayDu = "Dia chi day du") => new()
    {
        MaDK = "DK001",
        HoVaTen = "Nguyen Van A",
        NgaySinh = new DateTime(1990, 1, 2),
        SoCMT = soCmt,
        GioiTinh = gioiTinh,
        MaKhoaHoc = "K001",
        TenKH = "Khoa 1",
        HangDaoTao = "B2",
        TenHangDT = tenHangDT,
        HangGPLX = hangGplx,
        NoiTT = noiTT,
        NoiTTTenDayDu = noiTTTenDayDu,
        SoGPLXDaCo = "GPLX1",
        HangGPLXDaCo = "A1",
        NguoiNhanHoSo = "Nhan vien",
    };
}

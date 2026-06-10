using QLHV.Application.Sync.Mapping;

namespace QLHV.Tests.Sync;

public sealed class V2RowHashCalculatorTests
{
    [Fact]
    public void Same_input_gives_same_hash()
    {
        var first = V2RowHashCalculator.Compute(Model());
        var second = V2RowHashCalculator.Compute(Model());

        Assert.Equal(first, second);
    }

    [Fact]
    public void Changed_stable_field_changes_hash()
    {
        var first = V2RowHashCalculator.Compute(Model(hoTen: "Nguyen Van A"));
        var second = V2RowHashCalculator.Compute(Model(hoTen: "Nguyen Van B"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Changed_ma_hang_dt_changes_hash()
    {
        var first = V2RowHashCalculator.Compute(Model(maHangDT: "A1m"));
        var second = V2RowHashCalculator.Compute(Model(maHangDT: "B2"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Existing_hash_value_is_not_included_in_hash_input()
    {
        var first = V2RowHashCalculator.Compute(Model(existingHash: "old-sync-metadata"));
        var second = V2RowHashCalculator.Compute(Model(existingHash: "new-sync-metadata"));

        Assert.Equal(first, second);
    }

    private static HocVienTargetWriteModel Model(
        string hoTen = "Nguyen Van A",
        string maHangDT = "B2",
        string existingHash = "") => new()
    {
        MaDK = "DK001",
        MaKhoa = "K001",
        TenKhoa = "Khoa 1",
        MaHangDT = maHangDT,
        HangGPLXHoc = "B2",
        HoTen = hoTen,
        NgaySinh = new DateTime(1990, 1, 2),
        GioiTinh = "1",
        SoCCCD = "001234567890",
        DiaChiThuongTru = "Dia chi",
        SoGPLXDaCo = "GPLX1",
        HangGPLXDaCo = "A1",
        NguoiNhanHoSo = "Nhan vien",
        SourceOfTruth = HocVienDataRules.SourceOfTruthV2,
        V2RowHash = existingHash,
    };
}

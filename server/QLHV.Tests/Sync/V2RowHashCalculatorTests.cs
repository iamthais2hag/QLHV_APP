using QLHV.Application.Sync.Mapping;
using Xunit;

namespace QLHV.Tests.Sync;

public sealed class V2RowHashCalculatorTests
{
    [Fact]
    public void Same_input_gives_same_hash()
    {
        var model = SampleModel();

        var first = V2RowHashCalculator.Compute(model);
        var second = V2RowHashCalculator.Compute(model);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void Changed_stable_field_changes_hash()
    {
        var first = V2RowHashCalculator.Compute(SampleModel());
        var second = V2RowHashCalculator.Compute(SampleModel(hoTen: "Nguyen Van B"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Volatile_sync_fields_are_not_included_in_hash()
    {
        Assert.Null(typeof(HocVienTargetWriteModel).GetProperty("LastSyncFromV2At"));

        var first = V2RowHashCalculator.Compute(SampleModel(v2RowHash: "old"));
        var second = V2RowHashCalculator.Compute(SampleModel(v2RowHash: "new"));

        Assert.Equal(first, second);
    }

    private static HocVienTargetWriteModel SampleModel(
        string hoTen = "Nguyen Van A",
        string v2RowHash = "") => new()
    {
        MaDK = "DK001",
        MaKhoa = "KH001",
        TenKhoa = "Khoa A1",
        HangGPLXHoc = "A1",
        HoTen = hoTen,
        NgaySinh = new DateTime(1990, 1, 2),
        GioiTinh = "M",
        SoCCCD = "012345678901",
        DiaChiThuongTru = "Dia chi",
        SoGPLXDaCo = "GPLX001",
        HangGPLXDaCo = "A1",
        NguoiNhanHoSo = "Can bo tiep nhan",
        SourceOfTruth = HocVienDataRules.SourceOfTruthV2,
        V2RowHash = v2RowHash,
    };
}

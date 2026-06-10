using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using Xunit;

namespace QLHV.Tests.Sync;

public sealed class HocVienMappingRulesTests
{
    [Fact]
    public void SoCCCD_is_trimmed_and_preserved_only()
    {
        var result = HocVienSyncMapper.MapAndValidate(new V2HocVienSourceRow
        {
            MaDK = "DK001",
            SoCMT = " 012345678901 ",
        });

        Assert.False(result.ShouldSkip);
        Assert.NotNull(result.Model);
        Assert.Equal("012345678901", result.Model.SoCCCD);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Nine_digit_CMND_is_not_converted_to_twelve_digit_CCCD()
    {
        var result = HocVienSyncMapper.MapAndValidate(new V2HocVienSourceRow
        {
            MaDK = "DK001",
            SoCMT = " 123456789 ",
        });

        Assert.NotNull(result.Model);
        Assert.Equal("123456789", result.Model.SoCCCD);
        Assert.NotEqual(HocVienDataRules.CccdExpectedLength, result.Model.SoCCCD!.Length);
    }

    [Fact]
    public void Non_twelve_digit_identity_value_creates_warning()
    {
        var result = HocVienSyncMapper.MapAndValidate(new V2HocVienSourceRow
        {
            MaDK = "DK001",
            SoCMT = "ABC123",
        });

        var warning = Assert.Single(result.Warnings);
        Assert.Equal("SoCCCD", warning.Field);
        Assert.Equal("CCCD_LENGTH", warning.Code);
    }

    [Fact]
    public void GioiTinh_raw_value_is_trimmed_and_preserved()
    {
        var result = HocVienSyncMapper.MapAndValidate(new V2HocVienSourceRow
        {
            MaDK = "DK001",
            GioiTinh = " F ",
        });

        Assert.NotNull(result.Model);
        Assert.Equal("F", result.Model.GioiTinh);
    }
}

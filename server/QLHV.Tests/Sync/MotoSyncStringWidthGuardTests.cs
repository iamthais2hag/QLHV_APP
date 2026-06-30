using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class MotoSyncStringWidthGuardTests
{
    [Fact]
    public void Wider_schema_with_fitting_planned_values_returns_warning_not_blocker()
    {
        var result = MotoSyncStringWidthGuard.Evaluate(
            "NguoiLX_HoSo",
            "SoGiayCNTN",
            "nvarchar",
            60,
            "nvarchar",
            40,
            actualMaxLength: 0);

        Assert.False(result.IsBlocker);
        Assert.True(result.IsWarning);
        Assert.Contains("schema source rong hon target", result.Message);
        Assert.Contains("du lieu planned insert dai nhat 0", result.Message);
    }

    [Fact]
    public void Wider_schema_with_too_long_planned_value_returns_blocker()
    {
        var result = MotoSyncStringWidthGuard.Evaluate(
            "NguoiLX_HoSo",
            "SoGiayCNTN",
            "nvarchar",
            60,
            "nvarchar",
            40,
            actualMaxLength: 21);

        Assert.True(result.IsBlocker);
        Assert.False(result.IsWarning);
        Assert.Contains("vuot gioi han target 20", result.Message);
    }

    [Fact]
    public void Wider_schema_with_fitting_planned_update_values_returns_warning_not_blocker()
    {
        var result = MotoSyncStringWidthGuard.Evaluate(
            "NguoiLX_HoSo",
            "SoGiayCNTN",
            "nvarchar",
            60,
            "nvarchar",
            40,
            actualMaxLength: 18,
            operationName: "planned update");

        Assert.False(result.IsBlocker);
        Assert.True(result.IsWarning);
        Assert.Contains("du lieu planned update dai nhat 18", result.Message);
    }

    [Fact]
    public void Wider_schema_with_too_long_planned_update_value_returns_blocker()
    {
        var result = MotoSyncStringWidthGuard.Evaluate(
            "NguoiLX_HoSo",
            "SoGiayCNTN",
            "nvarchar",
            60,
            "nvarchar",
            40,
            actualMaxLength: 21,
            operationName: "planned update");

        Assert.True(result.IsBlocker);
        Assert.False(result.IsWarning);
        Assert.Contains("du lieu planned update dai nhat 21", result.Message);
        Assert.Contains("vuot gioi han target 20", result.Message);
    }

    [Fact]
    public void Converts_nvarchar_max_length_bytes_to_character_limit()
    {
        Assert.Equal(30, MotoSyncStringWidthGuard.ToCharacterLimit("nvarchar", 60));
        Assert.Equal(20, MotoSyncStringWidthGuard.ToCharacterLimit("nvarchar", 40));
        Assert.Equal(25, MotoSyncStringWidthGuard.ToCharacterLimit("varchar", 25));
        Assert.Null(MotoSyncStringWidthGuard.ToCharacterLimit("nvarchar", -1));
    }
}

using System.Data;
using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class MotoSyncUpdatePlannerTests
{
    [Fact]
    public void Plan_detects_zero_update_when_overlap_rows_are_equal()
    {
        var source = CreateRows(("DK001", "A", "Nam"));
        var target = CreateRows(("DK001", "A", "Nam"));

        var result = MotoSyncUpdatePlanner.Build(
            "NguoiLX",
            source,
            target,
            new[] { "HoVaTen", "GioiTinh" });

        Assert.Equal(0, result.UpdatedRowCount);
        Assert.Empty(result.UpdatedMaDks);
        Assert.Empty(result.Samples);
    }

    [Fact]
    public void Plan_detects_update_when_safe_field_differs_without_returning_raw_values()
    {
        var source = CreateRows(("DK001", "A", "M"));
        var target = CreateRows(("DK001", "A", "F"));

        var result = MotoSyncUpdatePlanner.Build(
            "NguoiLX",
            source,
            target,
            new[] { "HoVaTen", "GioiTinh" });

        Assert.Equal(1, result.UpdatedRowCount);
        Assert.Equal("DK001", result.UpdatedMaDks.Single());
        var sample = Assert.Single(result.Samples);
        Assert.Equal("DK001", sample.MaDK);
        Assert.Equal("NguoiLX", sample.TableName);
        Assert.Equal(new[] { "GioiTinh" }, sample.ChangedColumnNames);
    }

    private static DataTable CreateRows(params (string MaDK, string HoVaTen, string GioiTinh)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("MaDK", typeof(string));
        table.Columns.Add("HoVaTen", typeof(string));
        table.Columns.Add("GioiTinh", typeof(string));
        foreach (var row in rows)
        {
            table.Rows.Add(row.MaDK, row.HoVaTen, row.GioiTinh);
        }

        return table;
    }
}

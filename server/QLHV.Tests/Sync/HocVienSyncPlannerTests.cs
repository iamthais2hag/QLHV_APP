using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Tests.Sync;

public sealed class HocVienSyncPlannerTests
{
    [Fact]
    public void BuildPlan_uses_source_identity_not_madk_only()
    {
        var sourceRows = new[] { Source("DK001") };
        var existingTargetKeys = new HashSet<string>
        {
            HocVienSourceIdentityKey.Create("DATA_V1", "DK001"),
        };

        var plan = HocVienSyncPlanner.BuildPlan(
            sourceRows,
            existingTargetKeys,
            HocVienSourceIdentityContext.DataV2);

        Assert.Equal(1, plan.PlannedInsert);
        Assert.Equal(0, plan.PlannedUpdate);
        Assert.Contains(plan.Items, item =>
            item.MaDK == "DK001" &&
            item.Action == PlannedSyncAction.Insert);
    }

    [Fact]
    public void BuildPlan_updates_when_same_source_identity_exists()
    {
        var sourceRows = new[] { Source("DK001") };
        var existingTargetKeys = new HashSet<string>
        {
            HocVienSourceIdentityKey.Create("DATA_V2", "DK001"),
        };

        var plan = HocVienSyncPlanner.BuildPlan(
            sourceRows,
            existingTargetKeys,
            HocVienSourceIdentityContext.DataV2);

        Assert.Equal(0, plan.PlannedInsert);
        Assert.Equal(1, plan.PlannedUpdate);
        Assert.Contains(plan.Items, item =>
            item.MaDK == "DK001" &&
            item.Action == PlannedSyncAction.Update);
    }

    private static V2HocVienSourceRow Source(string maDk) => new()
    {
        MaDK = maDk,
        HoVaTen = "Nguyen Van A",
        NgaySinh = new DateTime(1990, 1, 2),
        SoCMT = "001234567890",
        GioiTinh = "M",
        MaKhoaHoc = "K001",
        TenKH = "Khoa 1",
        HangDaoTao = "B2",
        TenHangDT = "Hang B2",
        NoiTT = "Dia chi",
        NoiTTTenDayDu = "Dia chi day du",
        SoGPLXDaCo = "GPLX1",
        HangGPLXDaCo = "A1",
        NguoiNhanHoSo = "Nhan vien",
    };
}


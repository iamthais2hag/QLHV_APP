using QLHV.Application.Sync.VehicleRealtime;

namespace QLHV.Tests.Sync.Rt03;

public sealed class Rt03VehicleFullConvergenceTests
{
    [Fact]
    public void Ct_off_snapshot_convergence_inserts_source_vehicle()
    {
        var plan = Build([Source("51A00001")], []);

        var row = Assert.Single(plan.Rows);
        Assert.Equal(VehicleRealtimeActions.InsertSourceRow, row.Action);
        Assert.Equal(84, row.SourceCtVersion);
    }

    [Fact]
    public void Existing_external_full_sync_vehicle_is_no_change()
    {
        var source = Source("51A00001");
        var mapped = VehicleSourceMapper.Map(source, VehicleRealtimeProfiles.Oto).Model!;
        var target = Target(
            1,
            source.BienSoXe,
            mapped.SourceRowHash,
            assigned: false);

        var plan = Build([source], [target]);

        Assert.Equal(VehicleRealtimeActions.NoChange, Assert.Single(plan.Rows).Action);
    }

    [Fact]
    public void Stale_vehicle_updates_source_owned_fields()
    {
        var plan = Build(
            [Source("51A00001")],
            [Target(1, "51A00001", "stale", assigned: false)]);

        Assert.Equal(
            VehicleRealtimeActions.UpdateSourceOwnedFields,
            Assert.Single(plan.Rows).Action);
    }

    [Fact]
    public void Missing_assigned_vehicle_requires_manual_review()
    {
        var plan = Build(
            [],
            [Target(1, "51A00001", "old", assigned: true)]);

        var row = Assert.Single(plan.Rows);
        Assert.Equal(VehicleRealtimeActions.ManualReview, row.Action);
        Assert.Equal(
            VehicleRealtimeReviewCodes.SourceMissingWithAssignment,
            row.ReviewCode);
    }

    [Fact]
    public void Inactive_assigned_vehicle_requires_manual_review()
    {
        var plan = Build(
            [Source("51A00001", active: false)],
            [Target(1, "51A00001", "old", assigned: true)]);

        Assert.Equal(
            VehicleRealtimeActions.ManualReview,
            Assert.Single(plan.Rows).Action);
    }

    [Fact]
    public void Duplicate_exact_target_blocks_full_convergence()
    {
        var error = Assert.Throws<VehicleRealtimeSafetyException>(() => Build(
            [Source("51A00001")],
            [
                Target(1, "51A00001", "one", assigned: false),
                Target(2, "51A00001", "two", assigned: false),
            ]));

        Assert.Equal(VehicleRealtimeErrorCodes.UnsafePlan, error.Code);
    }

    [Fact]
    public void Mapped_plate_collisions_are_manual_review_not_identity_merge()
    {
        var plan = Build(
            [Source("51A00001"), Source("51A-00001")],
            []);

        Assert.Equal(2, plan.Rows.Count);
        Assert.All(plan.Rows, row =>
        {
            Assert.Equal(VehicleRealtimeActions.ManualReview, row.Action);
            Assert.Equal(VehicleRealtimeReviewCodes.PlateCollision, row.ReviewCode);
        });
    }

    [Fact]
    public void Zero_anchor_is_supported_for_moto_snapshot()
    {
        var plan = Build(
            [Source("59A00001", VehicleRealtimeProfiles.Moto)],
            [],
            VehicleRealtimeRouteCatalog.Moto,
            anchor: 0);

        Assert.Equal(0, Assert.Single(plan.Rows).SourceCtVersion);
        Assert.Equal(0, plan.AnchorVersion);
    }

    private static VehicleFullConvergencePlan Build(
        IReadOnlyCollection<VehicleSourceRow> source,
        IReadOnlyList<VehicleTargetSnapshot> target,
        VehicleRealtimeRoute? route = null,
        long anchor = 84)
    {
        route ??= VehicleRealtimeRouteCatalog.Oto;
        return VehicleFullConvergencePlanner.Build(
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            route,
            route.ExpectedProductionDatabaseGuid,
            anchor,
            new string('a', 64),
            source,
            target);
    }

    private static VehicleSourceRow Source(
        string plate,
        string profile = VehicleRealtimeProfiles.Oto,
        bool active = true)
    {
        var route = VehicleRealtimeRouteCatalog.GetRequired(profile);
        return new VehicleSourceRow
        {
            BienSoXe = plate,
            MaCSDT = route.ExpectedMaCsdt,
            MaSoGTVT = "66000",
            SoDK = "DK-" + plate,
            SoHuu = true,
            TrangThai = active,
            NgayTao = new DateTime(2026, 1, 1),
            NgaySua = new DateTime(2026, 1, 2),
        };
    }

    private static VehicleTargetSnapshot Target(
        long id,
        string plate,
        string hash,
        bool assigned)
        => new(
            id,
            VehicleRealtimeProfiles.Oto,
            plate,
            plate,
            VehicleSourceNormalizer.NormalizePlateCollisionKey(plate),
            hash,
            true,
            VehicleRealtimeLifecycles.Active,
            null,
            "DK-" + plate,
            VehicleSourceNormalizer.NormalizeSecondaryCollisionKey("DK-" + plate),
            null,
            null,
            null,
            null,
            IsDeleted: false,
            HasActiveAssignments: assigned,
            RowVersion: BitConverter.GetBytes(id));
}

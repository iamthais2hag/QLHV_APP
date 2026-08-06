using System.Reflection;
using System.Text.RegularExpressions;
using QLHV.Application.Sync.VehicleRealtime;
using QLHV.Infrastructure.Sync.VehicleRealtime;

namespace QLHV.Tests.Sync;

public sealed class VehicleRealtimeMappingTests
{
    [Fact]
    public void Source_contract_is_exact_profile_safe_and_does_not_invent_ma_xe()
    {
        Assert.Equal("dbo.XeTap", VehicleRealtimeSourceObjects.QualifiedTable);
        Assert.Equal("BienSoXe", VehicleRealtimeSourceObjects.PrimaryKey);
        Assert.Equal(
            ["CSDT_OTO", "CSDT_MOTO"],
            VehicleRealtimeRouteCatalog.Ordered
                .Select(route => route.SourceProfileCode)
                .ToArray());
        Assert.DoesNotContain(
            typeof(VehicleSourceRow).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.Name is "MaXe" or "SourceMaXe");
        Assert.DoesNotContain(
            typeof(VehicleSourceWriteModel).GetProperties(
                BindingFlags.Public | BindingFlags.Instance),
            property => property.Name is "MaXe" or "SourceMaXe");
    }

    [Fact]
    public void Same_source_plate_is_distinct_identity_between_oto_and_moto()
    {
        var oto = VehicleSourceMapper.Map(Source(), VehicleRealtimeProfiles.Oto);
        var moto = VehicleSourceMapper.Map(
            Source(maCsdt: "66030"),
            VehicleRealtimeProfiles.Moto);

        Assert.True(oto.IsSafe);
        Assert.True(moto.IsSafe);
        Assert.Equal("51A-123.45", oto.Model!.Identity.SourceBienSoXe);
        Assert.Equal("51A-123.45", moto.Model!.Identity.SourceBienSoXe);
        Assert.NotEqual(
            oto.Model.Identity.SourceProfileCode,
            moto.Model.Identity.SourceProfileCode);
        Assert.NotEqual(oto.Model.SourceRowHash, moto.Model.SourceRowHash);
    }

    [Fact]
    public void Mapper_preserves_exact_plate_uses_collision_key_and_hashes_image_path_only()
    {
        var result = VehicleSourceMapper.Map(
            Source(imagePath: @"D:\SourceVehiclePhotos\51A.jpg"),
            VehicleRealtimeProfiles.Oto);

        Assert.True(result.IsSafe);
        Assert.Equal("51A-123.45", result.Model!.BienSoXe);
        Assert.Equal("51A12345", result.Model.NormalizedBienSoXe);
        Assert.Equal(64, result.Model.SourceImagePathHash!.Length);
        Assert.Contains(
            VehicleRealtimeWarnings.ManagedImageCopyRequired,
            result.Warnings);
        Assert.DoesNotContain(
            @"D:\SourceVehiclePhotos",
            result.Model.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain("AnhRelativePath",
            VehicleRealtimeTargetOwnership.SourceOwnedColumns);
    }

    [Fact]
    public void Mapper_fails_closed_for_wrong_partition_or_target_overflow()
    {
        var wrongPartition = VehicleSourceMapper.Map(
            Source(maCsdt: "66030"),
            VehicleRealtimeProfiles.Oto);
        var overflow = VehicleSourceMapper.Map(
            Source(soDk: new string('X', 51)),
            VehicleRealtimeProfiles.Oto);

        Assert.False(wrongPartition.IsSafe);
        Assert.Contains(
            VehicleRealtimeReviewCodes.WrongSourcePartition,
            wrongPartition.Blockers);
        Assert.False(overflow.IsSafe);
        Assert.Contains(
            $"{VehicleRealtimeReviewCodes.SourceValueTooLong}:SoDK",
            overflow.Blockers);
    }

    [Fact]
    public void Mapping_hash_is_stable_and_changes_only_when_mapped_source_changes()
    {
        var first = VehicleSourceMapper.Map(Source(), VehicleRealtimeProfiles.Oto);
        var same = VehicleSourceMapper.Map(Source(), VehicleRealtimeProfiles.Oto);
        var changed = VehicleSourceMapper.Map(
            Source(nhanHieu: "TOYOTA"),
            VehicleRealtimeProfiles.Oto);

        Assert.Equal(first.Model!.SourceRowHash, same.Model!.SourceRowHash);
        Assert.NotEqual(first.Model.SourceRowHash, changed.Model!.SourceRowHash);
        Assert.Equal(64, VehicleSourceMapper.ComputeMappingFingerprint().Length);
    }

    [Fact]
    public void Cross_profile_plate_collision_is_manual_review_not_merge()
    {
        var source = VehicleSourceMapper.Map(
            Source(maCsdt: "66030"),
            VehicleRealtimeProfiles.Moto);
        var target = Target(
            id: 7,
            profile: VehicleRealtimeProfiles.Oto,
            sourcePlate: "51A-123.45",
            normalizedPlate: "51A12345");

        var plan = VehicleRealtimePlanner.PlanUpsert(source, [target], 12);

        Assert.Equal(VehicleRealtimeActions.ManualReview, plan.Action);
        Assert.Equal(
            VehicleRealtimeReviewCodes.CrossProfilePlateCollision,
            plan.ReviewCode);
        Assert.Equal(7, plan.ConflictingXeTapId);
        Assert.False(plan.MutatesVehicle);
    }

    [Theory]
    [InlineData("SoDK", "REG-001", "REG001", "REGISTRATION_COLLISION")]
    [InlineData("SoKhung", "VIN-001", "VIN001", "CHASSIS_COLLISION")]
    [InlineData("SoDongCo", "ENG-001", "ENG001", "ENGINE_COLLISION")]
    public void Secondary_identity_collision_is_manual_review(
        string field,
        string value,
        string normalized,
        string expectedReview)
    {
        var row = Source(
            soDk: field == "SoDK" ? value : "REG-OTHER",
            soKhung: field == "SoKhung" ? value : "VIN-OTHER",
            soDongCo: field == "SoDongCo" ? value : "ENG-OTHER");
        var source = VehicleSourceMapper.Map(row, VehicleRealtimeProfiles.Oto);
        var target = Target(
            id: 9,
            profile: VehicleRealtimeProfiles.Oto,
            sourcePlate: "51A-999.99",
            normalizedPlate: "51A99999",
            soDk: field == "SoDK" ? value : "REG-TARGET",
            normalizedSoDk: field == "SoDK" ? normalized : "REGTARGET",
            soKhung: field == "SoKhung" ? value : "VIN-TARGET",
            normalizedSoKhung: field == "SoKhung" ? normalized : "VINTARGET",
            soDongCo: field == "SoDongCo" ? value : "ENG-TARGET",
            normalizedSoDongCo: field == "SoDongCo" ? normalized : "ENGTARGET");

        var plan = VehicleRealtimePlanner.PlanUpsert(source, [target], 13);

        Assert.Equal(VehicleRealtimeActions.ManualReview, plan.Action);
        Assert.Equal(expectedReview, plan.ReviewCode);
        Assert.Equal(field, plan.CollisionField);
    }

    [Fact]
    public void Source_inactive_with_assignment_is_retained_for_manual_review()
    {
        var source = VehicleSourceMapper.Map(
            Source(active: false),
            VehicleRealtimeProfiles.Oto);
        var target = Target(
            id: 10,
            profile: VehicleRealtimeProfiles.Oto,
            sourcePlate: "51A-123.45",
            normalizedPlate: "51A12345",
            assigned: true);

        var plan = VehicleRealtimePlanner.PlanUpsert(source, [target], 14);

        Assert.Equal(VehicleRealtimeActions.ManualReview, plan.Action);
        Assert.Equal(
            VehicleRealtimeReviewCodes.SourceInactiveWithAssignment,
            plan.ReviewCode);
        Assert.False(plan.MutatesVehicle);
    }

    [Fact]
    public void Source_inactive_without_assignment_updates_only_source_lifecycle()
    {
        var source = VehicleSourceMapper.Map(
            Source(active: false),
            VehicleRealtimeProfiles.Oto);
        var target = Target(
            id: 11,
            profile: VehicleRealtimeProfiles.Oto,
            sourcePlate: "51A-123.45",
            normalizedPlate: "51A12345");

        var plan = VehicleRealtimePlanner.PlanUpsert(source, [target], 15);

        Assert.Equal(VehicleRealtimeActions.MarkSourceInactive, plan.Action);
        Assert.Equal(VehicleRealtimeLifecycles.SourceInactive, plan.Lifecycle);
        Assert.True(plan.MutatesVehicle);
    }

    [Fact]
    public void Source_delete_never_hard_deletes_and_assignment_forces_review()
    {
        var identity = VehicleSourceIdentity.Create(
            VehicleRealtimeProfiles.Oto,
            "51A-123.45");
        var unassigned = Target(
            id: 12,
            profile: VehicleRealtimeProfiles.Oto,
            sourcePlate: identity.SourceBienSoXe,
            normalizedPlate: "51A12345");
        var assigned = unassigned with { HasActiveAssignments = true };

        var missingPlan = VehicleRealtimePlanner.PlanDelete(identity, [unassigned], 16);
        var assignedPlan = VehicleRealtimePlanner.PlanDelete(identity, [assigned], 16);

        Assert.Equal(VehicleRealtimeActions.MarkSourceMissing, missingPlan.Action);
        Assert.Equal(VehicleRealtimeLifecycles.SourceMissing, missingPlan.Lifecycle);
        Assert.Equal(VehicleRealtimeActions.ManualReview, assignedPlan.Action);
        Assert.Equal(
            VehicleRealtimeReviewCodes.SourceMissingWithAssignment,
            assignedPlan.ReviewCode);
        Assert.DoesNotContain(
            "DELETE",
            VehicleRealtimeActions.MarkSourceMissing,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Equal_hash_and_lifecycle_is_no_change_without_row_mutation()
    {
        var mapping = VehicleSourceMapper.Map(Source(), VehicleRealtimeProfiles.Oto);
        var target = Target(
            id: 13,
            profile: VehicleRealtimeProfiles.Oto,
            sourcePlate: mapping.Model!.Identity.SourceBienSoXe,
            normalizedPlate: mapping.Model.NormalizedBienSoXe) with
        {
            SourceRowHash = mapping.Model.SourceRowHash,
            SourceTrangThai = true,
            SourceLifecycle = VehicleRealtimeLifecycles.Active,
        };

        var plan = VehicleRealtimePlanner.PlanUpsert(mapping, [target], 17);

        Assert.Equal(VehicleRealtimeActions.NoChange, plan.Action);
        Assert.False(plan.MutatesVehicle);
    }

    [Fact]
    public async Task Unrelated_continuous_source_commits_do_not_block_a_sealed_vehicle_cycle()
    {
        var fixture = CycleFixture(revalidateKeys: true);

        var result = await fixture.Processor.ProcessAsync(VehicleRealtimeProfiles.Oto);

        Assert.Equal(101, result.CheckpointAfter);
        Assert.Equal(1, fixture.Target.CommitCalls);
        Assert.Equal(1, fixture.Source.RevalidateCalls);
    }

    [Fact]
    public async Task Later_change_to_the_same_vehicle_key_retries_without_target_commit()
    {
        var fixture = CycleFixture(revalidateKeys: false);

        var exception = await Assert.ThrowsAsync<VehicleRealtimeSafetyException>(
            () => fixture.Processor.ProcessAsync(VehicleRealtimeProfiles.Oto));

        Assert.Equal(
            VehicleRealtimeErrorCodes.SourceChangedDuringPlan,
            exception.Code);
        Assert.Equal(0, fixture.Target.CommitCalls);
    }

    [Fact]
    public void Writer_update_allowlist_excludes_every_qlhv_owned_column()
    {
        var setClause = SqlVehicleRealtimeTargetStore.UpdateVehicleSql.Split(
            "WHERE",
            2,
            StringSplitOptions.None)[0];
        foreach (var column in VehicleRealtimeTargetOwnership.QlhvOwnedColumns)
        {
            Assert.DoesNotMatch(
                new Regex(
                    $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(column)}\s*=",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                setClause);
        }

        Assert.DoesNotContain(
            "DELETE FROM dbo.App_XeTap",
            SqlVehicleRealtimeTargetStore.UpdateVehicleSql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "RowVersion=@ExpectedTargetRowVersion",
            SqlVehicleRealtimeTargetStore.UpdateVehicleSql,
            StringComparison.Ordinal);
    }

    private static (
        VehicleRealtimeCycleProcessor Processor,
        FakeSourceFeed Source,
        FakeTargetStore Target) CycleFixture(bool revalidateKeys)
    {
        var route = VehicleRealtimeRouteCatalog.Oto;
        var schema = new string('b', 64);
        var checkpoint = new VehicleRealtimeCheckpoint(
            route.SourceProfileCode,
            route.ExpectedProductionDatabaseGuid,
            100,
            VehicleSourceMapper.ComputeMappingFingerprint(),
            schema,
            "ACTIVE",
            [1, 2, 3, 4, 5, 6, 7, 8]);
        var capability = new VehicleSourceCapability(
            route.SourceProfileCode,
            route.SourceDatabaseName,
            route.ExpectedProductionDatabaseGuid,
            true,
            true,
            true,
            101,
            0,
            schema);
        var batch = new VehicleSourceBatch(capability, 100, 101, 101, []);
        var source = new FakeSourceFeed(batch, revalidateKeys);
        var target = new FakeTargetStore(
            new VehicleTargetPlanningSnapshot(checkpoint, []));
        return (new VehicleRealtimeCycleProcessor(source, target), source, target);
    }

    private static VehicleSourceRow Source(
        string maCsdt = "66029",
        string soDk = "REG-001",
        string soKhung = "VIN-001",
        string soDongCo = "ENG-001",
        string nhanHieu = "HONDA",
        bool active = true,
        string? imagePath = null)
        => new()
        {
            BienSoXe = "51A-123.45",
            MaSoGTVT = "66000",
            MaCSDT = maCsdt,
            SoDK = soDk,
            SoHuu = false,
            NhanHieu = nhanHieu,
            LoaiXe = "SEDAN",
            MacXe = "MODEL",
            HangXe = "HANG",
            MauXe = "TRANG",
            SoDongCo = soDongCo,
            SoKhung = soKhung,
            GiayPhepXTL = true,
            SoGPXTL = "GP-001",
            CoQuanCapGPXTL = "SGTVT",
            NgayCapGPXTL = new DateTime(2026, 1, 1),
            NgayHHGPXTL = new DateTime(2027, 1, 1),
            NamSX = 2025,
            HeThongPP = true,
            NgayCapGCNKD = new DateTime(2026, 1, 2),
            NgayHHGCNKD = new DateTime(2027, 1, 2),
            BaoHiem = true,
            TuyenDuong = "TUYEN",
            ChatLuong = "TOT",
            GhiChu = "SOURCE NOTE",
            TrangThai = active,
            NguoiTao = "SOURCE",
            NguoiSua = "SOURCE",
            NgayTao = new DateTime(2026, 1, 1, 1, 2, 3),
            NgaySua = new DateTime(2026, 1, 2, 1, 2, 3),
            DuongDanAnh = imagePath,
            HangGPLXXe = "B2",
            MaFileTiepNhanXML = "FILE-1",
            ThoiGianTiepNhanXML = new DateTime(2026, 1, 3),
        };

    private static VehicleTargetSnapshot Target(
        long id,
        string profile,
        string sourcePlate,
        string normalizedPlate,
        string soDk = "REG-TARGET",
        string normalizedSoDk = "REGTARGET",
        string soKhung = "VIN-TARGET",
        string normalizedSoKhung = "VINTARGET",
        string soDongCo = "ENG-TARGET",
        string normalizedSoDongCo = "ENGTARGET",
        bool assigned = false)
        => new(
            id,
            profile,
            sourcePlate,
            sourcePlate,
            normalizedPlate,
            new string('a', 64),
            true,
            VehicleRealtimeLifecycles.Active,
            null,
            soDk,
            normalizedSoDk,
            soKhung,
            normalizedSoKhung,
            soDongCo,
            normalizedSoDongCo,
            false,
            assigned,
            [0, 0, 0, 0, 0, 0, 0, (byte)id]);

    private sealed class FakeSourceFeed : IVehicleRealtimeSourceFeed
    {
        private readonly VehicleSourceBatch _batch;
        private readonly bool _revalidate;

        public FakeSourceFeed(VehicleSourceBatch batch, bool revalidate)
        {
            _batch = batch;
            _revalidate = revalidate;
        }

        public int RevalidateCalls { get; private set; }

        public Task<VehicleSourceBatch> ReadNextAsync(
            VehicleRealtimeRoute route,
            VehicleRealtimeCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_batch);

        public Task<bool> RevalidateKeysAsync(
            VehicleRealtimeRoute route,
            VehicleSourceBatch batch,
            CancellationToken cancellationToken = default)
        {
            RevalidateCalls++;
            return Task.FromResult(_revalidate);
        }
    }

    private sealed class FakeTargetStore : IVehicleRealtimeTargetStore
    {
        private readonly VehicleTargetPlanningSnapshot _snapshot;

        public FakeTargetStore(VehicleTargetPlanningSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public int CommitCalls { get; private set; }

        public Task<VehicleTargetPlanningSnapshot> ReadPlanningSnapshotAsync(
            string sourceProfileCode,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot);

        public Task<VehicleRealtimeCycleResult> CommitAsync(
            VehicleRealtimeSealedPlan plan,
            VehicleTargetPlanningSnapshot expectedTarget,
            CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            return Task.FromResult(
                new VehicleRealtimeCycleResult(
                    plan.CycleId,
                    plan.SourceProfileCode,
                    plan.CheckpointBefore,
                    plan.CheckpointAfter,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    plan.PlanToken));
        }
    }
}

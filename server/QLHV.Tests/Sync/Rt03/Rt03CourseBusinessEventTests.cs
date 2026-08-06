using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using QLHV.Application.Sync.Rt03;

namespace QLHV.Tests.Sync.Rt03;

public sealed class Rt03CourseBusinessEventTests
{
    [Fact]
    public void Exact_source_schema_mapping_builds_the_reviewed_course_projection()
    {
        var source = new QlhvKhoaHocSourceRow
        {
            MaKH = " KH-MAP ",
            MaCSDT = " CS01 ",
            MaSoGTVT = " SG01 ",
            TenKH = " Ten khoa ",
            HangGPLX = " B2 ",
            HangDT = " Hang dao tao ",
            SoQD_KhaiGiang = " QD-01 ",
            NgayQD_KhaiGiang = new DateTime(2026, 7, 1),
            NgayKG = new DateTime(2026, 7, 2),
            NgayBG = new DateTime(2026, 9, 2),
            MucTieuDT = " Muc tieu ",
            NgayThi = new DateTime(2026, 9, 3),
            NgaySH = new DateTime(2026, 9, 4),
            TongSoHV = 10,
            SoHVTotNghiep = 9,
            SoHVDuocCapGPLX = 8,
            ThoiGianDT = 90,
            SoNgayOnKT = 2,
            SoNgayThucHoc = 60,
            SoNgayNghiLe = 1,
            TongSoNgay = 63,
            GhiChu = " Ghi chu ",
            TrangThai = false,
            TT_Xuly = 4,
            HTDaoTao = 2,
        };

        var mapped = Assert.IsType<QlhvImportKhoaHocWriteModel>(
            QlhvImportCourseTeacherMapper.MapKhoaHoc(
                source,
                Rt03Profiles.Oto).Model);
        Assert.Equal(Rt03Profiles.Oto, mapped.SourceProfileCode);
        Assert.Equal("KH-MAP", mapped.SourceMaKhoaHoc);
        Assert.Equal("KH-MAP", mapped.MaKhoa);
        Assert.Equal("Ten khoa", mapped.TenKhoa);
        Assert.Equal("CS01", mapped.MaCSDT);
        Assert.Equal("SG01", mapped.MaSoGTVT);
        Assert.Equal("B2", mapped.HangGPLX);
        Assert.Equal("Hang dao tao", mapped.HangDaoTao);
        Assert.Equal("QD-01", mapped.SoQuyetDinhKhaiGiang);
        Assert.Equal(source.NgaySH, mapped.NgaySatHach);
        Assert.Equal(10, mapped.TongSoHocVien);
        Assert.Equal("Ghi chu", mapped.GhiChu);
        Assert.False(mapped.TrangThaiNguon);
        Assert.Equal(4, mapped.TtXuLy);
        Assert.Equal(2, mapped.HinhThucDaoTao);
        Assert.Equal(64, mapped.SourceHash.Length);
    }

    [Fact]
    public void Oto_source_course_without_target_plans_insert()
        => Assert.Equal(
            Rt03CourseBusinessActions.Insert,
            Plan(Course(Rt03Profiles.Oto)).Action);

    [Fact]
    public void Moto_source_course_without_target_plans_insert()
        => Assert.Equal(
            Rt03CourseBusinessActions.Insert,
            Plan(Course(Rt03Profiles.Moto)).Action);

    [Fact]
    public void Replay_with_same_source_hash_plans_no_change()
    {
        var source = Course(Rt03Profiles.Oto);
        Assert.Equal(
            Rt03CourseBusinessActions.NoChange,
            Plan(source, [Target(source)]).Action);
    }

    [Fact]
    public void Changed_source_hash_plans_source_owned_update()
    {
        var source = Course(Rt03Profiles.Oto, hash: "hash-new");
        Assert.Equal(
            Rt03CourseBusinessActions.Update,
            Plan(source, [Target(source, hash: "hash-old")]).Action);
    }

    [Fact]
    public void Soft_deleted_exact_target_is_reactivated_by_update()
    {
        var source = Course(Rt03Profiles.Oto);
        Assert.Equal(
            Rt03CourseBusinessActions.Update,
            Plan(source, [Target(source, isDeleted: true)]).Action);
    }

    [Fact]
    public void Same_MaKhoa_in_other_profile_does_not_steal_identity()
    {
        var source = Course(Rt03Profiles.Moto, key: "SAME-01");
        var oto = Target(
            Course(Rt03Profiles.Oto, key: "SAME-01"),
            id: 9);
        Assert.Equal(
            Rt03CourseBusinessActions.Insert,
            Plan(source, [], [oto]).Action);
    }

    [Fact]
    public void Duplicate_exact_target_identity_is_blocked_ambiguous()
    {
        var source = Course(Rt03Profiles.Oto);
        var exception = Assert.Throws<Rt03SafetyException>(
            () => Plan(
                source,
                [Target(source, id: 1), Target(source, id: 2)]));
        Assert.Equal(Rt03Errors.AmbiguousCourseIdentity, exception.Code);
    }

    [Fact]
    public void Legacy_unpartitioned_MaKhoa_collision_is_blocked_ambiguous()
    {
        var source = Course(Rt03Profiles.Oto);
        var legacy = Target(source, id: 5) with
        {
            SourceProfileCode = null,
            SourceMaKhoaHoc = null,
        };
        var exception = Assert.Throws<Rt03SafetyException>(
            () => Plan(source, [], [legacy]));
        Assert.Equal(Rt03Errors.AmbiguousCourseIdentity, exception.Code);
    }

    [Fact]
    public void Same_profile_natural_key_collision_is_blocked_ambiguous()
    {
        var source = Course(Rt03Profiles.Oto);
        var collision = Target(source, id: 6) with
        {
            SourceMaKhoaHoc = "OTHER-SOURCE-KEY",
        };
        Assert.Equal(
            Rt03Errors.AmbiguousCourseIdentity,
            Assert.Throws<Rt03SafetyException>(
                () => Plan(source, [], [collision])).Code);
    }

    [Fact]
    public void Source_disappearing_before_apply_is_retryable_and_not_approved()
    {
        var source = Course(Rt03Profiles.Oto);
        var exception = Assert.Throws<Rt03SafetyException>(
            () => Rt03CourseBusinessRules.RequireStableSource(source, null));
        Assert.Equal(Rt03Errors.SourceChangedDuringPlan, exception.Code);
        Assert.True(Rt03WorkerFailurePolicy.IsRetryable(exception.Code));
    }

    [Fact]
    public void New_source_event_during_processing_invalidates_sealed_source()
    {
        var planned = Course(Rt03Profiles.Oto, hash: "hash-before");
        var newer = planned with { SourceHash = "hash-after" };
        Assert.Equal(
            Rt03Errors.SourceChangedDuringPlan,
            Assert.Throws<Rt03SafetyException>(
                () => Rt03CourseBusinessRules.RequireStableSource(
                    planned,
                    newer)).Code);
    }

    [Fact]
    public void Stable_source_revalidation_accepts_exact_replay()
    {
        var source = Course(Rt03Profiles.Moto);
        Rt03CourseBusinessRules.RequireStableSource(source, source with { });
    }

    [Fact]
    public void Qlhv_owned_fields_with_same_fingerprint_are_preserved()
        => Rt03CourseBusinessRules.RequireQlhvOwnedFingerprintUnchanged(
            "owned-before",
            "owned-before");

    [Fact]
    public void Qlhv_owned_field_change_fails_transaction_verification()
        => Assert.Equal(
            Rt03Errors.TargetDrift,
            Assert.Throws<Rt03SafetyException>(
                () => Rt03CourseBusinessRules
                    .RequireQlhvOwnedFingerprintUnchanged(
                        "owned-before",
                        "owned-after")).Code);

    [Fact]
    public void Course_can_converge_before_dependent_learner_insert()
    {
        var source = Course(Rt03Profiles.Oto, key: "COURSE-FIRST");
        var plan = Plan(source);
        Assert.Equal(Rt03CourseBusinessActions.Insert, plan.Action);

        var converged = Target(source);
        Assert.Same(
            converged,
            Rt03CourseBusinessRules.RequireLearnerCourse(
                source.SourceProfileCode,
                source.SourceMaKhoaHoc,
                [converged]));
    }

    [Fact]
    public void Learner_insert_without_converged_course_is_blocked()
        => Assert.Equal(
            Rt03Errors.LearnerCourseNotConvergent,
            Assert.Throws<Rt03SafetyException>(
                () => Rt03CourseBusinessRules.RequireLearnerCourse(
                    Rt03Profiles.Oto,
                    "MISSING",
                    [])).Code);

    [Fact]
    public void Learner_insert_for_inactive_source_course_is_blocked()
    {
        var source = Course(Rt03Profiles.Oto, active: false);
        Assert.Equal(
            Rt03Errors.LearnerCourseNotConvergent,
            Assert.Throws<Rt03SafetyException>(
                () => Rt03CourseBusinessRules.RequireLearnerCourse(
                    source.SourceProfileCode,
                    source.SourceMaKhoaHoc,
                    [Target(source)])).Code);
    }

    [Fact]
    public void Explicitly_inactive_source_course_still_converges_as_course()
    {
        var source = Course(Rt03Profiles.Oto, active: false);
        Assert.False(source.TrangThaiNguon);
        Assert.Equal(Rt03CourseBusinessActions.Insert, Plan(source).Action);
    }

    [Fact]
    public void Repeated_course_event_does_not_create_a_second_target()
    {
        var source = Course(Rt03Profiles.Oto);
        var first = Plan(source);
        var persisted = Target(first.Source);
        var replay = Plan(source, [persisted], [persisted]);
        Assert.Equal(Rt03CourseBusinessActions.NoChange, replay.Action);
        Assert.Equal(persisted.KhoaHocId, replay.Target!.KhoaHocId);
    }

    [Fact]
    public void Six_learner_event_replays_are_all_no_change_and_create_no_duplicate()
    {
        var keys = Enumerable.Range(1, 6)
            .Select(index => $"LEARNER-{index:00}")
            .ToArray();
        var sources = keys.Select(key => new Rt03LearnerReplayIdentity(
            Rt03Profiles.Oto,
            key,
            $"hash-{key}")).ToArray();
        var targets = sources.Select(source => source with { }).ToArray();

        Assert.True(Rt03LearnerReplayRules.AreAllConverged(
            Rt03Profiles.Oto,
            keys,
            sources,
            targets));
        Assert.Equal(6, targets.Select(row => row.SourceMaDK).Distinct().Count());
    }

    [Fact]
    public void Learner_replay_with_hash_drift_is_not_silently_advanced()
    {
        var source = new Rt03LearnerReplayIdentity(
            Rt03Profiles.Oto,
            "LEARNER-01",
            "hash-new");
        var target = source with { SourceHash = "hash-old" };
        Assert.False(Rt03LearnerReplayRules.AreAllConverged(
            Rt03Profiles.Oto,
            [source.SourceMaDK],
            [source],
            [target]));
    }

    [Fact]
    public void Learner_delete_replay_is_noop_when_exact_pair_and_both_sides_absent()
    {
        Assert.Equal(
            Rt03LearnerReplayDisposition.IdempotentDeleteAlreadyAbsent,
            Rt03LearnerReplayRules.ClassifyConvergedReplay(
            Rt03Profiles.Moto,
            DeletePair("MOTO-DELETED-IDENTITY"),
            [],
            []));
    }

    [Fact]
    public void Learner_delete_replay_is_repeatably_idempotent()
    {
        var events = DeletePair("MOTO-DELETED-IDENTITY");
        var first = Rt03LearnerReplayRules.ClassifyConvergedReplay(
            Rt03Profiles.Moto,
            events,
            [],
            []);
        var retry = Rt03LearnerReplayRules.ClassifyConvergedReplay(
            Rt03Profiles.Moto,
            events,
            [],
            []);
        Assert.Equal(first, retry);
        Assert.Equal(Rt03LearnerReplayDisposition.IdempotentDeleteAlreadyAbsent, retry);
    }

    [Fact]
    public void Learner_delete_replay_blocks_when_an_active_or_protected_target_exists()
    {
        Assert.Equal(
            Rt03LearnerReplayDisposition.Blocked,
            Rt03LearnerReplayRules.ClassifyConvergedReplay(
                Rt03Profiles.Moto,
                DeletePair("MOTO-DELETED-IDENTITY"),
                [],
                [new Rt03LearnerReplayIdentity(
                    Rt03Profiles.Moto,
                    "MOTO-DELETED-IDENTITY",
                    "TARGET-HASH")]));
    }

    [Fact]
    public void Learner_delete_replay_blocks_when_a_retained_target_exists()
    {
        Assert.Equal(
            Rt03LearnerReplayDisposition.Blocked,
            Rt03LearnerReplayRules.ClassifyConvergedReplay(
                Rt03Profiles.Moto,
                DeletePair("MOTO-DELETED-IDENTITY"),
                [],
                [new Rt03LearnerReplayIdentity(
                    Rt03Profiles.Moto,
                    "MOTO-DELETED-IDENTITY",
                    "TARGET-HASH",
                    IsDeleted: true)]));
    }

    [Fact]
    public void Learner_delete_replay_blocks_duplicate_target_identity()
    {
        var target = new Rt03LearnerReplayIdentity(
            Rt03Profiles.Moto,
            "MOTO-DELETED-IDENTITY",
            "TARGET-HASH");
        Assert.Equal(
            Rt03LearnerReplayDisposition.Blocked,
            Rt03LearnerReplayRules.ClassifyConvergedReplay(
                Rt03Profiles.Moto,
                DeletePair("MOTO-DELETED-IDENTITY"),
                [],
                [target, target with { SourceHash = "SECOND-HASH" }]));
    }

    [Fact]
    public void Learner_delete_replay_blocks_missing_identity()
    {
        Assert.Equal(
            Rt03LearnerReplayDisposition.Blocked,
            Rt03LearnerReplayRules.ClassifyConvergedReplay(
                Rt03Profiles.Moto,
                [new Rt03LearnerReplayEvent("dbo.NguoiLX", "D", " ")],
                [],
                []));
    }

    [Fact]
    public void Learner_delete_replay_blocks_when_source_still_exists_without_target()
    {
        Assert.Equal(
            Rt03LearnerReplayDisposition.Blocked,
            Rt03LearnerReplayRules.ClassifyConvergedReplay(
                Rt03Profiles.Moto,
                DeletePair("MOTO-DELETED-IDENTITY"),
                [new Rt03LearnerReplayIdentity(
                    Rt03Profiles.Moto,
                    "MOTO-DELETED-IDENTITY",
                    "SOURCE-HASH")],
                []));
    }

    [Theory]
    [InlineData("I")]
    [InlineData("U")]
    public void Learner_absent_replay_requires_delete_operation(string operation)
    {
        Assert.Equal(
            Rt03LearnerReplayDisposition.Blocked,
            Rt03LearnerReplayRules.ClassifyConvergedReplay(
                Rt03Profiles.Moto,
                [
                    new Rt03LearnerReplayEvent(
                        "dbo.NguoiLX", operation, "MOTO-DELETED-IDENTITY"),
                    new Rt03LearnerReplayEvent(
                        "dbo.NguoiLX_HoSo", operation, "MOTO-DELETED-IDENTITY"),
                ],
                [],
                []));
    }

    [Fact]
    public void Learner_delete_replay_requires_the_exact_master_and_file_pair()
    {
        Assert.Equal(
            Rt03LearnerReplayDisposition.Blocked,
            Rt03LearnerReplayRules.ClassifyConvergedReplay(
                Rt03Profiles.Moto,
                [new Rt03LearnerReplayEvent(
                    "dbo.NguoiLX", "D", "MOTO-DELETED-IDENTITY")],
                [],
                []));
    }

    [Fact]
    public void Planner_has_no_fixed_historical_row_count()
    {
        var plans = Enumerable.Range(1, 7)
            .Select(index => Plan(Course(
                Rt03Profiles.Oto,
                key: $"COURSE-{index:00}")))
            .ToArray();
        Assert.Equal(7, plans.Length);
        Assert.All(
            plans,
            plan => Assert.Equal(Rt03CourseBusinessActions.Insert, plan.Action));
    }

    [Fact]
    public void Checkpoint_is_publishable_only_after_verified_commit_marker()
    {
        Assert.Equal(
            Rt03RecoveryDisposition.RollbackOpenTransaction,
            Rt03RecoveryPlanner.Decide(new Rt03RecoverySnapshot(
                TransactionStarted: true,
                CommitConfirmed: false,
                CommitAmbiguous: false,
                MarkerExists: false,
                MarkerMatchesPlan: false,
                IntegrityVerified: false,
                CheckpointExists: false,
                CheckpointMatchesMarker: false)));
        Assert.Equal(
            Rt03RecoveryDisposition.VerifyMarkerThenPublishCheckpoint,
            Rt03RecoveryPlanner.Decide(new Rt03RecoverySnapshot(
                TransactionStarted: true,
                CommitConfirmed: true,
                CommitAmbiguous: false,
                MarkerExists: true,
                MarkerMatchesPlan: true,
                IntegrityVerified: true,
                CheckpointExists: false,
                CheckpointMatchesMarker: false)));
    }

    private static Rt03CourseBusinessPlan Plan(
        QlhvImportKhoaHocWriteModel source,
        IReadOnlyCollection<Rt03CourseTargetIdentity>? exact = null,
        IReadOnlyCollection<Rt03CourseTargetIdentity>? sameMaKhoa = null)
        => Rt03CourseBusinessRules.Plan(
            source,
            exact ?? [],
            sameMaKhoa ?? exact ?? []);

    private static Rt03CourseTargetIdentity Target(
        QlhvImportKhoaHocWriteModel source,
        long id = 1,
        string? hash = null,
        bool isDeleted = false)
        => new(
            id,
            source.SourceProfileCode,
            source.SourceMaKhoaHoc,
            hash ?? source.SourceHash,
            source.MaKhoa,
            source.TrangThaiNguon,
            isDeleted);

    private static QlhvImportKhoaHocWriteModel Course(
        string profile,
        string key = "KH-01",
        string hash = "hash-01",
        bool active = true)
        => new(
            profile,
            key,
            hash,
            key,
            "Khoa hoc",
            "CSDT01",
            "SGTVT1",
            "B2",
            "B2",
            "QD-01",
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 2),
            new DateTime(2026, 9, 2),
            "Muc tieu",
            new DateTime(2026, 9, 3),
            new DateTime(2026, 9, 4),
            10,
            9,
            8,
            90,
            2,
            60,
            1,
            63,
            "Ghi chu nguon",
            active,
            0,
            1);

    private static Rt03LearnerReplayEvent[] DeletePair(string sourceMaDk) =>
    [
        new("dbo.NguoiLX", "D", sourceMaDk),
        new("dbo.NguoiLX_HoSo", "D", sourceMaDk),
    ];
}

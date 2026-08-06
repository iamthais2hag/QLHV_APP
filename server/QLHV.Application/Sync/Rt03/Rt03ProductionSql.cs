namespace QLHV.Application.Sync.Rt03;

/// <summary>
/// Fixed, parameterized SQL reviewed for the separately approved RT-03 Task 2
/// operator. No production host registers or executes these commands in Task 1.
/// Apply commands never delete, deactivate, reassign a profile, or use dynamic SQL.
/// Rollback commands are isolated in a separate list and require exact IDs/hashes.
/// </summary>
public static class Rt03ProductionSql
{
    public const string RevalidateTargetIdentity = """
        SELECT
            DB_NAME() AS DatabaseName,
            DB_ID() AS DatabaseId,
            CONVERT(nvarchar(36), databaseIdentity.database_guid) AS DatabaseGuid
        FROM sys.database_recovery_status AS databaseIdentity
        WHERE databaseIdentity.database_id = DB_ID()
          AND DB_NAME() = N'QLHV_APP'
          AND DB_ID() = 12
          AND databaseIdentity.database_guid =
              '9C44B304-8A84-4D0D-9A82-19C7233FF6BB';
        """;

    public const string AcquireProductionProfileLock = """
        DECLARE @LockResult int;
        EXEC @LockResult = sys.sp_getapplock
            @Resource = @ExactProfileLockName,
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 0;
        IF @LockResult < 0
            THROW 527530, 'RT03_PROFILE_LOCK_REJECTED', 1;
        """;

    public const string RejectActiveAutoSync = """
        IF EXISTS
        (
            SELECT 1
            FROM dbo.App_QlhvAutoSyncRun WITH (UPDLOCK, HOLDLOCK)
            WHERE Status IN (N'QUEUED', N'RUNNING') OR ActiveSlot = 1
        )
        OR EXISTS
        (
            SELECT 1
            FROM dbo.App_QlhvSyncOperationHistory WITH (UPDLOCK, HOLDLOCK)
            WHERE Status IN (N'QUEUED', N'RUNNING')
        )
            THROW 527531, 'RT03_AUTOSYNC_ACTIVE', 1;
        """;

    public const string RecheckExactLearner = """
        SELECT
            HocVienId,
            SourceProfileCode,
            SourceMaDK,
            V2RowHash,
            IsDeleted,
            GhiChuNoiBo,
            DaDoiChieuCCCD,
            DaInThe,
            DaTaoXML,
            CreatedBy,
            UpdatedBy,
            DeletedBy,
            DeleteReason
        FROM dbo.App_HocVien WITH (UPDLOCK, HOLDLOCK)
        WHERE SourceProfileCode = @SourceProfileCode
          AND SourceMaDK = @SourceMaDK;
        """;

    public const string RecheckExactCourse = """
        SELECT
            KhoaHocId, SourceProfileCode, SourceMaKhoaHoc, SourceHash,
            V2RowHash, SourceOfTruth,
            MaKhoa, TenKhoa, MaCSDT, MaSoGTVT, HangGPLX, HangDaoTao,
            SoQuyetDinhKhaiGiang, NgayQuyetDinhKhaiGiang,
            NgayKhaiGiang, NgayBeGiang, MucTieuDaoTao, NgayThi,
            NgaySatHach, TongSoHocVien, SoHocVienTotNghiep,
            SoHocVienDuocCapGPLX, ThoiGianDaoTao, SoNgayOnKiemTra,
            SoNgayThucHoc, SoNgayNghiLe, TongSoNgay, GhiChuV2,
            TrangThaiNguon, TtXuLy, HinhThucDaoTao, IsDeleted,
            GhiChuNoiBo, TrangThai, NgayBatDauThucHanh, LuuLuongDaoTao,
            CreatedAt, CreatedBy, RowVersion
        FROM dbo.App_KhoaHoc WITH (UPDLOCK, HOLDLOCK)
        WHERE SourceProfileCode = @SourceProfileCode
          AND SourceMaKhoaHoc = @SourceMaKhoaHoc;
        """;

    public const string RecheckSameMaKhoaCourses = """
        SELECT
            KhoaHocId, SourceProfileCode, SourceMaKhoaHoc, SourceHash,
            MaKhoa, CONVERT(bit, COALESCE(TrangThaiNguon, 0)) AS TrangThaiNguon,
            IsDeleted
        FROM dbo.App_KhoaHoc WITH (UPDLOCK, HOLDLOCK)
        WHERE MaKhoa = @MaKhoa;
        """;

    public const string ResolveLearnerCourse = """
        SELECT
            KhoaHocId, SourceProfileCode, SourceMaKhoaHoc, SourceHash,
            MaKhoa, CONVERT(bit, COALESCE(TrangThaiNguon, 0)) AS TrangThaiNguon,
            IsDeleted
        FROM dbo.App_KhoaHoc WITH (UPDLOCK, HOLDLOCK)
        WHERE SourceProfileCode = @SourceProfileCode
          AND SourceMaKhoaHoc = @SourceMaKhoaHoc;
        """;

    public const string InsertProductionCourse = """
        INSERT INTO dbo.App_KhoaHoc
        (
            MaKhoa, TenKhoa, HangDaoTao, NgayKhaiGiang, NgayBeGiang,
            TongSoHocVien, GhiChuV2, SourceOfTruth, V2RowHash,
            LastSyncFromV2At, LastSyncStatus, IsDeleted, CreatedBy,
            SourceProfileCode, SourceMaKhoaHoc, SourceHash, MaCSDT,
            MaSoGTVT, HangGPLX, SoQuyetDinhKhaiGiang,
            NgayQuyetDinhKhaiGiang, MucTieuDaoTao, NgayThi,
            NgaySatHach, SoHocVienTotNghiep, SoHocVienDuocCapGPLX,
            ThoiGianDaoTao, SoNgayOnKiemTra, SoNgayThucHoc,
            SoNgayNghiLe, TongSoNgay, TrangThaiNguon, TtXuLy,
            HinhThucDaoTao, CreatedAt, CreatedAtUtc, UpdatedAtUtc
        )
        VALUES
        (
            @MaKhoa, @TenKhoa, @HangDaoTao, @NgayKhaiGiang, @NgayBeGiang,
            @TongSoHocVien, @GhiChu, N'V2', @SourceHash,
            @CommittedAtUtc, N'ThanhCong', 0, N'Rt03DirectRealtimeWorker',
            @SourceProfileCode, @SourceMaKhoaHoc, @SourceHash, @MaCSDT,
            @MaSoGTVT, @HangGPLX, @SoQuyetDinhKhaiGiang,
            @NgayQuyetDinhKhaiGiang, @MucTieuDaoTao, @NgayThi,
            @NgaySatHach, @SoHocVienTotNghiep, @SoHocVienDuocCapGPLX,
            @ThoiGianDaoTao, @SoNgayOnKiemTra, @SoNgayThucHoc,
            @SoNgayNghiLe, @TongSoNgay, @TrangThaiNguon, @TtXuLy,
            @HinhThucDaoTao, @CommittedAtUtc, @CommittedAtUtc, @CommittedAtUtc
        );
        """;

    public const string UpdateProductionCourse = """
        UPDATE dbo.App_KhoaHoc
        SET MaKhoa = @MaKhoa,
            TenKhoa = @TenKhoa,
            HangDaoTao = @HangDaoTao,
            NgayKhaiGiang = @NgayKhaiGiang,
            NgayBeGiang = @NgayBeGiang,
            TongSoHocVien = @TongSoHocVien,
            GhiChuV2 = @GhiChu,
            SourceOfTruth = N'V2',
            V2RowHash = @SourceHash,
            LastSyncFromV2At = @CommittedAtUtc,
            LastSyncStatus = N'ThanhCong',
            IsDeleted = 0,
            DeletedAt = NULL,
            DeletedBy = NULL,
            DeleteReason = NULL,
            SourceHash = @SourceHash,
            MaCSDT = @MaCSDT,
            MaSoGTVT = @MaSoGTVT,
            HangGPLX = @HangGPLX,
            SoQuyetDinhKhaiGiang = @SoQuyetDinhKhaiGiang,
            NgayQuyetDinhKhaiGiang = @NgayQuyetDinhKhaiGiang,
            MucTieuDaoTao = @MucTieuDaoTao,
            NgayThi = @NgayThi,
            NgaySatHach = @NgaySatHach,
            SoHocVienTotNghiep = @SoHocVienTotNghiep,
            SoHocVienDuocCapGPLX = @SoHocVienDuocCapGPLX,
            ThoiGianDaoTao = @ThoiGianDaoTao,
            SoNgayOnKiemTra = @SoNgayOnKiemTra,
            SoNgayThucHoc = @SoNgayThucHoc,
            SoNgayNghiLe = @SoNgayNghiLe,
            TongSoNgay = @TongSoNgay,
            TrangThaiNguon = @TrangThaiNguon,
            TtXuLy = @TtXuLy,
            HinhThucDaoTao = @HinhThucDaoTao,
            UpdatedAt = @CommittedAtUtc,
            UpdatedAtUtc = @CommittedAtUtc,
            UpdatedBy = N'Rt03DirectRealtimeWorker'
        WHERE KhoaHocId = @KhoaHocId
          AND SourceProfileCode = @SourceProfileCode
          AND SourceMaKhoaHoc = @SourceMaKhoaHoc
          AND RowVersion = @ExpectedRowVersion;

        IF @@ROWCOUNT <> 1
            THROW 527538, 'RT03_KHOAHOC_TARGET_CHANGED_SINCE_PLAN', 1;
        """;

    public const string InsertExactLearner = """
        INSERT INTO dbo.App_HocVien
        (
            SourceProfileCode, SourceMaDK, SourceSystem, SourceVersion,
            MaDK, MaKhoa, TenKhoa, MaHangDT, HangGPLXHoc, HoTen, NgaySinh,
            GioiTinh, SoCCCD, DiaChiThuongTru, SoGPLXDaCo, HangGPLXDaCo,
            NguoiNhanHoSo, AnhRelativePath, ChatLuongAnh, NgayThuNhanAnh,
            NguoiThuNhanAnh, SourceOfTruth, V2RowHash, IsDeleted,
            LastSyncFromV2At, CreatedBy
        )
        VALUES
        (
            @SourceProfileCode, @SourceMaDK, N'V2', N'V2',
            @MaDK, @MaKhoa, @TenKhoa, @MaHangDT, @HangGPLXHoc, @HoTen,
            @NgaySinh, @GioiTinh, @SoCCCD, @DiaChiThuongTru, @SoGPLXDaCo,
            @HangGPLXDaCo, @NguoiNhanHoSo, @AnhRelativePath, @ChatLuongAnh,
            @NgayThuNhanAnh, @NguoiThuNhanAnh, @SourceOfTruth, @SourceRowHash,
            0, @CommittedAtUtc, N'Rt03DirectRealtimeCanary'
        );
        """;

    public const string InsertProductionLearner = """
        INSERT INTO dbo.App_HocVien
        (
            SourceProfileCode, SourceMaDK, SourceSystem, SourceVersion,
            MaDK, MaKhoa, TenKhoa, MaHangDT, HangGPLXHoc, HoTen, NgaySinh,
            GioiTinh, SoCCCD, DiaChiThuongTru, SoGPLXDaCo, HangGPLXDaCo,
            NguoiNhanHoSo, AnhRelativePath, ChatLuongAnh, NgayThuNhanAnh,
            NguoiThuNhanAnh, SourceOfTruth, V2RowHash, IsDeleted,
            LastSyncFromV2At, CreatedBy
        )
        VALUES
        (
            @SourceProfileCode, @SourceMaDK, N'V2', N'V2',
            @MaDK, @MaKhoa, @TenKhoa, @MaHangDT, @HangGPLXHoc, @HoTen,
            @NgaySinh, @GioiTinh, @SoCCCD, @DiaChiThuongTru, @SoGPLXDaCo,
            @HangGPLXDaCo, @NguoiNhanHoSo, @AnhRelativePath, @ChatLuongAnh,
            @NgayThuNhanAnh, @NguoiThuNhanAnh, @SourceOfTruth, @SourceRowHash,
            0, @CommittedAtUtc, N'Rt03DirectRealtimeWorker'
        );
        """;

    public const string UpdateExactHoTen = """
        UPDATE dbo.App_HocVien
        SET HoTen = @DesiredHoTen,
            V2RowHash = @SourceRowHash
        WHERE HocVienId = @TargetHocVienId
          AND SourceProfileCode = @SourceProfileCode
          AND SourceMaDK = @SourceMaDK
          AND IsDeleted = 0
          AND V2RowHash = @ExpectedMappedHash;

        IF @@ROWCOUNT <> 1
            THROW 527532, 'RT03_TARGET_CHANGED_SINCE_PLAN', 1;
        """;

    public const string InsertManualReview = """
        INSERT INTO dbo.App_QlhvDirectRealtimeManualReview
        (
            CycleId, PlanHash, CandidateId, SourceProfileCode, IdentityHmac,
            Classification, RollbackImageHash, TargetRetainedActive,
            TargetMutated, CreatedAtUtc
        )
        VALUES
        (
            @CycleId, @PlanHash, @CandidateId, @SourceProfileCode, @IdentityHmac,
            @Classification, @RollbackImageHash, 1, 0, @CommittedAtUtc
        );
        """;

    public const string InsertApplyMarker = """
        INSERT INTO dbo.App_QlhvDirectRealtimeApplyMarker
        (
            CycleId, SourceProfileCode, PlanHash, MarkerHash, DispositionHash,
            SourceDatabaseGuid, SourceChangeTrackingVersion,
            InsertedRows, UpdatedRows, RetainedRows, PreservedQlhvOwnedHash,
            CommittedAtUtc
        )
        VALUES
        (
            @CycleId, @SourceProfileCode, @PlanHash, @MarkerHash, @DispositionHash,
            @SourceDatabaseGuid, @SourceChangeTrackingVersion,
            @InsertedRows, @UpdatedRows, @RetainedRows,
            @PreservedQlhvOwnedHash, @CommittedAtUtc
        );
        """;

    public const string PublishCheckpointAfterVerifiedCommit = """
        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.App_QlhvDirectRealtimeApplyMarker
            WHERE CycleId = @CycleId
              AND PlanHash = @PlanHash
              AND MarkerHash = @MarkerHash
        )
            THROW 527533, 'RT03_COMMITTED_MARKER_MISSING', 1;

        IF EXISTS
        (
            SELECT 1
            FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint WITH (UPDLOCK, HOLDLOCK)
            WHERE SourceProfileCode = @SourceProfileCode
              AND Mode = N'DIRECT_REALTIME_APPLY'
              AND MappingFingerprint = @MappingFingerprint
              AND EnvironmentId = N'PRODUCTION'
              AND
              (
                  CycleId <> @CycleId
                  OR PlanHash <> @PlanHash
                  OR MarkerHash <> @MarkerHash
                  OR SourceChangeTrackingVersion <> @SourceChangeTrackingVersion
              )
        )
            THROW 527537, 'RT03_CHECKPOINT_CONFLICT', 1;

        INSERT INTO dbo.App_QlhvDirectRealtimeApplyCheckpoint
        (
            SourceProfileCode, Mode, MappingFingerprint, EnvironmentId,
            SourceDatabaseGuid, SourceChangeTrackingVersion, CycleId,
            PlanHash, MarkerHash, PublishedAtUtc
        )
        SELECT
            @SourceProfileCode, N'DIRECT_REALTIME_APPLY', @MappingFingerprint,
            N'PRODUCTION', @SourceDatabaseGuid, @SourceChangeTrackingVersion,
            @CycleId, @PlanHash, @MarkerHash, @PublishedAtUtc
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint WITH (UPDLOCK, HOLDLOCK)
            WHERE SourceProfileCode = @SourceProfileCode
              AND Mode = N'DIRECT_REALTIME_APPLY'
              AND MappingFingerprint = @MappingFingerprint
              AND EnvironmentId = N'PRODUCTION'
        );
        """;

    public const string RollbackExactCanaryInsert = """
        IF @DownstreamReferenceCount <> 0
            THROW 527534, 'RT03_ROLLBACK_DOWNSTREAM_USE', 1;

        DELETE FROM dbo.App_HocVien
        WHERE HocVienId = @ExactInsertedHocVienId
          AND SourceProfileCode = @SourceProfileCode
          AND SourceMaDK = @SourceMaDK
          AND V2RowHash = @ExpectedCurrentSourceOwnedHash
          AND IsDeleted = 0
          AND CreatedBy = N'Rt03DirectRealtimeCanary';

        IF @@ROWCOUNT <> 1
            THROW 527535, 'RT03_ROLLBACK_EXACT_INSERT_NOT_FOUND', 1;
        """;

    public const string RollbackExactHoTen = """
        UPDATE dbo.App_HocVien
        SET HoTen = @BeforeHoTen,
            V2RowHash = @BeforeV2RowHash
        WHERE HocVienId = @TargetHocVienId
          AND SourceProfileCode = @SourceProfileCode
          AND SourceMaDK = @SourceMaDK
          AND V2RowHash = @ExpectedCurrentSourceOwnedHash
          AND IsDeleted = 0;

        IF @@ROWCOUNT <> 1
            THROW 527536, 'RT03_ROLLBACK_EXACT_UPDATE_NOT_FOUND', 1;
        """;

    public const string RollbackExactManualReview = """
        DELETE FROM dbo.App_QlhvDirectRealtimeManualReview
        WHERE CycleId = @CycleId
          AND PlanHash = @PlanHash
          AND CandidateId = @CandidateId
          AND IdentityHmac = @IdentityHmac
          AND TargetMutated = 0;
        """;

    public static IReadOnlyList<string> ApplyCommands { get; } =
    [
        RevalidateTargetIdentity,
        AcquireProductionProfileLock,
        RejectActiveAutoSync,
        RecheckExactLearner,
        RecheckExactCourse,
        RecheckSameMaKhoaCourses,
        ResolveLearnerCourse,
        InsertProductionCourse,
        UpdateProductionCourse,
        InsertExactLearner,
        InsertProductionLearner,
        UpdateExactHoTen,
        InsertManualReview,
        InsertApplyMarker,
        PublishCheckpointAfterVerifiedCommit,
    ];

    public static IReadOnlyList<string> RollbackCommands { get; } =
    [
        RollbackExactCanaryInsert,
        RollbackExactHoTen,
        RollbackExactManualReview,
    ];
}

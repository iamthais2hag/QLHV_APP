using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Assignments;
using QLHV.Shared.Paging;

namespace QLHV.Infrastructure.Assignments;

public sealed partial class SqlAssignmentRepository
{
    public async Task<DossierReceiverItem> CreateDossierReceiverAsync(
        DossierReceiverWrite write,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var id = await connection.ExecuteScalarAsync<long>(Command("""
                INSERT dbo.App_GiaoVien_hs
                    (MaGiaoVienHs,HoTen,HoTenSearch,NgaySinh,SoCCCD,TrangThai,GhiChu,
                     IsDeleted,CreatedAt,CreatedBy)
                OUTPUT INSERTED.GiaoVienHsId
                VALUES (@Code,@FullName,@FullNameSearch,@DateOfBirth,@CitizenId,@Status,@Note,
                        0,SYSUTCDATETIME(),@Actor);
                """, write, cancellationToken, transaction));
            await WriteAuditAsync(connection, transaction, "GIAO_VIEN_HO_SO", "CREATE",
                "App_GiaoVien_hs", id, write.Code, write.Actor, write.Reason, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetDossierReceiverAsync(connection, id, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AssignmentDomainException("CONFLICT", "Mã giáo viên hồ sơ hoặc CCCD đã tồn tại.", 409);
        }
    }

    public async Task<DossierReceiverItem> UpdateDossierReceiverAsync(
        long id,
        DossierReceiverWrite write,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var affected = await connection.ExecuteAsync(Command("""
                UPDATE dbo.App_GiaoVien_hs
                SET MaGiaoVienHs=@Code,HoTen=@FullName,HoTenSearch=@FullNameSearch,
                    NgaySinh=@DateOfBirth,SoCCCD=@CitizenId,TrangThai=@Status,GhiChu=@Note,
                    UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@Actor
                WHERE GiaoVienHsId=@Id AND IsDeleted=0 AND RowVersion=@ExpectedRowVersion;
                """, new
            {
                Id = id,
                write.Code,
                write.FullName,
                write.FullNameSearch,
                DateOfBirth = write.DateOfBirth?.ToDateTime(TimeOnly.MinValue),
                write.CitizenId,
                write.Status,
                write.Note,
                write.Actor,
                ExpectedRowVersion = expectedRowVersion,
            }, cancellationToken, transaction));
            AssertExactlyOne(affected, "Giáo viên hồ sơ đã thay đổi; hãy tải lại dữ liệu.");
            await WriteAuditAsync(connection, transaction, "GIAO_VIEN_HO_SO", "UPDATE",
                "App_GiaoVien_hs", id, write.Code, write.Actor, write.Reason, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetDossierReceiverAsync(connection, id, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AssignmentDomainException("CONFLICT", "Mã giáo viên hồ sơ hoặc CCCD đã tồn tại.", 409);
        }
    }

    public async Task<DossierReceiverItem> InactivateDossierReceiverAsync(
        long id,
        string actor,
        string reason,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(Command("""
            UPDATE dbo.App_GiaoVien_hs
            SET TrangThai='INACTIVE',UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@Actor
            WHERE GiaoVienHsId=@Id AND IsDeleted=0 AND RowVersion=@ExpectedRowVersion;
            """, new { Id = id, Actor = actor, ExpectedRowVersion = expectedRowVersion }, cancellationToken, transaction));
        AssertExactlyOne(affected, "Giáo viên hồ sơ đã thay đổi; hãy tải lại dữ liệu.");
        await WriteAuditAsync(connection, transaction, "GIAO_VIEN_HO_SO", "INACTIVE",
            "App_GiaoVien_hs", id, null, actor, reason, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetDossierReceiverAsync(connection, id, cancellationToken);
    }

    public async Task SoftDeleteDossierReceiverAsync(
        long id,
        string actor,
        string reason,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(Command("""
            UPDATE dbo.App_GiaoVien_hs
            SET IsDeleted=1,TrangThai='INACTIVE',DeletedAt=SYSUTCDATETIME(),DeletedBy=@Actor,
                DeleteReason=@Reason,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@Actor
            WHERE GiaoVienHsId=@Id AND IsDeleted=0 AND RowVersion=@ExpectedRowVersion;
            """, new { Id = id, Actor = actor, Reason = reason, ExpectedRowVersion = expectedRowVersion }, cancellationToken, transaction));
        AssertExactlyOne(affected, "Giáo viên hồ sơ đã thay đổi; hãy tải lại dữ liệu.");
        await WriteAuditAsync(connection, transaction, "GIAO_VIEN_HO_SO", "SOFT_DELETE",
            "App_GiaoVien_hs", id, null, actor, reason, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<DossierReceiverHistoryResult> GetDossierReceiverHistoryAsync(
        long id,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var referenceCount = await connection.ExecuteScalarAsync<int>(Command("""
            SELECT CONVERT(int,COUNT_BIG(1)) FROM dbo.App_HocVien_PhanCong
            WHERE GiaoVienHoSoId=@Id;
            """, new { Id = id }, cancellationToken));
        var rows = await connection.QueryAsync<AuditRow>(Command("""
            SELECT CreatedAt AS OccurredAtUtc,COALESCE(CreatedBy,N'') AS Actor,
                   HanhDong AS Action,
                   COALESCE(JSON_VALUE(DuLieuSau,'$.reason'),N'') AS Reason
            FROM dbo.App_AuditLog
            WHERE EntityType=N'App_GiaoVien_hs' AND EntityId=CONVERT(nvarchar(100),@Id)
            ORDER BY CreatedAt DESC,AuditLogId DESC;
            """, new { Id = id }, cancellationToken));
        return new DossierReceiverHistoryResult(
            referenceCount,
            rows.Select(row => new AuditHistoryItem(
                row.OccurredAtUtc,row.Actor,row.Action,row.Reason)).ToArray());
    }

    public async Task<TrainingGroupItem> CreateGroupAsync(
        long courseId,
        TrainingGroupWrite write,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,cancellationToken);
        try
        {
            await LoadCourseIdentityAsync(connection,courseId,null,cancellationToken,
                transaction,lockForUpdate:true,activeRequired:true);
            await AssertGroupReferencesAsync(connection, transaction, courseId, write, cancellationToken);
            var id = await connection.ExecuteScalarAsync<long>(Command("""
                INSERT dbo.App_KhoaHoc_NhomDaoTao
                    (KhoaHocId,MaNhom,TenNhom,ThuTu,GiaoVienDungLopId,XeTapId,XeBaiSo10Id,
                     TrangThai,GhiChu,CreatedAt,CreatedBy)
                OUTPUT INSERTED.NhomDaoTaoId
                VALUES(@CourseId,@Code,@Name,@DisplayOrder,@DefaultTeacherId,@DefaultTrainingVehicleId,
                       @DefaultFigure10VehicleId,'ACTIVE',@Note,SYSUTCDATETIME(),@Actor);
                """, new
            {
                CourseId = courseId,
                write.Code,
                write.Name,
                write.DisplayOrder,
                write.DefaultTeacherId,
                write.DefaultTrainingVehicleId,
                write.DefaultFigure10VehicleId,
                write.Note,
                write.Actor,
            }, cancellationToken, transaction));
            await WriteAuditAsync(connection, transaction, "NHOM_DAO_TAO", "CREATE",
                "App_KhoaHoc_NhomDaoTao", id, write.Code, write.Actor, write.Reason, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetGroupAsync(connection, courseId, id, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AssignmentDomainException("CONFLICT", "Mã nhóm đã tồn tại trong khóa học.", 409);
        }
    }

    public async Task<TrainingGroupItem> UpdateGroupAsync(
        long courseId,
        long groupId,
        TrainingGroupWrite write,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,cancellationToken);
        try
        {
            await LoadCourseIdentityAsync(connection,courseId,null,cancellationToken,
                transaction,lockForUpdate:true,activeRequired:true);
            var existing=await LoadGroupDefaultsAsync(connection,groupId,courseId,activeRequired:false,
                transaction,cancellationToken,lockForUpdate:true);
            if(!existing.RowVersion.SequenceEqual(expectedRowVersion))
                throw new AssignmentDomainException("CONFLICT","Nhóm đã thay đổi; hãy tải lại dữ liệu.",409);
            if(existing.GiaoVienDungLopId!=write.DefaultTeacherId ||
               existing.XeTapId!=write.DefaultTrainingVehicleId ||
               existing.XeBaiSo10Id!=write.DefaultFigure10VehicleId)
                throw new AssignmentDomainException("CONFLICT",
                    "Mặc định nhóm chỉ được thay qua preview/confirm với chế độ propagation rõ ràng.",409);
            await AssertGroupReferencesAsync(connection, transaction, courseId, write, cancellationToken,existing);
            var affected = await connection.ExecuteAsync(Command("""
                UPDATE dbo.App_KhoaHoc_NhomDaoTao
                SET MaNhom=@Code,TenNhom=@Name,ThuTu=@DisplayOrder,
                    GiaoVienDungLopId=@DefaultTeacherId,XeTapId=@DefaultTrainingVehicleId,
                    XeBaiSo10Id=@DefaultFigure10VehicleId,GhiChu=@Note,
                    UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@Actor
                WHERE NhomDaoTaoId=@GroupId AND KhoaHocId=@CourseId
                  AND RowVersion=@ExpectedRowVersion;
                """, new
            {
                GroupId = groupId,
                CourseId = courseId,
                write.Code,
                write.Name,
                write.DisplayOrder,
                write.DefaultTeacherId,
                write.DefaultTrainingVehicleId,
                write.DefaultFigure10VehicleId,
                write.Note,
                write.Actor,
                ExpectedRowVersion = expectedRowVersion,
            }, cancellationToken, transaction));
            AssertExactlyOne(affected, "Nhóm đã thay đổi; hãy tải lại dữ liệu.");
            await WriteAuditAsync(connection, transaction, "NHOM_DAO_TAO", "UPDATE",
                "App_KhoaHoc_NhomDaoTao", groupId, write.Code, write.Actor, write.Reason, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetGroupAsync(connection, courseId, groupId, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new AssignmentDomainException("CONFLICT", "Mã nhóm đã tồn tại trong khóa học.", 409);
        }
    }

    public async Task<TrainingGroupItem> InactivateGroupAsync(
        long courseId,
        long groupId,
        string actor,
        string reason,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(Command("""
            UPDATE dbo.App_KhoaHoc_NhomDaoTao
            SET TrangThai='INACTIVE',UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@Actor
            WHERE NhomDaoTaoId=@GroupId AND KhoaHocId=@CourseId AND RowVersion=@ExpectedRowVersion;
            """, new { GroupId = groupId, CourseId = courseId, Actor = actor, ExpectedRowVersion = expectedRowVersion },
            cancellationToken, transaction));
        AssertExactlyOne(affected, "Nhóm đã thay đổi; hãy tải lại dữ liệu.");
        await WriteAuditAsync(connection, transaction, "NHOM_DAO_TAO", "INACTIVE",
            "App_KhoaHoc_NhomDaoTao", groupId, null, actor, reason, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetGroupAsync(connection, courseId, groupId, cancellationToken);
    }

    public async Task<CourseAssignmentDetail> GetCourseDetailAsync(
        long courseId,
        CourseDetailRequest request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var keyword = AssignmentRules.NormalizeOptional(request.StudentKeyword, 255);
        await using var connection = await OpenAsync(cancellationToken);
        var courseRow = await connection.QuerySingleOrDefaultAsync<CourseRow>(Command("""
            SELECT k.KhoaHocId,COALESCE(k.SourceProfileCode,N'') AS SourceProfileCode,k.MaKhoa,k.TenKhoa,
                   COALESCE(k.HangDaoTao,k.HangGPLX) AS HangDaoTao,
                   CONVERT(nvarchar(50),k.HinhThucDaoTao) AS LoaiDaoTao,k.NgayKhaiGiang,k.NgayBeGiang,
                   k.SoQuyetDinhKhaiGiang AS SoQuyetDinh,COALESCE(k.TrangThai,N'') AS TrangThai,
                   CONVERT(bit,CASE WHEN COALESCE(k.TrangThaiNguon,1)=1 THEN 1 ELSE 0 END) AS IsActive,
                   0 LearnerCount,0 UnassignedCount,0 ManualReviewCount,k.RowVersion
            FROM dbo.App_KhoaHoc k WHERE k.KhoaHocId=@CourseId AND k.IsDeleted=0;
            """, new { CourseId = courseId }, cancellationToken));
        if (courseRow is null || string.IsNullOrWhiteSpace(courseRow.SourceProfileCode))
        {
            throw new AssignmentDomainException("NOT_FOUND", "Không tìm thấy khóa học có định danh nguồn hợp lệ.", 404);
        }

        if (!await HasAssignmentReadSchemaAsync(connection, cancellationToken))
        {
            return await GetCourseDetailWithoutAssignmentSchemaAsync(
                connection,
                courseRow,
                keyword,
                request.GroupId,
                page,
                pageSize,
                cancellationToken);
        }

        var groups = await LoadGroupsAsync(connection, courseId, cancellationToken);
        var lookups = await LoadLookupsAsync(connection, courseRow.SourceProfileCode, cancellationToken);
        var filterArgs = new
        {
            CourseId = courseId,
            courseRow.MaKhoa,
            courseRow.SourceProfileCode,
            Keyword = keyword,
            request.GroupId,
            request.UnassignedOnly,
            Offset = (page - 1) * pageSize,
            PageSize = pageSize,
        };
        const string studentWhere = """
            h.IsDeleted=0 AND h.MaKhoa=@MaKhoa AND h.SourceProfileCode=@SourceProfileCode
            AND (@Keyword IS NULL OR h.MaDK LIKE N'%' + @Keyword + N'%' OR h.HoTen LIKE N'%' + @Keyword + N'%')
            AND (@GroupId IS NULL OR pc.NhomDaoTaoId=@GroupId)
            AND (@UnassignedOnly=0 OR pc.PhanCongId IS NULL)
            """;
        var total = await connection.ExecuteScalarAsync<int>(Command(
            $"""
                SELECT CONVERT(int,COUNT_BIG(1)) FROM dbo.App_HocVien h
                LEFT JOIN dbo.App_HocVien_PhanCong pc ON pc.HocVienId=h.HocVienId AND pc.IsCurrent=1
                WHERE {studentWhere};
                """,
            filterArgs,
            cancellationToken));
        var studentRows = total == 0
            ? []
            : (await connection.QueryAsync<StudentRow>(Command($"""
                SELECT h.HocVienId,h.MaDK AS MaDangKy,COALESCE(h.HoTen,N'') AS HoTen,h.NgaySinh,
                       h.MaKhoa,h.SourceProfileCode,h.HangGPLXHoc AS HangHoc,
                       pc.PhanCongId,pc.NhomDaoTaoId,n.MaNhom AS GroupCode,
                       CONVERT(bit,CASE WHEN n.NhomDaoTaoId IS NOT NULL AND n.TrangThai='ACTIVE'
                           THEN 1 ELSE 0 END) AS GroupActive,
                       pc.GiaoVienHoSoId,gh.MaGiaoVienHs AS DossierCode,gh.HoTen AS DossierLabel,
                       CONVERT(bit,CASE WHEN gh.GiaoVienHsId IS NOT NULL AND gh.TrangThai='ACTIVE' AND gh.IsDeleted=0 THEN 1 ELSE 0 END) AS DossierActive,
                       pc.GiaoVienDungLopId,gv.MaGV AS TeacherCode,gv.HoTen AS TeacherLabel,
                       CONVERT(bit,CASE WHEN gv.GiaoVienId IS NOT NULL AND COALESCE(gv.TrangThaiNguon,1)=1 AND gv.IsDeleted=0 THEN 1 ELSE 0 END) AS TeacherActive,
                       pc.XeTapId,xt.BienSoXe AS VehicleCode,xt.BienSoXe AS VehicleLabel,
                       CONVERT(bit,CASE WHEN xt.XeTapId IS NOT NULL AND xt.SourceLifecycle='ACTIVE'
                           AND COALESCE(xt.SourceTrangThai,1)=1 AND xt.IsDeleted=0 THEN 1 ELSE 0 END) AS VehicleActive,
                       pc.XeBaiSo10Id,x10.BienSoXe AS Figure10Code,x10.BienSoXe AS Figure10Label,
                       CONVERT(bit,CASE WHEN x10.XeTapId IS NOT NULL AND x10.SourceLifecycle='ACTIVE'
                           AND COALESCE(x10.SourceTrangThai,1)=1 AND x10.IsDeleted=0 THEN 1 ELSE 0 END) AS Figure10Active,
                       COALESCE(pc.IsGiaoVienDungLopOverride,0) AS OverrideClassTeacher,
                       COALESCE(pc.IsXeTapOverride,0) AS OverrideTrainingVehicle,
                       COALESCE(pc.IsXeBaiSo10Override,0) AS OverrideFigure10Vehicle,
                       pc.RowVersion AS AssignmentRowVersion
                FROM dbo.App_HocVien h
                LEFT JOIN dbo.App_HocVien_PhanCong pc ON pc.HocVienId=h.HocVienId AND pc.IsCurrent=1
                LEFT JOIN dbo.App_KhoaHoc_NhomDaoTao n ON n.NhomDaoTaoId=pc.NhomDaoTaoId
                LEFT JOIN dbo.App_GiaoVien_hs gh ON gh.GiaoVienHsId=pc.GiaoVienHoSoId
                LEFT JOIN dbo.App_GiaoVien gv ON gv.GiaoVienId=pc.GiaoVienDungLopId
                LEFT JOIN dbo.App_XeTap xt ON xt.XeTapId=pc.XeTapId
                LEFT JOIN dbo.App_XeTap x10 ON x10.XeTapId=pc.XeBaiSo10Id
                WHERE {studentWhere}
                ORDER BY h.HoTen,h.MaDK,h.HocVienId
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """, filterArgs, cancellationToken))).ToArray();

        var summary = await connection.QuerySingleAsync<CourseSummaryRow>(Command("""
            SELECT CONVERT(int,COUNT_BIG(1)) AS LearnerCount,
                   CONVERT(int,COUNT_BIG(CASE WHEN pc.PhanCongId IS NOT NULL THEN 1 END)) AS AssignedCount,
                   CONVERT(int,COUNT_BIG(CASE WHEN pc.PhanCongId IS NULL THEN 1 END)) AS UnassignedCount,
                   CONVERT(int,COUNT_BIG(CASE WHEN
                        (pc.NhomDaoTaoId IS NOT NULL AND (n.NhomDaoTaoId IS NULL OR n.TrangThai<>'ACTIVE')) OR
                        (pc.GiaoVienHoSoId IS NOT NULL AND (gh.GiaoVienHsId IS NULL OR gh.TrangThai<>'ACTIVE' OR gh.IsDeleted=1)) OR
                        (pc.GiaoVienDungLopId IS NOT NULL AND (gv.GiaoVienId IS NULL OR COALESCE(gv.TrangThaiNguon,1)=0 OR gv.IsDeleted=1)) OR
                        (pc.XeTapId IS NOT NULL AND (xt.XeTapId IS NULL OR xt.SourceLifecycle<>'ACTIVE' OR COALESCE(xt.SourceTrangThai,1)<>1 OR xt.IsDeleted=1)) OR
                        (pc.XeBaiSo10Id IS NOT NULL AND (x10.XeTapId IS NULL OR x10.SourceLifecycle<>'ACTIVE' OR COALESCE(x10.SourceTrangThai,1)<>1 OR x10.IsDeleted=1))
                       THEN 1 END)) AS ManualReviewCount
            FROM dbo.App_HocVien h
            LEFT JOIN dbo.App_HocVien_PhanCong pc ON pc.HocVienId=h.HocVienId AND pc.IsCurrent=1
            LEFT JOIN dbo.App_KhoaHoc_NhomDaoTao n ON n.NhomDaoTaoId=pc.NhomDaoTaoId
            LEFT JOIN dbo.App_GiaoVien_hs gh ON gh.GiaoVienHsId=pc.GiaoVienHoSoId
            LEFT JOIN dbo.App_GiaoVien gv ON gv.GiaoVienId=pc.GiaoVienDungLopId
            LEFT JOIN dbo.App_XeTap xt ON xt.XeTapId=pc.XeTapId
            LEFT JOIN dbo.App_XeTap x10 ON x10.XeTapId=pc.XeBaiSo10Id
            WHERE h.IsDeleted=0 AND h.MaKhoa=@MaKhoa AND h.SourceProfileCode=@SourceProfileCode;
            """, filterArgs, cancellationToken));

        var course = ToCourse(courseRow) with
        {
            LearnerCount = summary.LearnerCount,
            UnassignedCount = summary.UnassignedCount,
            ManualReviewCount = summary.ManualReviewCount,
        };
        return new CourseAssignmentDetail(
            course,
            new PagedResult<StudentAssignmentItem>
            {
                Items = studentRows.Select(ToStudent).ToArray(),
                Page = page,
                PageSize = pageSize,
                TotalItems = total,
            },
            groups,
            lookups,
            new CourseAssignmentSummary(summary.LearnerCount,summary.AssignedCount,
                summary.UnassignedCount,summary.ManualReviewCount));
    }

    private async Task<CourseAssignmentDetail> GetCourseDetailWithoutAssignmentSchemaAsync(
        SqlConnection connection,
        CourseRow courseRow,
        string? keyword,
        long? groupId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var args = new
        {
            courseRow.MaKhoa,
            courseRow.SourceProfileCode,
            Keyword = keyword,
            GroupId = groupId,
            Offset = (page - 1) * pageSize,
            PageSize = pageSize,
        };
        const string where = """
            h.IsDeleted=0 AND h.MaKhoa=@MaKhoa AND h.SourceProfileCode=@SourceProfileCode
            AND (@Keyword IS NULL OR h.MaDK LIKE N'%' + @Keyword + N'%' OR h.HoTen LIKE N'%' + @Keyword + N'%')
            AND @GroupId IS NULL
            """;
        var total = await connection.ExecuteScalarAsync<int>(Command(
            $"SELECT CONVERT(int,COUNT_BIG(1)) FROM dbo.App_HocVien h WHERE {where};",
            args,
            cancellationToken));
        var studentRows = total == 0
            ? []
            : (await connection.QueryAsync<StudentRow>(Command($"""
                SELECT h.HocVienId,h.MaDK AS MaDangKy,COALESCE(h.HoTen,N'') AS HoTen,h.NgaySinh,
                       h.MaKhoa,h.SourceProfileCode,h.HangGPLXHoc AS HangHoc
                FROM dbo.App_HocVien h
                WHERE {where}
                ORDER BY h.HoTen,h.MaDK,h.HocVienId
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """, args, cancellationToken))).ToArray();
        var learnerCount = await connection.ExecuteScalarAsync<int>(Command("""
            SELECT CONVERT(int,COUNT_BIG(1))
            FROM dbo.App_HocVien h
            WHERE h.IsDeleted=0 AND h.MaKhoa=@MaKhoa AND h.SourceProfileCode=@SourceProfileCode;
            """, args, cancellationToken));
        var course = ToCourse(courseRow) with
        {
            LearnerCount = learnerCount,
            UnassignedCount = learnerCount,
            ManualReviewCount = 0,
        };
        return new CourseAssignmentDetail(
            course,
            new PagedResult<StudentAssignmentItem>
            {
                Items = studentRows.Select(ToStudent).ToArray(),
                Page = page,
                PageSize = pageSize,
                TotalItems = total,
            },
            [],
            await LoadSourceOwnedLookupsAsync(
                connection,
                courseRow.SourceProfileCode,
                cancellationToken),
            new CourseAssignmentSummary(learnerCount, 0, learnerCount, 0));
    }

    private async Task<AssignmentLookups> LoadSourceOwnedLookupsAsync(
        SqlConnection connection,
        string sourceProfileCode,
        CancellationToken cancellationToken)
    {
        var teachers = await connection.QueryAsync<LookupRow>(Command("""
            SELECT GiaoVienId AS Id,MaGV AS Code,HoTen AS Label,
                   CONVERT(bit,1) AS IsActive,
                   CONVERT(bit,CASE WHEN COALESCE(LastSyncStatus,N'') IN (N'MANUAL_REVIEW',N'CONFLICT') THEN 1 ELSE 0 END) AS IsManualReview,
                   SourceProfileCode
            FROM dbo.App_GiaoVien
            WHERE IsDeleted=0 AND COALESCE(TrangThaiNguon,1)=1 AND SourceProfileCode=@SourceProfileCode
            ORDER BY HoTen,MaGV;
            """, new { SourceProfileCode = sourceProfileCode }, cancellationToken));
        var vehicles = await connection.QueryAsync<LookupRow>(Command("""
            SELECT XeTapId AS Id,COALESCE(SourceBienSoXe,BienSoXe) AS Code,BienSoXe AS Label,
                   CONVERT(bit,1) AS IsActive,
                   CONVERT(bit,CASE WHEN ManualReviewCode IS NOT NULL THEN 1 ELSE 0 END) AS IsManualReview,
                   SourceProfileCode
            FROM dbo.App_XeTap
            WHERE IsDeleted=0 AND SourceLifecycle='ACTIVE' AND COALESCE(SourceTrangThai,1)=1
              AND SourceProfileCode=@SourceProfileCode
            ORDER BY BienSoXe,XeTapId;
            """, new { SourceProfileCode = sourceProfileCode }, cancellationToken));
        return new AssignmentLookups(
            [],
            teachers.Select(ToLookup).ToArray(),
            vehicles.Select(ToLookup).ToArray());
    }

    private async Task<DossierReceiverItem> GetDossierReceiverAsync(
        SqlConnection connection,
        long id,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<DossierReceiverRow>(Command("""
            SELECT gh.GiaoVienHsId,gh.MaGiaoVienHs,gh.HoTen,gh.NgaySinh,gh.SoCCCD,
                   gh.TrangThai,gh.IsDeleted,
                   CONVERT(int,(SELECT COUNT_BIG(1) FROM dbo.App_HocVien_PhanCong pc
                                WHERE pc.GiaoVienHoSoId=gh.GiaoVienHsId)) AS ReferenceCount,
                   gh.RowVersion,COALESCE(gh.UpdatedAt,gh.CreatedAt) AS UpdatedAtUtc,
                   COALESCE(gh.UpdatedBy,gh.CreatedBy) AS UpdatedBy,gh.GhiChu
            FROM dbo.App_GiaoVien_hs gh WHERE gh.GiaoVienHsId=@Id;
            """, new { Id = id }, cancellationToken));
        return row is null
            ? throw new AssignmentDomainException("NOT_FOUND", "Không tìm thấy giáo viên hồ sơ.", 404)
            : ToDossierReceiver(row);
    }

    private async Task AssertGroupReferencesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long courseId,
        TrainingGroupWrite write,
        CancellationToken cancellationToken,
        GroupDefaultsRow? existing = null)
    {
        var valid = await connection.ExecuteScalarAsync<int>(Command("""
            SELECT CASE WHEN EXISTS(
                SELECT 1 FROM dbo.App_KhoaHoc k WITH (UPDLOCK,HOLDLOCK)
                WHERE k.KhoaHocId=@CourseId AND k.IsDeleted=0 AND COALESCE(k.TrangThaiNguon,1)=1
                  AND (@TeacherId IS NULL OR @RequireTeacherActive=0 OR EXISTS(
                       SELECT 1 FROM dbo.App_GiaoVien g WITH (UPDLOCK,HOLDLOCK) WHERE g.GiaoVienId=@TeacherId
                        AND g.IsDeleted=0 AND COALESCE(g.TrangThaiNguon,1)=1
                        AND g.SourceProfileCode=k.SourceProfileCode))
                  AND (@VehicleId IS NULL OR @RequireVehicleActive=0 OR EXISTS(
                       SELECT 1 FROM dbo.App_XeTap x WITH (UPDLOCK,HOLDLOCK) WHERE x.XeTapId=@VehicleId
                        AND x.IsDeleted=0 AND x.SourceLifecycle='ACTIVE'
                        AND COALESCE(x.SourceTrangThai,1)=1
                        AND x.SourceProfileCode=k.SourceProfileCode))
                  AND (@Figure10Id IS NULL OR @RequireFigure10Active=0 OR EXISTS(
                       SELECT 1 FROM dbo.App_XeTap x WITH (UPDLOCK,HOLDLOCK) WHERE x.XeTapId=@Figure10Id
                        AND x.IsDeleted=0 AND x.SourceLifecycle='ACTIVE'
                        AND COALESCE(x.SourceTrangThai,1)=1
                        AND x.SourceProfileCode=k.SourceProfileCode))
            ) THEN 1 ELSE 0 END;
            """, new
        {
            CourseId = courseId,
            TeacherId = write.DefaultTeacherId,
            VehicleId = write.DefaultTrainingVehicleId,
            Figure10Id = write.DefaultFigure10VehicleId,
            RequireTeacherActive=existing?.GiaoVienDungLopId!=write.DefaultTeacherId,
            RequireVehicleActive=existing?.XeTapId!=write.DefaultTrainingVehicleId,
            RequireFigure10Active=existing?.XeBaiSo10Id!=write.DefaultFigure10VehicleId,
        }, cancellationToken, transaction));
        if (valid != 1)
        {
            throw new AssignmentDomainException(
                "INACTIVE_REFERENCE",
                "Khóa học hoặc mặc định giáo viên/xe không active hay không cùng source profile.",
                409);
        }
    }

    private async Task<TrainingGroupItem> GetGroupAsync(
        SqlConnection connection,
        long courseId,
        long groupId,
        CancellationToken cancellationToken)
    {
        var rows = await LoadGroupsAsync(connection, courseId, cancellationToken);
        return rows.SingleOrDefault(row => row.GroupId == groupId)
            ?? throw new AssignmentDomainException("NOT_FOUND", "Không tìm thấy nhóm đào tạo.", 404);
    }

    private async Task<IReadOnlyList<TrainingGroupItem>> LoadGroupsAsync(
        SqlConnection connection,
        long courseId,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<GroupRow>(Command("""
            SELECT n.NhomDaoTaoId AS GroupId,n.MaNhom,n.TenNhom,n.ThuTu,n.TrangThai,
                   CONVERT(bit,CASE WHEN n.TrangThai='ACTIVE' THEN 1 ELSE 0 END) AS IsActive,
                   n.GiaoVienDungLopId,gv.MaGV AS TeacherCode,gv.HoTen AS TeacherLabel,
                   CONVERT(bit,CASE WHEN gv.GiaoVienId IS NOT NULL AND gv.IsDeleted=0 AND COALESCE(gv.TrangThaiNguon,1)=1 THEN 1 ELSE 0 END) AS TeacherActive,
                   n.XeTapId,xt.BienSoXe AS VehicleCode,xt.BienSoXe AS VehicleLabel,
                   CONVERT(bit,CASE WHEN xt.XeTapId IS NOT NULL AND xt.IsDeleted=0 AND xt.SourceLifecycle='ACTIVE'
                       AND COALESCE(xt.SourceTrangThai,1)=1 THEN 1 ELSE 0 END) AS VehicleActive,
                   n.XeBaiSo10Id,x10.BienSoXe AS Figure10Code,x10.BienSoXe AS Figure10Label,
                   CONVERT(bit,CASE WHEN x10.XeTapId IS NOT NULL AND x10.IsDeleted=0 AND x10.SourceLifecycle='ACTIVE'
                       AND COALESCE(x10.SourceTrangThai,1)=1 THEN 1 ELSE 0 END) AS Figure10Active,
                   CONVERT(int,(SELECT COUNT_BIG(1) FROM dbo.App_HocVien_PhanCong pc
                                WHERE pc.IsCurrent=1 AND pc.NhomDaoTaoId=n.NhomDaoTaoId)) AS StudentCount,
                   n.RowVersion
            FROM dbo.App_KhoaHoc_NhomDaoTao n
            LEFT JOIN dbo.App_GiaoVien gv ON gv.GiaoVienId=n.GiaoVienDungLopId
            LEFT JOIN dbo.App_XeTap xt ON xt.XeTapId=n.XeTapId
            LEFT JOIN dbo.App_XeTap x10 ON x10.XeTapId=n.XeBaiSo10Id
            WHERE n.KhoaHocId=@CourseId
            ORDER BY n.ThuTu,n.MaNhom,n.NhomDaoTaoId;
            """, new { CourseId = courseId }, cancellationToken));
        return rows.Select(ToGroup).ToArray();
    }

    private async Task<AssignmentLookups> LoadLookupsAsync(
        SqlConnection connection,
        string sourceProfileCode,
        CancellationToken cancellationToken)
    {
        var receivers = await connection.QueryAsync<LookupRow>(Command("""
            SELECT GiaoVienHsId AS Id,MaGiaoVienHs AS Code,HoTen AS Label,
                   CONVERT(bit,CASE WHEN TrangThai='ACTIVE' AND IsDeleted=0 THEN 1 ELSE 0 END) AS IsActive,
                   CONVERT(bit,0) AS IsManualReview,CAST(NULL AS nvarchar(50)) AS SourceProfileCode
            FROM dbo.App_GiaoVien_hs WHERE TrangThai='ACTIVE' AND IsDeleted=0
            ORDER BY HoTen,MaGiaoVienHs;
            """, null, cancellationToken));
        var teachers = await connection.QueryAsync<LookupRow>(Command("""
            SELECT GiaoVienId AS Id,MaGV AS Code,HoTen AS Label,
                   CONVERT(bit,1) AS IsActive,
                   CONVERT(bit,CASE WHEN COALESCE(LastSyncStatus,N'') IN (N'MANUAL_REVIEW',N'CONFLICT') THEN 1 ELSE 0 END) AS IsManualReview,
                   SourceProfileCode
            FROM dbo.App_GiaoVien
            WHERE IsDeleted=0 AND COALESCE(TrangThaiNguon,1)=1 AND SourceProfileCode=@SourceProfileCode
            ORDER BY HoTen,MaGV;
            """, new { SourceProfileCode = sourceProfileCode }, cancellationToken));
        var vehicles = await connection.QueryAsync<LookupRow>(Command("""
            SELECT XeTapId AS Id,COALESCE(SourceBienSoXe,BienSoXe) AS Code,BienSoXe AS Label,
                   CONVERT(bit,1) AS IsActive,
                   CONVERT(bit,CASE WHEN ManualReviewCode IS NOT NULL THEN 1 ELSE 0 END) AS IsManualReview,
                   SourceProfileCode
            FROM dbo.App_XeTap
            WHERE IsDeleted=0 AND SourceLifecycle='ACTIVE' AND COALESCE(SourceTrangThai,1)=1
              AND SourceProfileCode=@SourceProfileCode
            ORDER BY BienSoXe,XeTapId;
            """, new { SourceProfileCode = sourceProfileCode }, cancellationToken));
        return new AssignmentLookups(
            receivers.Select(ToLookup).ToArray(),
            teachers.Select(ToLookup).ToArray(),
            vehicles.Select(ToLookup).ToArray());
    }

    private async Task WriteAuditAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string feature,
        string action,
        string entityType,
        long entityId,
        string? entityKey,
        string actor,
        string reason,
        CancellationToken cancellationToken,
        DateTime? auditAtUtc = null)
    {
        await connection.ExecuteAsync(Command("""
            INSERT dbo.App_AuditLog
                (ChucNang,HanhDong,EntityType,EntityId,EntityKey,DuLieuTruoc,DuLieuSau,
                 KetQua,Loi,CreatedAt,CreatedBy,ClientIp,UserAgent)
            VALUES(@Feature,@Action,@EntityType,CONVERT(nvarchar(100),@EntityId),@EntityKey,
                   NULL,@AfterJson,N'SUCCESS',NULL,COALESCE(@AuditAtUtc,SYSUTCDATETIME()),@Actor,NULL,NULL);
            """, new
        {
            Feature = feature,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            EntityKey = entityKey,
            AfterJson = JsonSerializer.Serialize(new { reason }),
            Actor = actor,
            AuditAtUtc = auditAtUtc,
        }, cancellationToken, transaction));
    }

    private async Task WriteAssignmentAuditAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CourseIdentityRow course,
        AssignmentMutationTarget target,
        string operationId,
        string actor,
        string reason,
        DateTime auditAtUtc,
        CancellationToken cancellationToken)
    {
        var beforeJson=JsonSerializer.Serialize(new
        {
            courseId=course.KhoaHocId,
            sourceProfileCode=course.SourceProfileCode,
            operationId,
            hocVienId=target.HocVienId,
            assignment=target.Before,
        });
        var afterJson=JsonSerializer.Serialize(new
        {
            courseId=course.KhoaHocId,
            sourceProfileCode=course.SourceProfileCode,
            operationId,
            hocVienId=target.HocVienId,
            reason,
            assignment=target.After,
        });
        await connection.ExecuteAsync(Command("""
            INSERT dbo.App_AuditLog
                (ChucNang,HanhDong,EntityType,EntityId,EntityKey,DuLieuTruoc,DuLieuSau,
                 KetQua,Loi,CreatedAt,CreatedBy,ClientIp,UserAgent)
            VALUES(N'PHAN_CONG_HOC_VIEN',N'SNAPSHOT',N'App_HocVien_PhanCong',
                   CONVERT(nvarchar(100),@HocVienId),@OperationId,@BeforeJson,@AfterJson,
                   N'SUCCESS',NULL,@AuditAtUtc,@Actor,NULL,NULL);
            """,new
        {
            target.HocVienId,
            OperationId=operationId,
            BeforeJson=beforeJson,
            AfterJson=afterJson,
            Actor=actor,
            AuditAtUtc=auditAtUtc,
        },cancellationToken,transaction));
    }

    private static void AssertExactlyOne(int affected, string message)
    {
        if (affected != 1)
        {
            throw new AssignmentDomainException("CONFLICT", message, 409);
        }
    }

    private static TrainingGroupItem ToGroup(GroupRow row) => new(
        row.GroupId,row.MaNhom,row.TenNhom,row.ThuTu,row.TrangThai,row.IsActive,
        ToLookup(row.GiaoVienDungLopId,row.TeacherCode,row.TeacherLabel,row.TeacherActive),
        ToLookup(row.XeTapId,row.VehicleCode,row.VehicleLabel,row.VehicleActive),
        ToLookup(row.XeBaiSo10Id,row.Figure10Code,row.Figure10Label,row.Figure10Active),
        row.StudentCount,AssignmentRules.RowVersionToString(row.RowVersion));

    private static StudentAssignmentItem ToStudent(StudentRow row)
    {
        var warnings = new List<string>();
        if (row.NhomDaoTaoId.HasValue && !row.GroupActive) warnings.Add("NhÃ³m Ä‘Ã o táº¡o khÃ´ng cÃ²n active.");
        if (row.GiaoVienHoSoId.HasValue && !row.DossierActive) warnings.Add("Người nhận hồ sơ không còn active.");
        if (row.GiaoVienDungLopId.HasValue && !row.TeacherActive) warnings.Add("Giáo viên đứng lớp cần kiểm tra.");
        if (row.XeTapId.HasValue && !row.VehicleActive) warnings.Add("Xe tập lái cần kiểm tra.");
        if (row.XeBaiSo10Id.HasValue && !row.Figure10Active) warnings.Add("Xe bài số 10 cần kiểm tra.");
        var status = warnings.Count > 0 ? "MANUAL_REVIEW" : row.PhanCongId.HasValue ? "ASSIGNED" : "UNASSIGNED";
        return new StudentAssignmentItem(
            row.HocVienId,row.MaDangKy,row.HoTen,AssignmentRules.ToDateOnly(row.NgaySinh),
            row.MaKhoa,row.SourceProfileCode,row.HangHoc,row.NhomDaoTaoId,row.GroupCode,
            ToLookup(row.GiaoVienHoSoId,row.DossierCode,row.DossierLabel,row.DossierActive),
            ToLookup(row.GiaoVienDungLopId,row.TeacherCode,row.TeacherLabel,row.TeacherActive),
            ToLookup(row.XeTapId,row.VehicleCode,row.VehicleLabel,row.VehicleActive),
            ToLookup(row.XeBaiSo10Id,row.Figure10Code,row.Figure10Label,row.Figure10Active),
            row.OverrideClassTeacher,row.OverrideTrainingVehicle,row.OverrideFigure10Vehicle,
            row.AssignmentRowVersion is null ? null : AssignmentRules.RowVersionToString(row.AssignmentRowVersion),
            status,warnings);
    }

    private static LookupRef? ToLookup(long? id, string? code, string? label, bool active) =>
        id.HasValue
            ? new LookupRef(id.Value,code ?? string.Empty,label ?? code ?? string.Empty,active,!active)
            : null;

    private static LookupRef ToLookup(LookupRow row) =>
        new(row.Id,row.Code,row.Label,row.IsActive,row.IsManualReview,row.SourceProfileCode);

    private sealed class AuditRow
    {
        public DateTime OccurredAtUtc { get; init; }
        public string Actor { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
    }

    private sealed class LookupRow
    {
        public long Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public bool IsManualReview { get; init; }
        public string? SourceProfileCode { get; init; }
    }

    private sealed class GroupRow
    {
        public long GroupId { get; init; }
        public string MaNhom { get; init; } = string.Empty;
        public string TenNhom { get; init; } = string.Empty;
        public int ThuTu { get; init; }
        public string TrangThai { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public long? GiaoVienDungLopId { get; init; }
        public string? TeacherCode { get; init; }
        public string? TeacherLabel { get; init; }
        public bool TeacherActive { get; init; }
        public long? XeTapId { get; init; }
        public string? VehicleCode { get; init; }
        public string? VehicleLabel { get; init; }
        public bool VehicleActive { get; init; }
        public long? XeBaiSo10Id { get; init; }
        public string? Figure10Code { get; init; }
        public string? Figure10Label { get; init; }
        public bool Figure10Active { get; init; }
        public int StudentCount { get; init; }
        public byte[] RowVersion { get; init; } = [];
    }

    private sealed class StudentRow
    {
        public long HocVienId { get; init; }
        public string MaDangKy { get; init; } = string.Empty;
        public string HoTen { get; init; } = string.Empty;
        public DateTime? NgaySinh { get; init; }
        public string MaKhoa { get; init; } = string.Empty;
        public string SourceProfileCode { get; init; } = string.Empty;
        public string? HangHoc { get; init; }
        public long? PhanCongId { get; init; }
        public long? NhomDaoTaoId { get; init; }
        public string? GroupCode { get; init; }
        public bool GroupActive { get; init; }
        public long? GiaoVienHoSoId { get; init; }
        public string? DossierCode { get; init; }
        public string? DossierLabel { get; init; }
        public bool DossierActive { get; init; }
        public long? GiaoVienDungLopId { get; init; }
        public string? TeacherCode { get; init; }
        public string? TeacherLabel { get; init; }
        public bool TeacherActive { get; init; }
        public long? XeTapId { get; init; }
        public string? VehicleCode { get; init; }
        public string? VehicleLabel { get; init; }
        public bool VehicleActive { get; init; }
        public long? XeBaiSo10Id { get; init; }
        public string? Figure10Code { get; init; }
        public string? Figure10Label { get; init; }
        public bool Figure10Active { get; init; }
        public bool OverrideClassTeacher { get; init; }
        public bool OverrideTrainingVehicle { get; init; }
        public bool OverrideFigure10Vehicle { get; init; }
        public byte[]? AssignmentRowVersion { get; init; }
    }

    private sealed class CourseSummaryRow
    {
        public int LearnerCount { get; init; }
        public int AssignedCount { get; init; }
        public int UnassignedCount { get; init; }
        public int ManualReviewCount { get; init; }
    }
}

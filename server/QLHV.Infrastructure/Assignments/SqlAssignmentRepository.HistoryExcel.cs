using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Assignments;
using QLHV.Shared.Paging;

namespace QLHV.Infrastructure.Assignments;

public sealed partial class SqlAssignmentRepository
{
    public async Task<IReadOnlyList<AssignmentHistoryItem>> GetStudentHistoryAsync(
        long hocVienId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<HistoryRow>(Command("""
            SELECT pc.PhanCongId AS AssignmentId,pc.NgayHieuLuc AS EffectiveFromUtc,
                   pc.NgayHetHieuLuc AS EffectiveToUtc,pc.IsCurrent,pc.NguonGan AS Source,
                   COALESCE(pc.CreatedBy,N'') AS Actor,COALESCE(pc.GhiChu,N'') AS Reason,
                   pc.NhomDaoTaoId,n.MaNhom AS GroupCode,n.TenNhom AS GroupLabel,
                   CONVERT(bit,CASE WHEN n.NhomDaoTaoId IS NOT NULL AND n.TrangThai='ACTIVE' THEN 1 ELSE 0 END) GroupActive,
                   pc.GiaoVienHoSoId,gh.MaGiaoVienHs AS DossierCode,gh.HoTen AS DossierLabel,
                   CONVERT(bit,CASE WHEN gh.GiaoVienHsId IS NOT NULL AND gh.TrangThai='ACTIVE' AND gh.IsDeleted=0 THEN 1 ELSE 0 END) DossierActive,
                   pc.GiaoVienDungLopId,gv.MaGV AS TeacherCode,gv.HoTen AS TeacherLabel,
                   CONVERT(bit,CASE WHEN gv.GiaoVienId IS NOT NULL AND gv.IsDeleted=0 AND COALESCE(gv.TrangThaiNguon,1)=1 THEN 1 ELSE 0 END) TeacherActive,
                   pc.XeTapId,xt.BienSoXe AS VehicleCode,xt.BienSoXe AS VehicleLabel,
                   CONVERT(bit,CASE WHEN xt.XeTapId IS NOT NULL AND xt.IsDeleted=0 AND xt.SourceLifecycle='ACTIVE'
                       AND COALESCE(xt.SourceTrangThai,1)=1 THEN 1 ELSE 0 END) VehicleActive,
                   pc.XeBaiSo10Id,x10.BienSoXe AS Figure10Code,x10.BienSoXe AS Figure10Label,
                   CONVERT(bit,CASE WHEN x10.XeTapId IS NOT NULL AND x10.IsDeleted=0 AND x10.SourceLifecycle='ACTIVE'
                       AND COALESCE(x10.SourceTrangThai,1)=1 THEN 1 ELSE 0 END) Figure10Active,
                   pc.IsGiaoVienDungLopOverride AS OverrideClassTeacher,
                   pc.IsXeTapOverride AS OverrideTrainingVehicle,
                   pc.IsXeBaiSo10Override AS OverrideFigure10Vehicle
            FROM dbo.App_HocVien_PhanCong pc
            LEFT JOIN dbo.App_KhoaHoc_NhomDaoTao n ON n.NhomDaoTaoId=pc.NhomDaoTaoId
            LEFT JOIN dbo.App_GiaoVien_hs gh ON gh.GiaoVienHsId=pc.GiaoVienHoSoId
            LEFT JOIN dbo.App_GiaoVien gv ON gv.GiaoVienId=pc.GiaoVienDungLopId
            LEFT JOIN dbo.App_XeTap xt ON xt.XeTapId=pc.XeTapId
            LEFT JOIN dbo.App_XeTap x10 ON x10.XeTapId=pc.XeBaiSo10Id
            WHERE pc.HocVienId=@HocVienId
            ORDER BY pc.NgayHieuLuc DESC,pc.PhanCongId DESC;
            """,new { HocVienId=hocVienId },cancellationToken));
        return rows.Select(row => new AssignmentHistoryItem(
            row.AssignmentId,row.EffectiveFromUtc,row.EffectiveToUtc,row.IsCurrent,row.Source,
            row.Actor,row.Reason,
            ToLookup(row.NhomDaoTaoId,row.GroupCode,row.GroupLabel,row.GroupActive),
            ToLookup(row.GiaoVienHoSoId,row.DossierCode,row.DossierLabel,row.DossierActive),
            ToLookup(row.GiaoVienDungLopId,row.TeacherCode,row.TeacherLabel,row.TeacherActive),
            ToLookup(row.XeTapId,row.VehicleCode,row.VehicleLabel,row.VehicleActive),
            ToLookup(row.XeBaiSo10Id,row.Figure10Code,row.Figure10Label,row.Figure10Active),
            row.OverrideClassTeacher,row.OverrideTrainingVehicle,row.OverrideFigure10Vehicle)).ToArray();
    }

    public async Task<PagedResult<AuditHistoryItem>> GetCourseHistoryAsync(
        long courseId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page=Math.Max(1,page);
        pageSize=Math.Clamp(pageSize<=0 ? 50 : pageSize,1,200);
        await using var connection=await OpenAsync(cancellationToken);
        const string where="""
            (a.EntityType=N'App_KhoaHoc' AND a.EntityId=CONVERT(nvarchar(100),@CourseId))
            OR (a.EntityType=N'App_KhoaHoc_NhomDaoTao' AND EXISTS(
                SELECT 1 FROM dbo.App_KhoaHoc_NhomDaoTao n
                WHERE n.KhoaHocId=@CourseId AND CONVERT(nvarchar(100),n.NhomDaoTaoId)=a.EntityId))
            OR (a.EntityType=N'App_HocVien_PhanCong'
                AND TRY_CONVERT(bigint,JSON_VALUE(a.DuLieuSau,'$.courseId'))=@CourseId)
            """;
        var total=await connection.ExecuteScalarAsync<int>(Command(
            $"SELECT CONVERT(int,COUNT_BIG(1)) FROM dbo.App_AuditLog a WHERE {where};",
            new { CourseId=courseId },cancellationToken));
        var rows=total==0 ? [] : (await connection.QueryAsync<CourseAuditRow>(Command($"""
            SELECT a.CreatedAt AS OccurredAtUtc,COALESCE(a.CreatedBy,N'') AS Actor,
                   a.HanhDong AS Action,COALESCE(JSON_VALUE(a.DuLieuSau,'$.reason'),N'') AS Reason,
                   COALESCE(a.EntityKey,a.EntityId,N'') AS EntityLabel
            FROM dbo.App_AuditLog a WHERE {where}
            ORDER BY a.CreatedAt DESC,a.AuditLogId DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """,new { CourseId=courseId,Offset=(page-1)*pageSize,PageSize=pageSize },cancellationToken))).ToArray();
        return new PagedResult<AuditHistoryItem>
        {
            Items=rows.Select(row=>new AuditHistoryItem(row.OccurredAtUtc,row.Actor,row.Action,row.Reason,row.EntityLabel)).ToArray(),
            Page=page,PageSize=pageSize,TotalItems=total,
        };
    }

    public async Task<AssignmentExportData> GetExportDataAsync(
        long courseId,
        CancellationToken cancellationToken)
    {
        await using var connection=await OpenAsync(cancellationToken);
        var course=await LoadCourseIdentityAsync(connection,courseId,null,cancellationToken);
        var rows=await connection.QueryAsync<ExportRow>(Command("""
            SELECT h.HocVienId,h.SourceProfileCode,h.MaDK AS RegistrationCode,h.HoTen AS FullName,
                   h.NgaySinh AS DateOfBirth,h.GioiTinh AS Gender,h.SoCCCD AS CitizenId,
                   h.DiaChiThuongTru AS PermanentAddress,h.HangGPLXHoc AS TrainingClass,
                   h.MaHangDT AS TrainingClassCode,h.SoGPLXDaCo AS ExistingLicenseNumber,
                   h.HangGPLXDaCo AS ExistingLicenseClass,gh.HoTen AS DossierReceiverName,
                   k.TenKhoa AS CourseName,h.MaKhoa AS CourseCode,gv.HoTen AS ClassTeacherName,
                   xt.BienSoXe AS TrainingVehiclePlate,x10.BienSoXe AS Figure10VehiclePlate,
                   gh.MaGiaoVienHs AS DossierReceiverCode,n.MaNhom AS GroupCode,
                   gv.MaGV AS ClassTeacherCode,pc.PhanCongId AS AssignmentId,
                   pc.RowVersion AS AssignmentRowVersion
            FROM dbo.App_HocVien h
            JOIN dbo.App_KhoaHoc k ON k.KhoaHocId=@CourseId AND h.MaKhoa=k.MaKhoa
                 AND h.SourceProfileCode=k.SourceProfileCode
            LEFT JOIN dbo.App_HocVien_PhanCong pc ON pc.HocVienId=h.HocVienId AND pc.IsCurrent=1
            LEFT JOIN dbo.App_KhoaHoc_NhomDaoTao n ON n.NhomDaoTaoId=pc.NhomDaoTaoId
            LEFT JOIN dbo.App_GiaoVien_hs gh ON gh.GiaoVienHsId=pc.GiaoVienHoSoId
            LEFT JOIN dbo.App_GiaoVien gv ON gv.GiaoVienId=pc.GiaoVienDungLopId
            LEFT JOIN dbo.App_XeTap xt ON xt.XeTapId=pc.XeTapId
            LEFT JOIN dbo.App_XeTap x10 ON x10.XeTapId=pc.XeBaiSo10Id
            WHERE h.IsDeleted=0
            ORDER BY h.HoTen,h.MaDK,h.HocVienId;
            """,new { CourseId=courseId },cancellationToken));
        var lookups=await LoadLookupsAsync(connection,course.SourceProfileCode,cancellationToken);
        var groups=await LoadGroupsAsync(connection,courseId,cancellationToken);
        return new AssignmentExportData(course.KhoaHocId,course.MaKhoa,course.SourceProfileCode,
            rows.Select(ToExportRow).ToArray(),lookups,groups);
    }

    public async Task<AssignmentImportPlan> BuildImportPlanAsync(
        long courseId,
        string sourceProfileCode,
        string fileName,
        string fileSha256,
        IReadOnlyList<ParsedAssignmentImportRow> rows,
        CancellationToken cancellationToken)
    {
        var profile=AssignmentRules.NormalizeProfile(sourceProfileCode,true)!;
        if(rows.Count is <1 or >AssignmentRules.MaxImportRows)
            throw new AssignmentDomainException(Invalid,$"File phải có 1-{AssignmentRules.MaxImportRows:N0} dòng dữ liệu.");
        await using var connection=await OpenAsync(cancellationToken);
        var course=await LoadCourseIdentityAsync(connection,courseId,profile,cancellationToken,
            activeRequired:true);
        var catalog=await LoadImportResolutionCatalogAsync(connection,course,cancellationToken);
        var planRows=new List<AssignmentImportPlanRow>(rows.Count);
        foreach(var row in rows)
        {
            if(row.ValidationMessages.Count>0)
            {
                planRows.Add(new(row.RowNumber,row.RegistrationCode,Invalid,row.ValidationMessages,null));
                continue;
            }
            if(!string.Equals(row.CourseCode,course.MaKhoa,StringComparison.OrdinalIgnoreCase))
            {
                planRows.Add(new(row.RowNumber,row.RegistrationCode,Conflict,
                    ["MaKhoa trong file không khớp khóa được scope bởi route."],null));
                continue;
            }
            var matches=catalog.Learners[row.RegistrationCode].Take(2).ToArray();
            if(matches.Length==0)
            {
                planRows.Add(new(row.RowNumber,row.RegistrationCode,"NOT_FOUND",
                    ["Không tìm thấy học viên trong đúng KhoaHocId/SourceProfileCode."],null));
                continue;
            }
            if(matches.Length>1)
            {
                planRows.Add(new(row.RowNumber,row.RegistrationCode,"AMBIGUOUS",
                    ["Business key trả về nhiều học viên trong cùng scope."],null));
                continue;
            }
            var learner=matches[0];
            if(row.HocVienId.HasValue && row.HocVienId.Value!=learner.HocVienId)
            {
                planRows.Add(new(row.RowNumber,row.RegistrationCode,Conflict,
                    ["HocVienId kỹ thuật không khớp business identity duy nhất trong khóa/profile."],null));
                continue;
            }
            var messages=new List<string>();
            if(!string.IsNullOrWhiteSpace(row.AssignmentRowVersion) &&
               ValidateExpectedAssignmentRowVersion(learner.AssignmentRowVersion,row.AssignmentRowVersion,messages)==Conflict)
            {
                planRows.Add(new(row.RowNumber,row.RegistrationCode,Conflict,messages,null));
                continue;
            }
            try
            {
                var resolved=ResolveImportDesiredState(course,learner.Before,row,catalog);
                var status=SnapshotsEqual(learner.Before,resolved.Snapshot) ? NoChange : Ready;
                planRows.Add(new(row.RowNumber,row.RegistrationCode,status,[],
                    ToTarget(learner,resolved.Snapshot,status,[],resolved.Group)));
            }
            catch(AssignmentDomainException ex)
            {
                planRows.Add(new(row.RowNumber,row.RegistrationCode,ex.Code,[ex.Message],null));
            }
        }
        foreach(var duplicate in planRows.Where(item=>item.Target is not null)
                    .GroupBy(item=>item.Target!.HocVienId).Where(group=>group.Count()>1))
        {
            var states=duplicate.Select(item=>item.Target!.After).Distinct().Count();
            if(states>1)
            {
                foreach(var item in duplicate.ToArray())
                {
                    var index=planRows.IndexOf(item);
                    planRows[index]=item with { Status=Conflict,Messages=["Học viên bị lặp với desired state khác nhau."],Target=null };
                }
            }
            else
            {
                // Identical duplicate input is idempotent: exactly the first
                // occurrence owns the possible mutation; later rows are
                // classified NO_CHANGE and can never create duplicate history.
                foreach(var item in duplicate.Skip(1).ToArray())
                {
                    var index=planRows.IndexOf(item);
                    planRows[index]=item with
                    {
                        Status=NoChange,
                        Messages=["Dòng lặp có cùng desired state; không tạo thêm mutation."],
                        Target=null,
                    };
                }
            }
        }
        return new AssignmentImportPlan(course.KhoaHocId,course.MaKhoa,course.SourceProfileCode,
            course.RowVersion,fileName,fileSha256,planRows);
    }

    public async Task<AssignmentImportConfirmResult> ConfirmImportPlanAsync(
        AssignmentImportPlan plan,
        string actor,
        string reason,
        string idempotencyKey,
        string operationId,
        string previewToken,
        CancellationToken cancellationToken)
    {
        var blocked=plan.Rows.FirstOrDefault(row=>row.Status is not (Ready or NoChange));
        if(blocked is not null)
            throw new AssignmentDomainException(Conflict,$"Import có dòng {blocked.RowNumber} trạng thái {blocked.Status}; không ghi dòng nào.",409);
        await using var connection=await OpenAsync(cancellationToken);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,cancellationToken);
        try
        {
            await AcquireAssignmentOperationLockAsync(
                connection,transaction,idempotencyKey,cancellationToken);
            var payloadSha256=ComputeImportPayloadSha256(plan);
            var replay=await TryReplaySealedImportConfirmAsync(
                connection,transaction,plan,actor,idempotencyKey,payloadSha256,cancellationToken);
            if(replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }
            var transactionAtUtc=await ReadDatabaseUtcNowAsync(
                connection,transaction,cancellationToken);
            var course=await LoadCourseIdentityLockedAsync(connection,transaction,plan.CourseId,
                plan.SourceProfileCode,plan.CourseCode,plan.CourseRowVersion,cancellationToken);
            await StageAndValidateImportTargetsSetBasedAsync(
                connection,transaction,course,plan,cancellationToken);
            var sessionId=await connection.ExecuteScalarAsync<long>(Command("""
                INSERT dbo.App_ImportBatch
                    (LoaiImport,EntityType,FileName,RelativePath,SuggestedCourseCode,TotalRows,
                     MatchedRows,WarningRows,ErrorRows,ApprovedRows,TrangThai,KetQuaJson,IsDeleted,
                     CreatedAt,CreatedBy,FileSha256,TemplateVersion,NormalizationVersion,
                     PreviewExpiresAtUtc,ConfirmedAtUtc,IdempotencyKey)
                OUTPUT INSERTED.ImportBatchId
                VALUES(N'ASSIGNMENT',N'HOCVIEN_ASSIGNMENT',@FileName,NULL,@CourseCode,@TotalRows,
                       @MatchedRows,0,0,@ApprovedRows,N'CONFIRMING',@ResultJson,0,@TransactionAtUtc,@Actor,
                       @FileSha256,N'HOCVIEN_ASSIGNMENT_V2',N'V2',NULL,NULL,@IdempotencyKey);
                """,new
            {
                plan.FileName,plan.CourseCode,TotalRows=plan.Rows.Count,
                MatchedRows=plan.Rows.Count,ApprovedRows=0,
                ResultJson=JsonSerializer.Serialize(new
                    { plan.CourseId,plan.SourceProfileCode,operationId,previewToken }),
                Actor=actor,plan.FileSha256,
                TransactionAtUtc=transactionAtUtc,
                IdempotencyKey=ComputeIdempotencyKeySha256(idempotencyKey),
            },cancellationToken,transaction));
            var changed=await ApplyImportTargetsSetBasedAsync(
                connection,transaction,course,sessionId,actor,reason,operationId,
                transactionAtUtc,cancellationToken);
            var completeAt=transactionAtUtc;
            var updated=await connection.ExecuteAsync(Command("""
                UPDATE dbo.App_ImportBatch
                SET ApprovedRows=@Changed,TrangThai=N'COMPLETED',ConfirmedAtUtc=@CompletedAtUtc,
                    UpdatedAt=@CompletedAtUtc,UpdatedBy=@Actor
                WHERE ImportBatchId=@SessionId AND TrangThai=N'CONFIRMING';
                """,new { Changed=changed,CompletedAtUtc=completeAt,Actor=actor,SessionId=sessionId },
                cancellationToken,transaction));
            AssertExactlyOne(updated,"Import session không thể chuyển sang COMPLETED.");
            await WriteAuditAsync(connection,transaction,"PHAN_CONG_EXCEL","CONFIRM",
                "App_ImportBatch",sessionId,operationId,actor,reason,cancellationToken,
                completeAt);
            await WriteAssignmentOperationLedgerAsync(
                connection,transaction,"IMPORT",null,course,previewToken,idempotencyKey,
                payloadSha256,operationId,actor,requiresBulkPermission: false,changed,
                plan.Rows.Count(row=>row.Status==NoChange),sessionId,completeAt,cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AssignmentImportConfirmResult(sessionId,operationId,changed,
                plan.Rows.Count(row=>row.Status==NoChange),completeAt);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<AssignmentExportData> GetImportResultAsync(
        long courseId,
        long sessionId,
        CancellationToken cancellationToken)
    {
        await using(var connection=await OpenAsync(cancellationToken))
        {
            var valid=await connection.ExecuteScalarAsync<int>(Command("""
                SELECT CASE WHEN EXISTS(
                    SELECT 1 FROM dbo.App_ImportBatch b JOIN dbo.App_KhoaHoc k
                      ON k.KhoaHocId=@CourseId AND k.MaKhoa=b.SuggestedCourseCode
                    WHERE b.ImportBatchId=@SessionId AND b.EntityType=N'HOCVIEN_ASSIGNMENT'
                      AND b.TrangThai=N'COMPLETED'
                      AND TRY_CONVERT(bigint,JSON_VALUE(b.KetQuaJson,'$.CourseId'))=k.KhoaHocId
                      AND JSON_VALUE(b.KetQuaJson,'$.SourceProfileCode')=k.SourceProfileCode
                ) THEN 1 ELSE 0 END;
                """,new { CourseId=courseId,SessionId=sessionId },cancellationToken));
            if(valid!=1) throw new AssignmentDomainException("NOT_FOUND","Không tìm thấy kết quả import trong đúng khóa.",404);
        }
        return await GetExportDataAsync(courseId,cancellationToken);
    }

    private async Task<ImportResolutionCatalog> LoadImportResolutionCatalogAsync(
        SqlConnection connection,
        CourseIdentityRow course,
        CancellationToken cancellationToken)
    {
        using var grid=await connection.QueryMultipleAsync(Command("""
            SELECT h.HocVienId,h.MaDK AS RegistrationCode,COALESCE(h.HoTen,N'') AS LearnerName,
                   h.MaKhoa AS CourseCode,h.SourceProfileCode,h.RowVersion AS LearnerRowVersion,
                   pc.PhanCongId AS CurrentAssignmentId,pc.RowVersion AS AssignmentRowVersion,
                   pc.NhomDaoTaoId AS GroupId,pc.GiaoVienHoSoId AS DossierReceiverId,
                   pc.GiaoVienDungLopId AS ClassTeacherId,pc.XeTapId AS TrainingVehicleId,
                   pc.XeBaiSo10Id AS Figure10VehicleId,
                   COALESCE(pc.IsGiaoVienDungLopOverride,0) AS OverrideClassTeacher,
                   COALESCE(pc.IsXeTapOverride,0) AS OverrideTrainingVehicle,
                   COALESCE(pc.IsXeBaiSo10Override,0) AS OverrideFigure10Vehicle
            FROM dbo.App_HocVien h
            LEFT JOIN dbo.App_HocVien_PhanCong pc ON pc.HocVienId=h.HocVienId AND pc.IsCurrent=1
            WHERE h.IsDeleted=0 AND h.MaKhoa=@CourseCode
              AND h.SourceProfileCode=@SourceProfileCode
            ORDER BY h.HocVienId;

            SELECT NhomDaoTaoId AS GroupId,KhoaHocId,MaNhom AS GroupCode,
                   GiaoVienDungLopId,XeTapId,XeBaiSo10Id,TrangThai,RowVersion
            FROM dbo.App_KhoaHoc_NhomDaoTao
            WHERE KhoaHocId=@CourseId
            ORDER BY NhomDaoTaoId;

            SELECT GiaoVienHsId AS Id,MaGiaoVienHs AS Code
            FROM dbo.App_GiaoVien_hs
            WHERE IsDeleted=0 AND TrangThai='ACTIVE'
            ORDER BY GiaoVienHsId;

            SELECT GiaoVienId AS Id,MaGV AS Code
            FROM dbo.App_GiaoVien
            WHERE SourceProfileCode=@SourceProfileCode AND IsDeleted=0
              AND COALESCE(TrangThaiNguon,1)=1
            ORDER BY GiaoVienId;

            SELECT XeTapId AS Id,NormalizedBienSoXe AS Code
            FROM dbo.App_XeTap
            WHERE SourceProfileCode=@SourceProfileCode AND IsDeleted=0
              AND SourceLifecycle='ACTIVE' AND COALESCE(SourceTrangThai,1)=1
              AND NormalizedBienSoXe IS NOT NULL
            ORDER BY XeTapId;
            """,new
        {
            CourseId=course.KhoaHocId,
            CourseCode=course.MaKhoa,
            course.SourceProfileCode,
        },cancellationToken));
        var learners=(await grid.ReadAsync<AssignmentLearnerRow>()).Select(WithSnapshot).ToArray();
        var groupRows=(await grid.ReadAsync<ImportGroupRow>()).ToArray();
        var groups=groupRows.Select(item=>new GroupDefaultsRow(
            item.GroupId,item.KhoaHocId,item.GiaoVienDungLopId,item.XeTapId,
            item.XeBaiSo10Id,item.TrangThai,item.RowVersion)).ToArray();
        var dossiers=(await grid.ReadAsync<ImportLookupRow>()).ToArray();
        var teachers=(await grid.ReadAsync<ImportLookupRow>()).ToArray();
        var vehicles=(await grid.ReadAsync<ImportLookupRow>()).ToArray();
        return new ImportResolutionCatalog(
            learners.ToLookup(item=>item.RegistrationCode,StringComparer.OrdinalIgnoreCase),
            groups.ToDictionary(item=>item.GroupId),
            groupRows.ToLookup(
                item=>item.GroupCode,
                item=>new GroupDefaultsRow(
                    item.GroupId,item.KhoaHocId,item.GiaoVienDungLopId,item.XeTapId,
                    item.XeBaiSo10Id,item.TrangThai,item.RowVersion),
                StringComparer.OrdinalIgnoreCase),
            dossiers.ToLookup(item=>item.Code,StringComparer.OrdinalIgnoreCase),
            teachers.ToLookup(item=>item.Code,StringComparer.OrdinalIgnoreCase),
            vehicles.ToLookup(item=>item.Code,StringComparer.OrdinalIgnoreCase),
            dossiers.Select(item=>item.Id).ToHashSet(),
            teachers.Select(item=>item.Id).ToHashSet(),
            vehicles.Select(item=>item.Id).ToHashSet());
    }

    private ResolvedImportState ResolveImportDesiredState(
        CourseIdentityRow course,
        AssignmentSnapshot? before,
        ParsedAssignmentImportRow row,
        ImportResolutionCatalog catalog)
    {
        var state=before ?? new AssignmentSnapshot(null,null,null,null,null,true,true,true);
        var group=ResolveImportGroup(course,row,state,catalog);
        if(string.Equals(row.GroupAction,AssignmentAction.Set,StringComparison.OrdinalIgnoreCase))
            state=state with
            {
                GroupId=group!.GroupId,
                ClassTeacherId=group.GiaoVienDungLopId,
                TrainingVehicleId=group.XeTapId,
                Figure10VehicleId=group.XeBaiSo10Id,
                OverrideClassTeacher=false,
                OverrideTrainingVehicle=false,
                OverrideFigure10Vehicle=false,
            };
        else if(string.Equals(row.GroupAction,AssignmentAction.Clear,StringComparison.OrdinalIgnoreCase))
            state=state with
            {
                GroupId=null,
                OverrideClassTeacher=true,
                OverrideTrainingVehicle=true,
                OverrideFigure10Vehicle=true,
            };
        else if(!string.Equals(row.GroupAction,AssignmentAction.Keep,StringComparison.OrdinalIgnoreCase))
            throw new AssignmentDomainException(Invalid,"ActionNhom chỉ hỗ trợ KEEP/SET/CLEAR.");
        else state=RebaseInheritedFields(state,group);

        var dossierId=ResolveImportCode(catalog.DossierReceivers,row.DossierReceiverCode,
            row.DossierReceiverAction,"DOSSIER",normalizeVehicle:false);
        var teacherId=ResolveImportCode(catalog.Teachers,row.ClassTeacherCode,
            row.ClassTeacherAction,"TEACHER",normalizeVehicle:false);
        var vehicleId=ResolveImportCode(catalog.Vehicles,row.TrainingVehiclePlate,
            row.TrainingVehicleAction,"VEHICLE",normalizeVehicle:true);
        var figure10Id=ResolveImportCode(catalog.Vehicles,row.Figure10VehiclePlate,
            row.Figure10VehicleAction,"VEHICLE",normalizeVehicle:true);
        state=ApplyDossier(state,new FieldActionRequest { Action=row.DossierReceiverAction,Id=dossierId });
        state=ApplyTeacher(state,new FieldActionRequest { Action=row.ClassTeacherAction,Id=teacherId },group);
        state=ApplyTrainingVehicle(state,new FieldActionRequest { Action=row.TrainingVehicleAction,Id=vehicleId },group);
        state=ApplyFigure10Vehicle(state,new FieldActionRequest { Action=row.Figure10VehicleAction,Id=figure10Id },group);
        var result=state.HasAnyValue ? state : null;
        if(result is not null) ValidateImportSnapshotReferences(course,result,before,catalog);
        return new ResolvedImportState(result,group);
    }

    private static GroupDefaultsRow? ResolveImportGroup(
        CourseIdentityRow course,
        ParsedAssignmentImportRow row,
        AssignmentSnapshot state,
        ImportResolutionCatalog catalog)
    {
        long? groupId=state.GroupId;
        if(string.Equals(row.GroupAction,AssignmentAction.Set,StringComparison.OrdinalIgnoreCase))
        {
            if(string.IsNullOrWhiteSpace(row.GroupCode)) throw new AssignmentDomainException(Invalid,"SET nhóm yêu cầu MaNhom.");
            var matches=catalog.GroupsByCode[row.GroupCode]
                .Where(group=>string.Equals(group.TrangThai,"ACTIVE",StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray();
            if(matches.Length==0) throw new AssignmentDomainException("NOT_FOUND","Không tìm thấy MaNhom active trong đúng khóa.");
            if(matches.Length>1) throw new AssignmentDomainException("AMBIGUOUS","MaNhom không duy nhất trong đúng khóa.");
            groupId=matches[0].GroupId;
        }
        if(string.Equals(row.GroupAction,AssignmentAction.Clear,StringComparison.OrdinalIgnoreCase)) groupId=null;
        if(!groupId.HasValue) return null;
        var requiresActive=
            string.Equals(row.GroupAction,AssignmentAction.Set,StringComparison.OrdinalIgnoreCase) ||
            new[]
            {
                row.ClassTeacherAction,row.TrainingVehicleAction,row.Figure10VehicleAction,
            }.Any(action=>string.Equals(action,AssignmentAction.Inherit,StringComparison.OrdinalIgnoreCase));
        if(!catalog.GroupsById.TryGetValue(groupId.Value,out var group) || group.KhoaHocId!=course.KhoaHocId)
            throw new AssignmentDomainException(Conflict,"Nhóm hiện tại không còn thuộc đúng khóa.",409);
        if(requiresActive && !string.Equals(group.TrangThai,"ACTIVE",StringComparison.OrdinalIgnoreCase))
            throw new AssignmentDomainException("INACTIVE_REFERENCE","Nhóm không active cho SET/INHERIT.",409);
        return group;
    }

    private static long? ResolveImportCode(
        ILookup<string,ImportLookupRow> lookup,
        string? code,
        string action,
        string kind,
        bool normalizeVehicle)
    {
        if(string.Equals(action,AssignmentAction.Keep,StringComparison.OrdinalIgnoreCase) ||
           string.Equals(action,AssignmentAction.Clear,StringComparison.OrdinalIgnoreCase) ||
           string.Equals(action,AssignmentAction.Inherit,StringComparison.OrdinalIgnoreCase)) return null;
        if(!string.Equals(action,AssignmentAction.Set,StringComparison.OrdinalIgnoreCase))
            throw new AssignmentDomainException(Invalid,$"Action {kind} không hợp lệ.");
        if(string.IsNullOrWhiteSpace(code)) throw new AssignmentDomainException(Invalid,$"SET {kind} yêu cầu mã.");
        var key=normalizeVehicle ? AssignmentRules.NormalizeVehiclePlate(code) : code;
        var matches=lookup[key].Take(2).ToArray();
        if(matches.Length==0) throw new AssignmentDomainException("INACTIVE_REFERENCE",$"{kind} không tồn tại, inactive hoặc khác source profile.");
        if(matches.Length>1) throw new AssignmentDomainException("AMBIGUOUS",$"{kind} không duy nhất trong đúng source profile.");
        return matches[0].Id;
    }

    private static void ValidateImportSnapshotReferences(
        CourseIdentityRow course,
        AssignmentSnapshot snapshot,
        AssignmentSnapshot? before,
        ImportResolutionCatalog catalog)
    {
        if(snapshot.GroupId.HasValue &&
           (!catalog.GroupsById.TryGetValue(snapshot.GroupId.Value,out var group) ||
            group.KhoaHocId!=course.KhoaHocId ||
            (before?.GroupId!=snapshot.GroupId &&
             !string.Equals(group.TrangThai,"ACTIVE",StringComparison.OrdinalIgnoreCase))))
            throw new AssignmentDomainException(Conflict,"Nhóm không active hoặc không thuộc đúng khóa.",409);
        if(snapshot.DossierReceiverId.HasValue && before?.DossierReceiverId!=snapshot.DossierReceiverId &&
           !catalog.ActiveDossierReceiverIds.Contains(snapshot.DossierReceiverId.Value))
            throw new AssignmentDomainException("INACTIVE_REFERENCE","Người nhận hồ sơ không active.",409);
        var establishesTeacherInheritance = !snapshot.OverrideClassTeacher &&
            (before?.OverrideClassTeacher != false || before?.GroupId != snapshot.GroupId);
        var establishesVehicleInheritance = !snapshot.OverrideTrainingVehicle &&
            (before?.OverrideTrainingVehicle != false || before?.GroupId != snapshot.GroupId);
        var establishesFigure10Inheritance = !snapshot.OverrideFigure10Vehicle &&
            (before?.OverrideFigure10Vehicle != false || before?.GroupId != snapshot.GroupId);
        if(snapshot.ClassTeacherId.HasValue &&
           (before?.ClassTeacherId!=snapshot.ClassTeacherId || establishesTeacherInheritance) &&
           !catalog.ActiveTeacherIds.Contains(snapshot.ClassTeacherId.Value))
            throw new AssignmentDomainException("INACTIVE_REFERENCE","Giáo viên không active hoặc khác source profile.",409);
        if(snapshot.TrainingVehicleId.HasValue &&
           (before?.TrainingVehicleId!=snapshot.TrainingVehicleId || establishesVehicleInheritance) &&
           !catalog.ActiveVehicleIds.Contains(snapshot.TrainingVehicleId.Value))
            throw new AssignmentDomainException("INACTIVE_REFERENCE","Xe tập không active hoặc khác source profile.",409);
        if(snapshot.Figure10VehicleId.HasValue &&
           (before?.Figure10VehicleId!=snapshot.Figure10VehicleId || establishesFigure10Inheritance) &&
           !catalog.ActiveVehicleIds.Contains(snapshot.Figure10VehicleId.Value))
            throw new AssignmentDomainException("INACTIVE_REFERENCE","Xe bài số 10 không active hoặc khác source profile.",409);
    }

    private static AssignmentExportRow ToExportRow(ExportRow row)=>new(
        row.HocVienId,row.SourceProfileCode,row.RegistrationCode,row.FullName,
        AssignmentRules.ToDateOnly(row.DateOfBirth),row.Gender,row.CitizenId,row.PermanentAddress,
        row.TrainingClass,row.TrainingClassCode,row.ExistingLicenseNumber,row.ExistingLicenseClass,
        row.DossierReceiverName,row.CourseName,row.CourseCode,row.ClassTeacherName,
        row.TrainingVehiclePlate,row.Figure10VehiclePlate,row.DossierReceiverCode,row.GroupCode,
        row.ClassTeacherCode,row.AssignmentId,
        row.AssignmentRowVersion is null ? null : AssignmentRules.RowVersionToString(row.AssignmentRowVersion));

    private sealed class HistoryRow
    {
        public long AssignmentId { get; init; } public DateTime EffectiveFromUtc { get; init; }
        public DateTime? EffectiveToUtc { get; init; } public bool IsCurrent { get; init; }
        public string Source { get; init; }=string.Empty; public string Actor { get; init; }=string.Empty;
        public string Reason { get; init; }=string.Empty; public long? NhomDaoTaoId { get; init; }
        public string? GroupCode { get; init; } public string? GroupLabel { get; init; } public bool GroupActive { get; init; }
        public long? GiaoVienHoSoId { get; init; } public string? DossierCode { get; init; } public string? DossierLabel { get; init; } public bool DossierActive { get; init; }
        public long? GiaoVienDungLopId { get; init; } public string? TeacherCode { get; init; } public string? TeacherLabel { get; init; } public bool TeacherActive { get; init; }
        public long? XeTapId { get; init; } public string? VehicleCode { get; init; } public string? VehicleLabel { get; init; } public bool VehicleActive { get; init; }
        public long? XeBaiSo10Id { get; init; } public string? Figure10Code { get; init; } public string? Figure10Label { get; init; } public bool Figure10Active { get; init; }
        public bool OverrideClassTeacher { get; init; } public bool OverrideTrainingVehicle { get; init; } public bool OverrideFigure10Vehicle { get; init; }
    }
    private sealed record CourseAuditRow(DateTime OccurredAtUtc,string Actor,string Action,string Reason,string EntityLabel);
    private sealed class ExportRow
    {
        public long HocVienId { get; init; } public string SourceProfileCode { get; init; }=string.Empty;
        public string RegistrationCode { get; init; }=string.Empty; public string? FullName { get; init; }
        public DateTime? DateOfBirth { get; init; } public string? Gender { get; init; } public string? CitizenId { get; init; }
        public string? PermanentAddress { get; init; } public string? TrainingClass { get; init; }
        public string? TrainingClassCode { get; init; } public string? ExistingLicenseNumber { get; init; }
        public string? ExistingLicenseClass { get; init; } public string? DossierReceiverName { get; init; }
        public string? CourseName { get; init; } public string CourseCode { get; init; }=string.Empty;
        public string? ClassTeacherName { get; init; } public string? TrainingVehiclePlate { get; init; }
        public string? Figure10VehiclePlate { get; init; } public string? DossierReceiverCode { get; init; }
        public string? GroupCode { get; init; } public string? ClassTeacherCode { get; init; }
        public long? AssignmentId { get; init; } public byte[]? AssignmentRowVersion { get; init; }
    }
    private sealed record ResolvedImportState(AssignmentSnapshot? Snapshot,GroupDefaultsRow? Group);
    private sealed record ImportLookupRow(long Id,string Code);
    private sealed record ImportGroupRow(
        long GroupId,long KhoaHocId,string GroupCode,long? GiaoVienDungLopId,
        long? XeTapId,long? XeBaiSo10Id,string TrangThai,byte[] RowVersion);
    private sealed record ImportResolutionCatalog(
        ILookup<string,AssignmentLearnerRow> Learners,
        IReadOnlyDictionary<long,GroupDefaultsRow> GroupsById,
        ILookup<string,GroupDefaultsRow> GroupsByCode,
        ILookup<string,ImportLookupRow> DossierReceivers,
        ILookup<string,ImportLookupRow> Teachers,
        ILookup<string,ImportLookupRow> Vehicles,
        IReadOnlySet<long> ActiveDossierReceiverIds,
        IReadOnlySet<long> ActiveTeacherIds,
        IReadOnlySet<long> ActiveVehicleIds);
}

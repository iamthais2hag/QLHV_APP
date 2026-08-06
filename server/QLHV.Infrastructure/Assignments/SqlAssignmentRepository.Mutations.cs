using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Assignments;

namespace QLHV.Infrastructure.Assignments;

public sealed partial class SqlAssignmentRepository
{
    public async Task<AssignmentMutationPlan> BuildAssignmentPlanAsync(
        AssignmentPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var profile = AssignmentRules.NormalizeProfile(request.SourceProfileCode, required: true)!;
        if (request.KhoaHocId <= 0)
        {
            throw new AssignmentDomainException(Invalid, "KhoaHocId không hợp lệ.");
        }

        var operation = (request.Operation ?? string.Empty).Trim().ToUpperInvariant();
        if (operation is not (AssignmentOperation.PutInGroup or AssignmentOperation.BulkAssign or
            AssignmentOperation.StudentOverride or AssignmentOperation.ClearAssignment))
        {
            throw new AssignmentDomainException(Invalid, "Thao tác phân công không hợp lệ.");
        }
        if (request.GroupId.HasValue && operation != AssignmentOperation.PutInGroup)
        {
            throw new AssignmentDomainException(
                Invalid,
                "GroupId chỉ hợp lệ với thao tác PUT_IN_GROUP; INHERIT luôn dùng nhóm hiện tại của học viên.");
        }

        await using var connection = await OpenAsync(cancellationToken);
        var course = await LoadCourseIdentityAsync(connection, request.KhoaHocId, profile, cancellationToken,
            activeRequired: true);
        var learners = await LoadSelectedLearnersAsync(connection, course, request.Selection, cancellationToken);
        if (learners.Count > AssignmentRules.MaxImportRows)
        {
            throw new AssignmentDomainException(Invalid, $"Preview vượt quá {AssignmentRules.MaxImportRows:N0} học viên.");
        }

        GroupDefaultsRow? group = null;
        if (request.GroupId.HasValue)
        {
            group = await LoadGroupDefaultsAsync(connection, request.GroupId.Value, request.KhoaHocId,
                activeRequired: true, transaction: null, cancellationToken);
        }
        if (operation == AssignmentOperation.PutInGroup && group is null)
        {
            throw new AssignmentDomainException(Invalid, "Đưa vào nhóm yêu cầu GroupId active trong đúng khóa.");
        }

        var targets = new List<AssignmentMutationTarget>(learners.Count);
        var groupCache = new Dictionary<long,GroupDefaultsRow>();
        if(group is not null) groupCache[group.GroupId]=group;
        foreach (var learner in learners)
        {
            var hasExpected = request.ExpectedRowVersions.TryGetValue(
                AssignmentRules.ToInvariant(learner.HocVienId), out var expected);
            var messages = new List<string>();
            var status = string.Equals(request.Selection.Mode,"FILTER",StringComparison.OrdinalIgnoreCase) && !hasExpected
                ? Ready
                : ValidateExpectedAssignmentRowVersion(learner.AssignmentRowVersion,expected,messages);
            AssignmentSnapshot? after = learner.Before;
            var effectiveGroup=group;
            if (status != Conflict)
            {
                try
                {
                    if(effectiveGroup is null && learner.Before?.GroupId is long currentGroupId)
                    {
                        if(!groupCache.TryGetValue(currentGroupId,out effectiveGroup))
                        {
                            effectiveGroup=await LoadGroupDefaultsAsync(connection,currentGroupId,request.KhoaHocId,
                                activeRequired:NeedsGroupDefaults(request.Fields),transaction:null,cancellationToken);
                            groupCache[currentGroupId]=effectiveGroup;
                        }
                    }
                    after = BuildDesiredSnapshot(operation, learner.Before, effectiveGroup, request.Fields, request.GroupId);
                    if (after is not null)
                    {
                        await ValidateSnapshotReferencesAsync(connection, null, course, after, cancellationToken,learner.Before);
                    }
                    status = SnapshotsEqual(learner.Before, after) ? NoChange : Ready;
                }
                catch (AssignmentDomainException ex)
                {
                    status = ex.Code;
                    messages.Add(ex.Message);
                }
            }

            targets.Add(ToTarget(learner,after,status,messages,effectiveGroup));
        }

        if (string.Equals(request.Selection.Mode, "IDS", StringComparison.OrdinalIgnoreCase))
        {
            var found = targets.Select(target => target.HocVienId).ToHashSet();
            foreach (var missing in request.Selection.HocVienIds.Where(id => id > 0).Distinct().Where(id => !found.Contains(id)))
            {
                targets.Add(new AssignmentMutationTarget(
                    missing,string.Empty,string.Empty,course.MaKhoa,course.SourceProfileCode,[],null,null,
                    null,null,"NOT_FOUND",["HocVienId không thuộc đúng khóa/source profile hiện tại."]));
            }
        }

        var source = operation switch
        {
            AssignmentOperation.PutInGroup => "GROUP",
            AssignmentOperation.BulkAssign => "BULK",
            _ => "MANUAL",
        };
        return new AssignmentMutationPlan(
            course.KhoaHocId,course.MaKhoa,course.SourceProfileCode,course.RowVersion,
            operation,source,AssignmentRules.RequiresBulkPermission(request),
            targets.OrderBy(target => target.HocVienId).ToArray(),[]);
    }

    public async Task<GroupDefaultsMutationPlan> BuildGroupDefaultsPlanAsync(
        long groupId,
        GroupDefaultsPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var mode = (request.Mode ?? string.Empty).Trim().ToUpperInvariant();
        if (mode is not (GroupPropagationMode.UnoverriddenOnly or GroupPropagationMode.ReplaceAll or
            GroupPropagationMode.NoCurrentChange))
        {
            throw new AssignmentDomainException(Invalid, "Chế độ propagation không hợp lệ.");
        }
        var expectedGroupRowVersion = AssignmentRules.ParseRowVersion(request.RowVersion);
        await using var connection = await OpenAsync(cancellationToken);
        var group = await LoadGroupDefaultsAsync(connection, groupId, null, activeRequired: true,
            transaction: null, cancellationToken);
        if (!expectedGroupRowVersion.SequenceEqual(group.RowVersion))
        {
            throw new AssignmentDomainException(Conflict, "Nhóm đã thay đổi; hãy tải lại dữ liệu.", 409);
        }
        var course = await LoadCourseIdentityAsync(connection, group.KhoaHocId, null, cancellationToken,
            activeRequired: true);
        var desiredDefaults = new AssignmentSnapshot(
            group.GroupId,null,request.DefaultClassTeacherId,request.DefaultTrainingVehicleId,
            request.DefaultFigure10VehicleId,false,false,false);
        var currentDefaults = new AssignmentSnapshot(
            group.GroupId,null,group.GiaoVienDungLopId,group.XeTapId,group.XeBaiSo10Id,false,false,false);
        await ValidateSnapshotReferencesAsync(connection, null, course, desiredDefaults, cancellationToken,currentDefaults);

        var targets = new List<AssignmentMutationTarget>();
        if (mode != GroupPropagationMode.NoCurrentChange)
        {
            var learners = await LoadCurrentGroupAssignmentsAsync(connection, groupId, course, cancellationToken);
            foreach (var learner in learners)
            {
                var before = learner.Before!;
                var after = mode == GroupPropagationMode.ReplaceAll
                    ? before with
                    {
                        ClassTeacherId = request.DefaultClassTeacherId,
                        TrainingVehicleId = request.DefaultTrainingVehicleId,
                        Figure10VehicleId = request.DefaultFigure10VehicleId,
                        OverrideClassTeacher = false,
                        OverrideTrainingVehicle = false,
                        OverrideFigure10Vehicle = false,
                    }
                    : before with
                    {
                        ClassTeacherId = before.OverrideClassTeacher
                            ? before.ClassTeacherId : request.DefaultClassTeacherId,
                        TrainingVehicleId = before.OverrideTrainingVehicle
                            ? before.TrainingVehicleId : request.DefaultTrainingVehicleId,
                        Figure10VehicleId = before.OverrideFigure10Vehicle
                            ? before.Figure10VehicleId : request.DefaultFigure10VehicleId,
                    };
                targets.Add(ToTarget(learner,after,SnapshotsEqual(before,after) ? NoChange : Ready,[]));
            }
        }
        var warnings = mode == GroupPropagationMode.ReplaceAll &&
            targets.Any(target => target.Before is { OverrideClassTeacher: true } or
                { OverrideTrainingVehicle: true } or { OverrideFigure10Vehicle: true })
            ? new[] { "REPLACE_ALL sẽ thay thế override hiện tại và reset cả ba cờ override." }
            : Array.Empty<string>();
        return new GroupDefaultsMutationPlan(
            group.GroupId,group.KhoaHocId,course.MaKhoa,course.SourceProfileCode,course.RowVersion,
            group.RowVersion,mode,
            group.GiaoVienDungLopId,group.XeTapId,group.XeBaiSo10Id,
            request.DefaultClassTeacherId,request.DefaultTrainingVehicleId,request.DefaultFigure10VehicleId,
            AssignmentRules.RequiresBulkGroupPermission(mode),targets,warnings);
    }

    public async Task<AssignmentConfirmResult> ConfirmAssignmentPlanAsync(
        AssignmentMutationPlan plan,
        string actor,
        string reason,
        string operationId,
        string previewToken,
        string idempotencyKey,
        string planFingerprint,
        CancellationToken cancellationToken)
    {
        EnsureConfirmable(plan.Targets);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,cancellationToken);
        try
        {
            await AcquireAssignmentOperationLockAsync(
                connection,transaction,idempotencyKey,cancellationToken);
            var replay=await TryReplaySealedAssignmentConfirmAsync(
                connection,transaction,"ASSIGNMENT",plan.CourseId,plan.SourceProfileCode,
                null,actor,idempotencyKey,
                planFingerprint,cancellationToken);
            if(replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay.Result;
            }
            var transactionAtUtc = await ReadDatabaseUtcNowAsync(
                connection, transaction, cancellationToken);
            var course = await LoadCourseIdentityLockedAsync(connection, transaction, plan.CourseId,
                plan.SourceProfileCode, plan.CourseCode, plan.CourseRowVersion, cancellationToken);
            await ValidateSealedGroupsAsync(connection,transaction,plan.Targets,cancellationToken);
            await LockAndValidateTargetsAsync(connection,transaction,course,plan.Targets,cancellationToken);
            var changed = 0;
            foreach (var target in plan.Targets.Where(target => target.Status == Ready).OrderBy(target=>target.HocVienId))
            {
                await ApplyTargetAsync(connection, transaction, course, target, plan.AssignmentSource,
                    null, actor, reason, operationId, transactionAtUtc, cancellationToken);
                changed++;
            }
            var completedAt=transactionAtUtc;
            await WriteAssignmentOperationLedgerAsync(
                connection,transaction,"ASSIGNMENT",null,course,
                previewToken,idempotencyKey,planFingerprint,operationId,actor,
                plan.RequiresBulkPermission,changed,
                plan.Targets.Count(target=>target.Status==NoChange),null,completedAt,cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AssignmentConfirmResult(operationId,changed,
                plan.Targets.Count(target => target.Status == NoChange),completedAt);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<AssignmentConfirmResult> ConfirmGroupDefaultsPlanAsync(
        GroupDefaultsMutationPlan plan,
        string actor,
        string reason,
        string operationId,
        string previewToken,
        string idempotencyKey,
        string planFingerprint,
        CancellationToken cancellationToken)
    {
        EnsureConfirmable(plan.Targets);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,cancellationToken);
        try
        {
            await AcquireAssignmentOperationLockAsync(
                connection,transaction,idempotencyKey,cancellationToken);
            var replay=await TryReplaySealedAssignmentConfirmAsync(
                connection,transaction,"GROUP_DEFAULTS",plan.CourseId,plan.SourceProfileCode,
                plan.GroupId,actor,idempotencyKey,
                planFingerprint,cancellationToken);
            if(replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay.Result;
            }
            var transactionAtUtc = await ReadDatabaseUtcNowAsync(
                connection, transaction, cancellationToken);
            var course = await LoadCourseIdentityLockedAsync(connection,transaction,plan.CourseId,
                plan.SourceProfileCode,plan.CourseCode,plan.CourseRowVersion,cancellationToken);
            var group = await LoadGroupDefaultsAsync(connection, plan.GroupId, plan.CourseId,
                activeRequired: true, transaction, cancellationToken, lockForUpdate: true);
            if (!group.RowVersion.SequenceEqual(plan.GroupRowVersion))
            {
                throw new AssignmentDomainException(Conflict, "Nhóm đã thay đổi sau preview.", 409);
            }
            var desiredDefaults = new AssignmentSnapshot(
                plan.GroupId,null,plan.DefaultTeacherId,plan.DefaultTrainingVehicleId,
                plan.DefaultFigure10VehicleId,false,false,false);
            var currentDefaults = new AssignmentSnapshot(
                group.GroupId,null,group.GiaoVienDungLopId,group.XeTapId,group.XeBaiSo10Id,false,false,false);
            await ValidateSnapshotReferencesAsync(connection, transaction, course, desiredDefaults, cancellationToken,currentDefaults);

            if (plan.Mode != GroupPropagationMode.NoCurrentChange)
            {
                // A propagation preview seals the complete current membership set, including
                // NO_CHANGE rows.  Re-read that set under SERIALIZABLE range locks so a learner
                // added to or removed from the group after preview cannot be silently omitted.
                var currentMembers = await LoadCurrentGroupAssignmentsAsync(
                    connection, plan.GroupId, course, cancellationToken, transaction, lockForUpdate: true);
                var sealedMemberIds = plan.Targets.Select(target => target.HocVienId).OrderBy(id => id).ToArray();
                var currentMemberIds = currentMembers.Select(member => member.HocVienId).OrderBy(id => id).ToArray();
                if (!sealedMemberIds.SequenceEqual(currentMemberIds))
                {
                    throw new AssignmentDomainException(
                        Conflict,
                        "ThÃ nh viÃªn nhÃ³m Ä‘Ã£ thay Ä‘á»•i sau preview; hÃ£y preview láº¡i.",
                        409);
                }
            }

            await LockAndValidateTargetsAsync(connection,transaction,course,plan.Targets,cancellationToken);

            var defaultsChanged = group.GiaoVienDungLopId != plan.DefaultTeacherId ||
                group.XeTapId != plan.DefaultTrainingVehicleId ||
                group.XeBaiSo10Id != plan.DefaultFigure10VehicleId;
            if (defaultsChanged)
            {
                var groupAffected = await connection.ExecuteAsync(Command("""
                    UPDATE dbo.App_KhoaHoc_NhomDaoTao
                    SET GiaoVienDungLopId=@TeacherId,XeTapId=@VehicleId,XeBaiSo10Id=@Figure10Id,
                        UpdatedAt=@TransactionAtUtc,UpdatedBy=@Actor
                    WHERE NhomDaoTaoId=@GroupId AND KhoaHocId=@CourseId AND RowVersion=@RowVersion;
                    """, new
                {
                    TeacherId = plan.DefaultTeacherId,
                    VehicleId = plan.DefaultTrainingVehicleId,
                    Figure10Id = plan.DefaultFigure10VehicleId,
                    Actor = actor,
                    TransactionAtUtc = transactionAtUtc,
                    plan.GroupId,
                    plan.CourseId,
                    RowVersion = plan.GroupRowVersion,
                }, cancellationToken, transaction));
                AssertExactlyOne(groupAffected,"Nhóm đã thay đổi sau preview.");
            }

            var changed = 0;
            foreach (var target in plan.Targets.Where(target => target.Status == Ready).OrderBy(target=>target.HocVienId))
            {
                await ApplyTargetAsync(connection, transaction, course, target, "GROUP", null,
                    actor, reason, operationId, transactionAtUtc, cancellationToken);
                changed++;
            }
            var completedAt=transactionAtUtc;
            await WriteAssignmentOperationLedgerAsync(
                connection,transaction,"GROUP_DEFAULTS",plan.GroupId,course,
                previewToken,idempotencyKey,planFingerprint,operationId,actor,
                plan.RequiresBulkPermission,changed,
                plan.Targets.Count(target=>target.Status==NoChange),null,completedAt,cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AssignmentConfirmResult(operationId,changed,
                plan.Targets.Count(target => target.Status == NoChange),completedAt);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task ApplyTargetAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CourseIdentityRow course,
        AssignmentMutationTarget target,
        string source,
        long? importSessionId,
        string actor,
        string reason,
        string operationId,
        DateTime transactionAtUtc,
        CancellationToken cancellationToken)
    {
        var current=await LockAndValidateTargetAsync(
            connection,transaction,course,target,cancellationToken);
        if (target.After is not null)
        {
            await ValidateSnapshotReferencesAsync(connection, transaction, course, target.After, cancellationToken,target.Before);
        }

        var effectiveAt = transactionAtUtc;
        if (current is not null && effectiveAt <= current.NgayHieuLuc)
        {
            effectiveAt = current.NgayHieuLuc.AddTicks(1);
        }
        if (current is not null)
        {
            var closed = await connection.ExecuteAsync(Command("""
                UPDATE dbo.App_HocVien_PhanCong
                SET IsCurrent=0,NgayHetHieuLuc=@EffectiveAt,UpdatedAt=@EffectiveAt,UpdatedBy=@Actor
                WHERE PhanCongId=@AssignmentId AND IsCurrent=1 AND RowVersion=@RowVersion;
                """, new
            {
                EffectiveAt = effectiveAt,
                Actor = actor,
                AssignmentId = current.PhanCongId,
                RowVersion = current.RowVersion,
            }, cancellationToken, transaction));
            AssertExactlyOne(closed,$"Không thể đóng snapshot hiện tại của học viên {target.HocVienId}.");
        }
        if (target.After is not null && target.After.HasAnyValue)
        {
            var inserted = await connection.ExecuteAsync(Command("""
                INSERT dbo.App_HocVien_PhanCong
                    (HocVienId,NhomDaoTaoId,GiaoVienHoSoId,GiaoVienDungLopId,XeTapId,XeBaiSo10Id,
                     IsGiaoVienDungLopOverride,IsXeTapOverride,IsXeBaiSo10Override,NguonGan,
                     ImportSessionId,NgayHieuLuc,NgayHetHieuLuc,IsCurrent,GhiChu,CreatedAt,CreatedBy)
                VALUES(@HocVienId,@GroupId,@DossierReceiverId,@ClassTeacherId,@TrainingVehicleId,
                       @Figure10VehicleId,@OverrideClassTeacher,@OverrideTrainingVehicle,
                       @OverrideFigure10Vehicle,@Source,@ImportSessionId,@EffectiveAt,NULL,1,
                       @Reason,@EffectiveAt,@Actor);
                """, new
            {
                target.HocVienId,
                target.After.GroupId,
                target.After.DossierReceiverId,
                target.After.ClassTeacherId,
                target.After.TrainingVehicleId,
                target.After.Figure10VehicleId,
                target.After.OverrideClassTeacher,
                target.After.OverrideTrainingVehicle,
                target.After.OverrideFigure10Vehicle,
                Source = source,
                ImportSessionId = importSessionId,
                EffectiveAt = effectiveAt,
                Reason = reason,
                Actor = actor,
            }, cancellationToken, transaction));
            AssertExactlyOne(inserted,$"Không thể tạo snapshot mới cho học viên {target.HocVienId}.");
        }
        await WriteAssignmentAuditAsync(
            connection,transaction,course,target,operationId,actor,reason,
            transactionAtUtc,cancellationToken);
    }

    private async Task<CurrentAssignmentRow?> LockAndValidateTargetAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CourseIdentityRow course,
        AssignmentMutationTarget target,
        CancellationToken cancellationToken)
    {
        var learners = (await connection.QueryAsync<LearnerLockRow>(Command("""
            SELECT TOP(2) HocVienId,MaDK,MaKhoa,SourceProfileCode,RowVersion
            FROM dbo.App_HocVien WITH (UPDLOCK,HOLDLOCK)
            WHERE IsDeleted=0 AND MaDK=@RegistrationCode AND MaKhoa=@CourseCode
              AND SourceProfileCode=@SourceProfileCode
            ORDER BY HocVienId;
            """, new
        {
            target.RegistrationCode,
            CourseCode=course.MaKhoa,
            course.SourceProfileCode,
        }, cancellationToken, transaction))).ToArray();
        if (learners.Length!=1 || learners[0].HocVienId!=target.HocVienId ||
            !learners[0].RowVersion.SequenceEqual(target.LearnerRowVersion))
        {
            throw new AssignmentDomainException(Conflict,
                $"Business identity học viên {target.HocVienId} đã thay đổi hoặc trở nên mơ hồ sau preview.",409);
        }

        var current = await connection.QuerySingleOrDefaultAsync<CurrentAssignmentRow>(Command("""
            SELECT TOP(2) PhanCongId,HocVienId,NhomDaoTaoId,GiaoVienHoSoId,GiaoVienDungLopId,
                   XeTapId,XeBaiSo10Id,IsGiaoVienDungLopOverride,IsXeTapOverride,
                   IsXeBaiSo10Override,NgayHieuLuc,RowVersion
            FROM dbo.App_HocVien_PhanCong WITH (UPDLOCK,HOLDLOCK)
            WHERE HocVienId=@HocVienId AND IsCurrent=1 ORDER BY PhanCongId;
            """, new { target.HocVienId }, cancellationToken, transaction));
        if (!CurrentMatchesPlan(current,target))
        {
            throw new AssignmentDomainException(Conflict,
                $"Phân công học viên {target.HocVienId} đã thay đổi sau preview.",409);
        }
        return current;
    }

    private async Task LockAndValidateTargetsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CourseIdentityRow course,
        IEnumerable<AssignmentMutationTarget> targets,
        CancellationToken cancellationToken)
    {
        foreach(var target in targets
                    .Where(target=>target.Status is Ready or NoChange)
                    .GroupBy(target=>target.HocVienId)
                    .Select(group=>group.First())
                    .OrderBy(target=>target.HocVienId))
        {
            await LockAndValidateTargetAsync(connection,transaction,course,target,cancellationToken);
        }
    }

    private async Task ValidateSealedGroupsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IEnumerable<AssignmentMutationTarget> targets,
        CancellationToken cancellationToken)
    {
        var groups=targets
            .Where(target=>target.Status is Ready or NoChange && target.GroupDefaults is not null)
            .Select(target=>target.GroupDefaults!)
            .GroupBy(group=>group.GroupId)
            .OrderBy(group=>group.Key);
        foreach(var group in groups)
        {
            var expected=group.First();
            if(group.Skip(1).Any(item=>!SealedGroupEquals(item,expected)))
                throw new AssignmentDomainException(Conflict,"Preview chứa nhiều phiên bản defaults của cùng một nhóm.",409);
            GroupDefaultsRow current;
            try
            {
                current=await LoadGroupDefaultsAsync(connection,expected.GroupId,expected.CourseId,
                    activeRequired:false,transaction,cancellationToken,lockForUpdate:true);
            }
            catch(AssignmentDomainException)
            {
                throw new AssignmentDomainException(Conflict,$"Nhóm {expected.GroupId} không còn tồn tại sau preview.",409);
            }
            if(!current.RowVersion.SequenceEqual(expected.RowVersion) ||
               current.GiaoVienDungLopId!=expected.ClassTeacherId ||
               current.XeTapId!=expected.TrainingVehicleId ||
               current.XeBaiSo10Id!=expected.Figure10VehicleId ||
               !string.Equals(current.TrangThai,expected.Status,StringComparison.Ordinal))
            {
                throw new AssignmentDomainException(Conflict,$"Defaults nhóm {expected.GroupId} đã thay đổi sau preview.",409);
            }
        }
    }

    private static bool SealedGroupEquals(SealedGroupDefaults left,SealedGroupDefaults right)=>
        left.GroupId==right.GroupId && left.CourseId==right.CourseId &&
        left.ClassTeacherId==right.ClassTeacherId && left.TrainingVehicleId==right.TrainingVehicleId &&
        left.Figure10VehicleId==right.Figure10VehicleId &&
        string.Equals(left.Status,right.Status,StringComparison.Ordinal) &&
        left.RowVersion.SequenceEqual(right.RowVersion);

    private async Task<CourseIdentityRow> LoadCourseIdentityLockedAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long courseId,
        string sourceProfileCode,
        string courseCode,
        byte[] rowVersion,
        CancellationToken cancellationToken)
    {
        var course = await LoadCourseIdentityAsync(connection,courseId,sourceProfileCode,
            cancellationToken,transaction,lockForUpdate:true,activeRequired:true);
        if (course.MaKhoa != courseCode || !course.RowVersion.SequenceEqual(rowVersion))
        {
            throw new AssignmentDomainException(Conflict,"Khóa học đã thay đổi sau preview.",409);
        }
        return course;
    }

    private async Task<CourseIdentityRow> LoadCourseIdentityAsync(
        SqlConnection connection,
        long courseId,
        string? sourceProfileCode,
        CancellationToken cancellationToken,
        SqlTransaction? transaction = null,
        bool lockForUpdate = false,
        bool activeRequired = false)
    {
        var lockHint = lockForUpdate ? "WITH (UPDLOCK,HOLDLOCK)" : string.Empty;
        var row = await connection.QuerySingleOrDefaultAsync<CourseIdentityRow>(Command($"""
            SELECT KhoaHocId,MaKhoa,SourceProfileCode,RowVersion
            FROM dbo.App_KhoaHoc {lockHint}
            WHERE KhoaHocId=@CourseId AND IsDeleted=0
              AND (@SourceProfileCode IS NULL OR SourceProfileCode=@SourceProfileCode)
              AND (@ActiveRequired=0 OR COALESCE(TrangThaiNguon,1)=1);
            """, new
            {
                CourseId = courseId,
                SourceProfileCode = sourceProfileCode,
                ActiveRequired = activeRequired,
            },
            cancellationToken,transaction));
        if (row is null || string.IsNullOrWhiteSpace(row.SourceProfileCode))
        {
            throw new AssignmentDomainException("NOT_FOUND",
                "Không tìm thấy khóa học đúng SourceProfileCode.",404);
        }
        return row;
    }

    private async Task<IReadOnlyList<AssignmentLearnerRow>> LoadSelectedLearnersAsync(
        SqlConnection connection,
        CourseIdentityRow course,
        AssignmentSelectionRequest selection,
        CancellationToken cancellationToken)
    {
        var mode = (selection.Mode ?? string.Empty).Trim().ToUpperInvariant();
        string selector;
        object args;
        if (mode == "IDS")
        {
            var ids = selection.HocVienIds.Where(id => id > 0).Distinct().OrderBy(id=>id)
                .Take(AssignmentRules.MaxImportRows + 1).ToArray();
            if (ids.Length == 0)
            {
                throw new AssignmentDomainException(Invalid,"Cần chọn ít nhất một học viên.");
            }
            // One JSON parameter avoids SQL Server's 2,100-parameter ceiling and
            // keeps a 5,000-row selection inside one consistent read (no chunks).
            selector = """
                AND h.HocVienId IN
                (
                    SELECT requested.HocVienId
                    FROM OPENJSON(@IdsJson)
                    WITH (HocVienId BIGINT '$') AS requested
                )
                """;
            args = new
            {
                course.MaKhoa,course.SourceProfileCode,
                IdsJson=JsonSerializer.Serialize(ids),
                Limit=AssignmentRules.MaxImportRows+1,
            };
        }
        else if (mode == "FILTER")
        {
            var filter = selection.Filter ?? new AssignmentSelectionFilter();
            selector = """
                AND (@Keyword IS NULL OR h.MaDK LIKE N'%' + @Keyword + N'%' OR h.HoTen LIKE N'%' + @Keyword + N'%')
                AND (@GroupId IS NULL OR pc.NhomDaoTaoId=@GroupId)
                AND (@UnassignedOnly=0 OR pc.PhanCongId IS NULL)
                """;
            args = new
            {
                course.MaKhoa,course.SourceProfileCode,
                Keyword=AssignmentRules.NormalizeOptional(filter.Keyword,255),filter.GroupId,filter.UnassignedOnly,
                Limit=AssignmentRules.MaxImportRows+1,
            };
        }
        else
        {
            throw new AssignmentDomainException(Invalid,"Selection mode chỉ hỗ trợ IDS hoặc FILTER.");
        }

        var rows = await connection.QueryAsync<AssignmentLearnerRow>(Command($"""
            SELECT TOP (@Limit) h.HocVienId,h.MaDK AS RegistrationCode,COALESCE(h.HoTen,N'') AS LearnerName,
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
            WHERE h.IsDeleted=0 AND h.MaKhoa=@MaKhoa AND h.SourceProfileCode=@SourceProfileCode
            {selector}
            ORDER BY h.HocVienId;
            """,args,cancellationToken));
        return rows.Select(WithSnapshot).ToArray();
    }

    private async Task<IReadOnlyList<AssignmentLearnerRow>> LoadCurrentGroupAssignmentsAsync(
        SqlConnection connection,
        long groupId,
        CourseIdentityRow course,
        CancellationToken cancellationToken,
        SqlTransaction? transaction = null,
        bool lockForUpdate = false)
    {
        var lockHint = lockForUpdate ? "WITH (UPDLOCK,HOLDLOCK)" : string.Empty;
        var rows = await connection.QueryAsync<AssignmentLearnerRow>(Command($"""
            SELECT h.HocVienId,h.MaDK AS RegistrationCode,COALESCE(h.HoTen,N'') AS LearnerName,
                   h.MaKhoa AS CourseCode,h.SourceProfileCode,h.RowVersion AS LearnerRowVersion,
                   pc.PhanCongId AS CurrentAssignmentId,pc.RowVersion AS AssignmentRowVersion,
                   pc.NhomDaoTaoId AS GroupId,pc.GiaoVienHoSoId AS DossierReceiverId,
                   pc.GiaoVienDungLopId AS ClassTeacherId,pc.XeTapId AS TrainingVehicleId,
                   pc.XeBaiSo10Id AS Figure10VehicleId,pc.IsGiaoVienDungLopOverride AS OverrideClassTeacher,
                   pc.IsXeTapOverride AS OverrideTrainingVehicle,
                   pc.IsXeBaiSo10Override AS OverrideFigure10Vehicle
            FROM dbo.App_HocVien_PhanCong pc {lockHint}
            JOIN dbo.App_HocVien h {lockHint} ON h.HocVienId=pc.HocVienId
            WHERE pc.IsCurrent=1 AND pc.NhomDaoTaoId=@GroupId AND h.IsDeleted=0
              AND h.MaKhoa=@MaKhoa AND h.SourceProfileCode=@SourceProfileCode
            ORDER BY h.HocVienId;
            """,new { GroupId=groupId,course.MaKhoa,course.SourceProfileCode },
            cancellationToken,transaction));
        return rows.Select(WithSnapshot).ToArray();
    }

    private async Task<GroupDefaultsRow> LoadGroupDefaultsAsync(
        SqlConnection connection,
        long groupId,
        long? courseId,
        bool activeRequired,
        SqlTransaction? transaction,
        CancellationToken cancellationToken,
        bool lockForUpdate = false)
    {
        var lockHint = lockForUpdate ? "WITH (UPDLOCK,HOLDLOCK)" : string.Empty;
        var row = await connection.QuerySingleOrDefaultAsync<GroupDefaultsRow>(Command($"""
            SELECT NhomDaoTaoId AS GroupId,KhoaHocId,GiaoVienDungLopId,XeTapId,XeBaiSo10Id,TrangThai,RowVersion
            FROM dbo.App_KhoaHoc_NhomDaoTao {lockHint}
            WHERE NhomDaoTaoId=@GroupId AND (@CourseId IS NULL OR KhoaHocId=@CourseId)
              AND (@ActiveRequired=0 OR TrangThai='ACTIVE');
            """,new { GroupId=groupId,CourseId=courseId,ActiveRequired=activeRequired },
            cancellationToken,transaction));
        return row ?? throw new AssignmentDomainException("NOT_FOUND",
            "Không tìm thấy nhóm active trong đúng khóa.",404);
    }

    private async Task ValidateSnapshotReferencesAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CourseIdentityRow course,
        AssignmentSnapshot snapshot,
        CancellationToken cancellationToken,
        AssignmentSnapshot? before = null)
    {
        var valid = await connection.QuerySingleAsync<ReferenceValidationRow>(Command("""
            SELECT
              CONVERT(bit,CASE WHEN @GroupId IS NULL OR EXISTS(
                SELECT 1 FROM dbo.App_KhoaHoc_NhomDaoTao n
                WHERE n.NhomDaoTaoId=@GroupId AND n.KhoaHocId=@CourseId
                  AND (@RequireGroupActive=0 OR n.TrangThai='ACTIVE')) THEN 1 ELSE 0 END) GroupValid,
              CONVERT(bit,CASE WHEN @DossierId IS NULL OR @RequireDossierActive=0 OR EXISTS(
                SELECT 1 FROM dbo.App_GiaoVien_hs gh
                WHERE gh.GiaoVienHsId=@DossierId AND gh.TrangThai='ACTIVE' AND gh.IsDeleted=0) THEN 1 ELSE 0 END) DossierValid,
              CONVERT(bit,CASE WHEN @TeacherId IS NULL OR @RequireTeacherActive=0 OR EXISTS(
                SELECT 1 FROM dbo.App_GiaoVien g
                WHERE g.GiaoVienId=@TeacherId AND g.IsDeleted=0 AND COALESCE(g.TrangThaiNguon,1)=1
                  AND g.SourceProfileCode=@SourceProfileCode) THEN 1 ELSE 0 END) TeacherValid,
              CONVERT(bit,CASE WHEN @VehicleId IS NULL OR @RequireVehicleActive=0 OR EXISTS(
                SELECT 1 FROM dbo.App_XeTap x
                WHERE x.XeTapId=@VehicleId AND x.IsDeleted=0 AND x.SourceLifecycle='ACTIVE'
                  AND COALESCE(x.SourceTrangThai,1)=1
                  AND x.SourceProfileCode=@SourceProfileCode) THEN 1 ELSE 0 END) VehicleValid,
              CONVERT(bit,CASE WHEN @Figure10Id IS NULL OR @RequireFigure10Active=0 OR EXISTS(
                SELECT 1 FROM dbo.App_XeTap x
                WHERE x.XeTapId=@Figure10Id AND x.IsDeleted=0 AND x.SourceLifecycle='ACTIVE'
                  AND COALESCE(x.SourceTrangThai,1)=1
                  AND x.SourceProfileCode=@SourceProfileCode) THEN 1 ELSE 0 END) Figure10Valid;
            """,new
        {
            snapshot.GroupId,
            DossierId=snapshot.DossierReceiverId,
            TeacherId=snapshot.ClassTeacherId,
            VehicleId=snapshot.TrainingVehicleId,
            Figure10Id=snapshot.Figure10VehicleId,
            CourseId=course.KhoaHocId,
            course.SourceProfileCode,
            RequireGroupActive=before?.GroupId!=snapshot.GroupId,
            RequireDossierActive=before?.DossierReceiverId!=snapshot.DossierReceiverId,
            RequireTeacherActive=before?.ClassTeacherId!=snapshot.ClassTeacherId ||
                (!snapshot.OverrideClassTeacher &&
                 (before?.OverrideClassTeacher!=false || before?.GroupId!=snapshot.GroupId)),
            RequireVehicleActive=before?.TrainingVehicleId!=snapshot.TrainingVehicleId ||
                (!snapshot.OverrideTrainingVehicle &&
                 (before?.OverrideTrainingVehicle!=false || before?.GroupId!=snapshot.GroupId)),
            RequireFigure10Active=before?.Figure10VehicleId!=snapshot.Figure10VehicleId ||
                (!snapshot.OverrideFigure10Vehicle &&
                 (before?.OverrideFigure10Vehicle!=false || before?.GroupId!=snapshot.GroupId)),
        },cancellationToken,transaction));
        if (!valid.GroupValid)
            throw new AssignmentDomainException(Conflict,"Nhóm không active hoặc không thuộc đúng khóa.",409);
        if (!valid.DossierValid)
            throw new AssignmentDomainException("INACTIVE_REFERENCE","Người nhận hồ sơ không active.",409);
        if (!valid.TeacherValid)
            throw new AssignmentDomainException("INACTIVE_REFERENCE","Giáo viên không active hoặc khác source profile.",409);
        if (!valid.VehicleValid || !valid.Figure10Valid)
            throw new AssignmentDomainException("INACTIVE_REFERENCE","Xe không active hoặc khác source profile.",409);
    }

    private static AssignmentSnapshot? BuildDesiredSnapshot(
        string operation,
        AssignmentSnapshot? before,
        GroupDefaultsRow? selectedGroup,
        AssignmentFieldsRequest? fields,
        long? requestedGroupId)
    {
        if (operation == AssignmentOperation.ClearAssignment)
            return null;
        // Without a group, teacher/vehicle fields are necessarily explicit
        // learner-level choices (including intentional CLEAR), so all three
        // override flags are true. This is also enforced by the database.
        var state = before ?? new AssignmentSnapshot(null,null,null,null,null,true,true,true);
        state=RebaseInheritedFields(state,selectedGroup);
        if (operation == AssignmentOperation.PutInGroup)
        {
            state = state with
            {
                GroupId=selectedGroup!.GroupId,
                ClassTeacherId=selectedGroup.GiaoVienDungLopId,
                TrainingVehicleId=selectedGroup.XeTapId,
                Figure10VehicleId=selectedGroup.XeBaiSo10Id,
                OverrideClassTeacher=false,OverrideTrainingVehicle=false,OverrideFigure10Vehicle=false,
            };
        }
        else if (requestedGroupId.HasValue)
        {
            state = state with { GroupId=requestedGroupId };
        }
        if (fields is not null)
        {
            state = ApplyDossier(state,fields.DossierReceiver);
            state = ApplyTeacher(state,fields.ClassTeacher,selectedGroup);
            state = ApplyTrainingVehicle(state,fields.TrainingVehicle,selectedGroup);
            state = ApplyFigure10Vehicle(state,fields.Figure10Vehicle,selectedGroup);
        }
        if (!state.GroupId.HasValue)
        {
            state = state with
            {
                OverrideClassTeacher=true,
                OverrideTrainingVehicle=true,
                OverrideFigure10Vehicle=true,
            };
        }
        return state.HasAnyValue ? state : null;
    }

    private static AssignmentSnapshot RebaseInheritedFields(
        AssignmentSnapshot state,
        GroupDefaultsRow? group)
    {
        if(group is null || state.GroupId!=group.GroupId) return state;
        return state with
        {
            ClassTeacherId=state.OverrideClassTeacher ? state.ClassTeacherId : group.GiaoVienDungLopId,
            TrainingVehicleId=state.OverrideTrainingVehicle ? state.TrainingVehicleId : group.XeTapId,
            Figure10VehicleId=state.OverrideFigure10Vehicle ? state.Figure10VehicleId : group.XeBaiSo10Id,
        };
    }

    private static bool NeedsGroupDefaults(AssignmentFieldsRequest? fields) =>
        fields is not null && new[]
        {
            fields.ClassTeacher?.Action,
            fields.TrainingVehicle?.Action,
            fields.Figure10Vehicle?.Action,
        }.Any(action=>string.Equals(action,AssignmentAction.Inherit,StringComparison.OrdinalIgnoreCase));

    private static AssignmentSnapshot ApplyDossier(AssignmentSnapshot state, FieldActionRequest? field)
    {
        if (field is null || string.Equals(field.Action,AssignmentAction.Keep,StringComparison.OrdinalIgnoreCase)) return state;
        if (!AssignmentAction.IsValid(field.Action,false)) throw new AssignmentDomainException(Invalid,"Action người nhận hồ sơ không hợp lệ.");
        if (string.Equals(field.Action,AssignmentAction.Set,StringComparison.OrdinalIgnoreCase) && !field.Id.HasValue)
            throw new AssignmentDomainException(Invalid,"SET người nhận hồ sơ yêu cầu id.");
        return state with { DossierReceiverId=string.Equals(field.Action,AssignmentAction.Set,StringComparison.OrdinalIgnoreCase) ? field.Id : null };
    }

    private static AssignmentSnapshot ApplyTeacher(AssignmentSnapshot state, FieldActionRequest? field, GroupDefaultsRow? group)
    {
        if (field is null || string.Equals(field.Action,AssignmentAction.Keep,StringComparison.OrdinalIgnoreCase)) return state;
        ValidateOverrideAction(field,group,"giáo viên đứng lớp");
        if (string.Equals(field.Action,AssignmentAction.Inherit,StringComparison.OrdinalIgnoreCase))
            return state with { ClassTeacherId=group!.GiaoVienDungLopId,OverrideClassTeacher=false };
        return state with
        {
            ClassTeacherId=string.Equals(field.Action,AssignmentAction.Set,StringComparison.OrdinalIgnoreCase) ? field.Id : null,
            OverrideClassTeacher=true,
        };
    }

    private static AssignmentSnapshot ApplyTrainingVehicle(AssignmentSnapshot state, FieldActionRequest? field, GroupDefaultsRow? group)
    {
        if (field is null || string.Equals(field.Action,AssignmentAction.Keep,StringComparison.OrdinalIgnoreCase)) return state;
        ValidateOverrideAction(field,group,"xe tập lái");
        if (string.Equals(field.Action,AssignmentAction.Inherit,StringComparison.OrdinalIgnoreCase))
            return state with { TrainingVehicleId=group!.XeTapId,OverrideTrainingVehicle=false };
        return state with
        {
            TrainingVehicleId=string.Equals(field.Action,AssignmentAction.Set,StringComparison.OrdinalIgnoreCase) ? field.Id : null,
            OverrideTrainingVehicle=true,
        };
    }

    private static AssignmentSnapshot ApplyFigure10Vehicle(AssignmentSnapshot state, FieldActionRequest? field, GroupDefaultsRow? group)
    {
        if (field is null || string.Equals(field.Action,AssignmentAction.Keep,StringComparison.OrdinalIgnoreCase)) return state;
        ValidateOverrideAction(field,group,"xe bài số 10");
        if (string.Equals(field.Action,AssignmentAction.Inherit,StringComparison.OrdinalIgnoreCase))
            return state with { Figure10VehicleId=group!.XeBaiSo10Id,OverrideFigure10Vehicle=false };
        return state with
        {
            Figure10VehicleId=string.Equals(field.Action,AssignmentAction.Set,StringComparison.OrdinalIgnoreCase) ? field.Id : null,
            OverrideFigure10Vehicle=true,
        };
    }

    private static void ValidateOverrideAction(FieldActionRequest field, GroupDefaultsRow? group, string label)
    {
        if (!AssignmentAction.IsValid(field.Action,true))
            throw new AssignmentDomainException(Invalid,$"Action {label} không hợp lệ.");
        if (string.Equals(field.Action,AssignmentAction.Set,StringComparison.OrdinalIgnoreCase) && !field.Id.HasValue)
            throw new AssignmentDomainException(Invalid,$"SET {label} yêu cầu id.");
        if (string.Equals(field.Action,AssignmentAction.Inherit,StringComparison.OrdinalIgnoreCase) && group is null)
            throw new AssignmentDomainException(Conflict,$"INHERIT {label} yêu cầu học viên thuộc một nhóm hợp lệ.",409);
    }

    private static string ValidateExpectedAssignmentRowVersion(byte[]? current, string? expected, List<string> messages)
    {
        if (current is null && string.IsNullOrWhiteSpace(expected)) return Ready;
        if (current is null || string.IsNullOrWhiteSpace(expected))
        {
            messages.Add("Assignment RowVersion không khớp trạng thái hiện tại.");
            return Conflict;
        }
        try
        {
            if (AssignmentRules.ParseRowVersion(expected).SequenceEqual(current)) return Ready;
        }
        catch (AssignmentDomainException) { }
        messages.Add("Assignment RowVersion đã thay đổi.");
        return Conflict;
    }

    private static AssignmentMutationTarget ToTarget(
        AssignmentLearnerRow row,
        AssignmentSnapshot? after,
        string status,
        IReadOnlyList<string> messages,
        GroupDefaultsRow? group = null) => new(
            row.HocVienId,row.RegistrationCode,row.LearnerName,row.CourseCode,row.SourceProfileCode,
            row.LearnerRowVersion,row.CurrentAssignmentId,row.AssignmentRowVersion,row.Before,after,status,messages,
            group is not null && after?.GroupId==group.GroupId
                ? new SealedGroupDefaults(
                    group.GroupId,group.KhoaHocId,group.GiaoVienDungLopId,group.XeTapId,
                    group.XeBaiSo10Id,group.TrangThai,group.RowVersion)
                : null);

    private static AssignmentLearnerRow WithSnapshot(AssignmentLearnerRow row)
    {
        var before = row.CurrentAssignmentId.HasValue
            ? new AssignmentSnapshot(row.GroupId,row.DossierReceiverId,row.ClassTeacherId,
                row.TrainingVehicleId,row.Figure10VehicleId,row.OverrideClassTeacher,
                row.OverrideTrainingVehicle,row.OverrideFigure10Vehicle)
            : null;
        return row with { Before=before };
    }

    private static bool SnapshotsEqual(AssignmentSnapshot? left, AssignmentSnapshot? right) => left == right;

    private static bool CurrentMatchesPlan(CurrentAssignmentRow? current, AssignmentMutationTarget target)
    {
        if (current is null) return !target.CurrentAssignmentId.HasValue && target.CurrentAssignmentRowVersion is null;
        return current.PhanCongId==target.CurrentAssignmentId && target.CurrentAssignmentRowVersion is not null &&
            current.RowVersion.SequenceEqual(target.CurrentAssignmentRowVersion) &&
            SnapshotsEqual(current.ToSnapshot(),target.Before);
    }

    private static void EnsureConfirmable(IEnumerable<AssignmentMutationTarget> targets)
    {
        var invalid = targets.FirstOrDefault(target => target.Status is not (Ready or NoChange));
        if (invalid is not null)
        {
            throw new AssignmentDomainException(Conflict,
                $"Preview có dòng {invalid.Status}; confirm bị từ chối toàn bộ.",409);
        }
    }

    private sealed record CourseIdentityRow(
        long KhoaHocId,string MaKhoa,string SourceProfileCode,byte[] RowVersion);

    private sealed record AssignmentLearnerRow(
        long HocVienId,string RegistrationCode,string LearnerName,string CourseCode,
        string SourceProfileCode,byte[] LearnerRowVersion,long? CurrentAssignmentId,
        byte[]? AssignmentRowVersion,long? GroupId,long? DossierReceiverId,long? ClassTeacherId,
        long? TrainingVehicleId,long? Figure10VehicleId,bool OverrideClassTeacher,
        bool OverrideTrainingVehicle,bool OverrideFigure10Vehicle,AssignmentSnapshot? Before = null);

    private sealed record GroupDefaultsRow(
        long GroupId,long KhoaHocId,long? GiaoVienDungLopId,long? XeTapId,long? XeBaiSo10Id,
        string TrangThai,byte[] RowVersion);

    private sealed record LearnerLockRow(
        long HocVienId,string MaDK,string MaKhoa,string SourceProfileCode,byte[] RowVersion);

    private sealed record CurrentAssignmentRow(
        long PhanCongId,long HocVienId,long? NhomDaoTaoId,long? GiaoVienHoSoId,
        long? GiaoVienDungLopId,long? XeTapId,long? XeBaiSo10Id,
        bool IsGiaoVienDungLopOverride,bool IsXeTapOverride,bool IsXeBaiSo10Override,
        DateTime NgayHieuLuc,byte[] RowVersion)
    {
        public AssignmentSnapshot ToSnapshot() => new(
            NhomDaoTaoId,GiaoVienHoSoId,GiaoVienDungLopId,XeTapId,XeBaiSo10Id,
            IsGiaoVienDungLopOverride,IsXeTapOverride,IsXeBaiSo10Override);
    }

    private sealed record ReferenceValidationRow(
        bool GroupValid,bool DossierValid,bool TeacherValid,bool VehicleValid,bool Figure10Valid);
}

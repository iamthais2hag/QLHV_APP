using System.Text.RegularExpressions;
using System.Text.Json;
using QLHV.Shared.Paging;

namespace QLHV.Application.Assignments;

public sealed partial class AssignmentService : IAssignmentService
{
    private const string AssignmentKind="ASSIGNMENT";
    private const string GroupDefaultsKind="GROUP_DEFAULTS";
    private const string ImportKind="IMPORT";
    private readonly IAssignmentRepository _repository;
    private readonly AssignmentPreviewStore _previewStore;

    public AssignmentService(IAssignmentRepository repository,AssignmentPreviewStore previewStore)
    {
        _repository=repository;
        _previewStore=previewStore;
    }

    public Task<PagedResult<SourceTeacherItem>> SearchTeachersAsync(CatalogSearchRequest request,CancellationToken cancellationToken)
        =>_repository.SearchTeachersAsync(request,cancellationToken);
    public Task<PagedResult<SourceVehicleItem>> SearchVehiclesAsync(CatalogSearchRequest request,CancellationToken cancellationToken)
        =>_repository.SearchVehiclesAsync(request,cancellationToken);
    public Task<PagedResult<CourseItem>> SearchCoursesAsync(CourseSearchRequest request,CancellationToken cancellationToken)
        =>_repository.SearchCoursesAsync(request,cancellationToken);
    public Task<PagedResult<DossierReceiverItem>> SearchDossierReceiversAsync(CatalogSearchRequest request,CancellationToken cancellationToken)
        =>_repository.SearchDossierReceiversAsync(request,cancellationToken);
    public Task<DossierReceiverHistoryResult> GetDossierReceiverHistoryAsync(long id,CancellationToken cancellationToken)
        =>_repository.GetDossierReceiverHistoryAsync(id,cancellationToken);
    public Task<CourseAssignmentDetail> GetCourseDetailAsync(long courseId,CourseDetailRequest request,CancellationToken cancellationToken)
        =>_repository.GetCourseDetailAsync(courseId,request,cancellationToken);
    public Task<IReadOnlyList<AssignmentHistoryItem>> GetStudentHistoryAsync(long hocVienId,CancellationToken cancellationToken)
        =>_repository.GetStudentHistoryAsync(hocVienId,cancellationToken);
    public Task<PagedResult<AuditHistoryItem>> GetCourseHistoryAsync(long courseId,int page,int pageSize,CancellationToken cancellationToken)
        =>_repository.GetCourseHistoryAsync(courseId,page,pageSize,cancellationToken);

    public Task<DossierReceiverItem> CreateDossierReceiverAsync(
        SaveDossierReceiverRequest request,string actor,CancellationToken cancellationToken)
        =>_repository.CreateDossierReceiverAsync(ToReceiverWrite(request,actor),cancellationToken);

    public Task<DossierReceiverItem> UpdateDossierReceiverAsync(
        long id,SaveDossierReceiverRequest request,string actor,CancellationToken cancellationToken)
        =>_repository.UpdateDossierReceiverAsync(id,ToReceiverWrite(request,actor),
            AssignmentRules.ParseRowVersion(request.RowVersion),cancellationToken);

    public Task<DossierReceiverItem> InactivateDossierReceiverAsync(
        long id,RowVersionCommand request,string actor,CancellationToken cancellationToken)
        =>_repository.InactivateDossierReceiverAsync(id,NormalizeActor(actor),AssignmentRules.NormalizeReason(request.Reason),
            AssignmentRules.ParseRowVersion(request.RowVersion),cancellationToken);

    public Task SoftDeleteDossierReceiverAsync(
        long id,RowVersionCommand request,string actor,CancellationToken cancellationToken)
        =>_repository.SoftDeleteDossierReceiverAsync(id,NormalizeActor(actor),AssignmentRules.NormalizeReason(request.Reason),
            AssignmentRules.ParseRowVersion(request.RowVersion),cancellationToken);

    public Task<TrainingGroupItem> CreateGroupAsync(
        long courseId,SaveTrainingGroupRequest request,string actor,CancellationToken cancellationToken)
        =>_repository.CreateGroupAsync(courseId,ToGroupWrite(request,actor),cancellationToken);

    public Task<TrainingGroupItem> UpdateGroupAsync(
        long courseId,long groupId,SaveTrainingGroupRequest request,string actor,CancellationToken cancellationToken)
        =>_repository.UpdateGroupAsync(courseId,groupId,ToGroupWrite(request,actor),
            AssignmentRules.ParseRowVersion(request.RowVersion),cancellationToken);

    public Task<TrainingGroupItem> InactivateGroupAsync(
        long courseId,long groupId,RowVersionCommand request,string actor,CancellationToken cancellationToken)
        =>_repository.InactivateGroupAsync(courseId,groupId,NormalizeActor(actor),AssignmentRules.NormalizeReason(request.Reason),
            AssignmentRules.ParseRowVersion(request.RowVersion),cancellationToken);

    public async Task<AssignmentPreviewResult> PreviewAssignmentAsync(
        AssignmentPreviewRequest request,string actor,CancellationToken cancellationToken)
    {
        actor=NormalizeActor(actor);
        request.Reason=AssignmentRules.NormalizeReason(request.Reason);
        var plan=await _repository.BuildAssignmentPlanAsync(request,cancellationToken);
        return SealPlan(AssignmentKind,actor,plan,plan.Targets,plan.Warnings);
    }

    public async Task<AssignmentPreviewResult> PreviewGroupDefaultsAsync(
        long groupId,GroupDefaultsPreviewRequest request,string actor,CancellationToken cancellationToken)
    {
        actor=NormalizeActor(actor);
        request.Reason=AssignmentRules.NormalizeReason(request.Reason);
        var plan=await _repository.BuildGroupDefaultsPlanAsync(groupId,request,cancellationToken);
        return SealPlan(GroupDefaultsKind,actor,plan,plan.Targets,plan.Warnings);
    }

    public async Task<AssignmentConfirmResult> ConfirmAssignmentAsync(
        ConfirmPreviewRequest request,string actor,bool canBulkAssign,CancellationToken cancellationToken)
    {
        actor=NormalizeActor(actor);
        var key=NormalizeIdempotencyKey(request.IdempotencyKey);
        var reason=AssignmentRules.NormalizeReason(request.Reason);
        var durable=await _repository.TryReplayAssignmentConfirmAsync(
            AssignmentKind,null,actor,key,request.PreviewToken,cancellationToken);
        if(durable is not null)
        {
            if(durable.RequiresBulkPermission && !canBulkAssign)
                throw new AssignmentDomainException("FORBIDDEN","Replay này yêu cầu quyền phân công hàng loạt.",403);
            return durable.Result;
        }
        var plan=_previewStore.Get<AssignmentMutationPlan>(request.PreviewToken,AssignmentKind,actor);
        var planFingerprint=ComputePlanFingerprint(plan);
        return await _previewStore.RunIdempotentAsync(
            request.PreviewToken,actor,key,planFingerprint,AssignmentKind,async()=>
        {
            if(plan.RequiresBulkPermission && !canBulkAssign)
                throw new AssignmentDomainException("FORBIDDEN","Preview này yêu cầu quyền phân công hàng loạt.",403);
            return await _repository.ConfirmAssignmentPlanAsync(plan,actor,reason,
                Guid.NewGuid().ToString("N"),request.PreviewToken,key,planFingerprint,cancellationToken);
        });
    }

    public async Task<AssignmentConfirmResult> ConfirmGroupDefaultsAsync(
        long groupId,ConfirmPreviewRequest request,string actor,bool canBulkAssign,CancellationToken cancellationToken)
    {
        actor=NormalizeActor(actor);
        var key=NormalizeIdempotencyKey(request.IdempotencyKey);
        var reason=AssignmentRules.NormalizeReason(request.Reason);
        var scopeIdentity=$"{GroupDefaultsKind}:{groupId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var durable=await _repository.TryReplayAssignmentConfirmAsync(
            GroupDefaultsKind,groupId,actor,key,request.PreviewToken,cancellationToken);
        if(durable is not null)
        {
            if(durable.RequiresBulkPermission && !canBulkAssign)
                throw new AssignmentDomainException("FORBIDDEN","Replay này yêu cầu quyền phân công hàng loạt.",403);
            return durable.Result;
        }
        var plan=_previewStore.Get<GroupDefaultsMutationPlan>(request.PreviewToken,GroupDefaultsKind,actor);
        var planFingerprint=ComputePlanFingerprint(plan);
        return await _previewStore.RunIdempotentAsync(
            request.PreviewToken,actor,key,planFingerprint,scopeIdentity,async()=>
        {
            if(plan.GroupId!=groupId)
                throw new AssignmentDomainException("CONFLICT","Preview defaults không thuộc nhóm trong route.",409);
            if(plan.RequiresBulkPermission && !canBulkAssign)
                throw new AssignmentDomainException("FORBIDDEN","Propagation defaults yêu cầu quyền phân công hàng loạt.",403);
            return await _repository.ConfirmGroupDefaultsPlanAsync(plan,actor,reason,
                Guid.NewGuid().ToString("N"),request.PreviewToken,key,planFingerprint,cancellationToken);
        });
    }

    public async Task<AssignmentExportFile> ExportAsync(long courseId,CancellationToken cancellationToken)
        =>AssignmentExcel.CreateExport(await _repository.GetExportDataAsync(courseId,cancellationToken));

    public async Task<AssignmentExportFile> TemplateAsync(long courseId,CancellationToken cancellationToken)
        =>AssignmentExcel.CreateTemplate(await _repository.GetExportDataAsync(courseId,cancellationToken));

    public async Task<AssignmentImportPreviewResult> PreviewImportAsync(
        long courseId,string sourceProfileCode,string fileName,byte[] content,string actor,
        CancellationToken cancellationToken)
    {
        actor=NormalizeActor(actor);
        fileName=ValidateFileName(fileName);
        var parsed=AssignmentExcel.Parse(content);
        var profile=AssignmentRules.NormalizeProfile(sourceProfileCode,true)!;
        if(parsed.TechnicalCourseId.HasValue && parsed.TechnicalCourseId.Value!=courseId)
            throw new AssignmentDomainException("CONFLICT","KhoaHocId kỹ thuật trong file không khớp route.",409);
        if(parsed.TechnicalSourceProfileCode is not null &&
           !string.Equals(parsed.TechnicalSourceProfileCode,profile,StringComparison.Ordinal))
            throw new AssignmentDomainException("CONFLICT","SourceProfileCode kỹ thuật trong file không khớp route.",409);
        var plan=await _repository.BuildImportPlanAsync(courseId,profile,fileName,parsed.Sha256,parsed.Rows,cancellationToken);
        var sealedValue=_previewStore.Put(ImportKind,actor,plan);
        var rows=plan.Rows.Select(row=>new AssignmentImportPreviewRow(
            row.RowNumber,row.RegistrationCode,row.Status,row.Messages)).ToArray();
        return new AssignmentImportPreviewResult(sealedValue.Token,sealedValue.ExpiresAtUtc,fileName,rows.Length,
            new ImportStatusCounts(
                rows.Count(row=>row.Status=="READY"),rows.Count(row=>row.Status=="NO_CHANGE"),
                rows.Count(row=>row.Status=="NOT_FOUND"),rows.Count(row=>row.Status=="AMBIGUOUS"),
                rows.Count(row=>row.Status=="INACTIVE_REFERENCE"),rows.Count(row=>row.Status=="INVALID"),
                rows.Count(row=>row.Status=="CONFLICT")),rows);
    }

    public async Task<AssignmentImportConfirmResult> ConfirmImportAsync(
        long courseId,ConfirmPreviewRequest request,string actor,CancellationToken cancellationToken)
    {
        actor=NormalizeActor(actor);
        var key=NormalizeIdempotencyKey(request.IdempotencyKey);
        var reason=AssignmentRules.NormalizeReason(request.Reason);
        var scopeIdentity=$"{ImportKind}:{courseId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var durable=await _repository.TryReplayImportConfirmAsync(
            courseId,actor,key,request.PreviewToken,cancellationToken);
        if(durable is not null) return durable;
        if(_previewStore.TryGetCompletedForToken<AssignmentImportConfirmResult>(
               request.PreviewToken,actor,key,scopeIdentity,out var completed))
            return completed!;
        var sealedPlan=_previewStore.Get<AssignmentImportPlan>(request.PreviewToken,ImportKind,actor);
        if(sealedPlan.CourseId!=courseId)
            throw new AssignmentDomainException("CONFLICT","Preview import không thuộc khóa trong route.",409);
        var planIdentity=AssignmentRules.ComputeFingerprint(
        [
            ImportKind,
            sealedPlan.CourseId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sealedPlan.SourceProfileCode,
            sealedPlan.FileSha256,
            AssignmentExcel.TemplateVersion,
            AssignmentExcel.NormalizationVersion,
        ]);
        return await _previewStore.RunIdempotentAsync(
            request.PreviewToken,actor,key,planIdentity,scopeIdentity,async()=>
        {
            var plan=_previewStore.Get<AssignmentImportPlan>(request.PreviewToken,ImportKind,actor);
            if(plan.CourseId!=courseId) throw new AssignmentDomainException("CONFLICT","Preview import không thuộc khóa trong route.",409);
            return await _repository.ConfirmImportPlanAsync(plan,actor,reason,key,
                Guid.NewGuid().ToString("N"),request.PreviewToken,cancellationToken);
        });
    }

    public async Task<AssignmentExportFile> ImportResultAsync(
        long courseId,long sessionId,CancellationToken cancellationToken)
        =>AssignmentExcel.CreateExport(await _repository.GetImportResultAsync(courseId,sessionId,cancellationToken),
            $"ket-qua-{sessionId}");

    private AssignmentPreviewResult SealPlan<T>(
        string kind,string actor,T payload,IReadOnlyList<AssignmentMutationTarget> targets,
        IReadOnlyList<string> warnings) where T:class
    {
        var fingerprint=ComputePlanFingerprint(payload);
        var sealedValue=_previewStore.Put(kind,actor,payload);
        return new AssignmentPreviewResult(sealedValue.Token,sealedValue.ExpiresAtUtc,fingerprint,
            targets.Count,targets.Count(target=>target.Status=="READY"),
            targets.Count(target=>target.Status=="NO_CHANGE"),targets.Count(target=>target.Status=="CONFLICT"),
            targets.Count(target=>target.Status is not ("READY" or "NO_CHANGE" or "CONFLICT")),warnings,
            targets.Select(target=>new AssignmentPreviewRow(
                target.HocVienId,target.RegistrationCode,target.LearnerName,target.Status,
                target.Before?.ToDto(),target.After?.ToDto(),target.Messages)).ToArray());
    }

    private static DossierReceiverWrite ToReceiverWrite(SaveDossierReceiverRequest request,string actor)
    {
        RejectOuterWhitespace(request.MaGiaoVienHs,"Mã giáo viên hồ sơ");
        RejectOuterWhitespace(request.HoTen,"Họ tên");
        var code=AssignmentRules.NormalizeRequired(request.MaGiaoVienHs,50,"Mã giáo viên hồ sơ").ToUpperInvariant();
        var fullName=AssignmentRules.NormalizeRequired(request.HoTen,255,"Họ tên");
        var citizenId=AssignmentRules.NormalizeOptional(request.SoCccd,20);
        if(citizenId is not null && !CitizenIdRegex().IsMatch(citizenId))
            throw new AssignmentDomainException("INVALID","CCCD phải có đúng 9 hoặc 12 chữ số.");
        var status=AssignmentRules.NormalizeRequired(request.TrangThai,20,"Trạng thái").ToUpperInvariant();
        if(status is not ("ACTIVE" or "INACTIVE")) throw new AssignmentDomainException("INVALID","Trạng thái không hợp lệ.");
        return new DossierReceiverWrite(code,fullName,AssignmentRules.NormalizeSearchName(fullName),request.NgaySinh,
            citizenId,status,AssignmentRules.NormalizeOptional(request.GhiChu,1000),NormalizeActor(actor),
            AssignmentRules.NormalizeReason(request.Reason));
    }

    private static TrainingGroupWrite ToGroupWrite(SaveTrainingGroupRequest request,string actor)
    {
        RejectOuterWhitespace(request.MaNhom,"Mã nhóm"); RejectOuterWhitespace(request.TenNhom,"Tên nhóm");
        if(request.ThuTu<0) throw new AssignmentDomainException("INVALID","Thứ tự nhóm không được âm.");
        return new TrainingGroupWrite(
            AssignmentRules.NormalizeRequired(request.MaNhom,50,"Mã nhóm").ToUpperInvariant(),
            AssignmentRules.NormalizeRequired(request.TenNhom,255,"Tên nhóm"),request.ThuTu,
            PositiveOrNull(request.DefaultClassTeacherId,"Giáo viên mặc định"),
            PositiveOrNull(request.DefaultTrainingVehicleId,"Xe tập mặc định"),
            PositiveOrNull(request.DefaultFigure10VehicleId,"Xe bài số 10 mặc định"),
            AssignmentRules.NormalizeOptional(request.GhiChu,1000),NormalizeActor(actor),
            AssignmentRules.NormalizeReason(request.Reason));
    }

    private static string NormalizeActor(string actor)=>AssignmentRules.NormalizeRequired(actor,100,"Actor");
    private static string ComputePlanFingerprint<T>(T plan) where T:class =>
        AssignmentRules.ComputeFingerprint([JsonSerializer.Serialize(plan)]);
    private static string NormalizeIdempotencyKey(string value)=>AssignmentRules.NormalizeRequired(value,100,"IdempotencyKey");
    private static long? PositiveOrNull(long? value,string label)
    {
        if(value is <=0) throw new AssignmentDomainException("INVALID",$"{label} không hợp lệ.");
        return value;
    }
    private static void RejectOuterWhitespace(string value,string label)
    {
        if(value is not null && !string.Equals(value,value.Trim(),StringComparison.Ordinal))
            throw new AssignmentDomainException("INVALID",$"{label} không được có khoảng trắng đầu/cuối.");
    }
    private static string ValidateFileName(string value)
    {
        var fileName=Path.GetFileName(AssignmentRules.NormalizeRequired(value,255,"Tên file"));
        if(!string.Equals(Path.GetExtension(fileName),".xlsx",StringComparison.OrdinalIgnoreCase))
            throw new AssignmentDomainException("INVALID","Chỉ chấp nhận file .xlsx không macro.");
        return fileName;
    }

    [GeneratedRegex("^(?:[0-9]{9}|[0-9]{12})$",RegexOptions.CultureInvariant)]
    private static partial Regex CitizenIdRegex();
}

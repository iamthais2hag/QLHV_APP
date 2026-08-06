using QLHV.Shared.Paging;

namespace QLHV.Application.Assignments;

public interface IAssignmentService
{
    Task<PagedResult<SourceTeacherItem>> SearchTeachersAsync(CatalogSearchRequest request,CancellationToken cancellationToken);
    Task<PagedResult<SourceVehicleItem>> SearchVehiclesAsync(CatalogSearchRequest request,CancellationToken cancellationToken);
    Task<PagedResult<CourseItem>> SearchCoursesAsync(CourseSearchRequest request,CancellationToken cancellationToken);
    Task<PagedResult<DossierReceiverItem>> SearchDossierReceiversAsync(CatalogSearchRequest request,CancellationToken cancellationToken);
    Task<DossierReceiverItem> CreateDossierReceiverAsync(SaveDossierReceiverRequest request,string actor,CancellationToken cancellationToken);
    Task<DossierReceiverItem> UpdateDossierReceiverAsync(long id,SaveDossierReceiverRequest request,string actor,CancellationToken cancellationToken);
    Task<DossierReceiverItem> InactivateDossierReceiverAsync(long id,RowVersionCommand request,string actor,CancellationToken cancellationToken);
    Task SoftDeleteDossierReceiverAsync(long id,RowVersionCommand request,string actor,CancellationToken cancellationToken);
    Task<DossierReceiverHistoryResult> GetDossierReceiverHistoryAsync(long id,CancellationToken cancellationToken);
    Task<CourseAssignmentDetail> GetCourseDetailAsync(long courseId,CourseDetailRequest request,CancellationToken cancellationToken);
    Task<TrainingGroupItem> CreateGroupAsync(long courseId,SaveTrainingGroupRequest request,string actor,CancellationToken cancellationToken);
    Task<TrainingGroupItem> UpdateGroupAsync(long courseId,long groupId,SaveTrainingGroupRequest request,string actor,CancellationToken cancellationToken);
    Task<TrainingGroupItem> InactivateGroupAsync(long courseId,long groupId,RowVersionCommand request,string actor,CancellationToken cancellationToken);
    Task<AssignmentPreviewResult> PreviewAssignmentAsync(AssignmentPreviewRequest request,string actor,CancellationToken cancellationToken);
    Task<AssignmentPreviewResult> PreviewGroupDefaultsAsync(long groupId,GroupDefaultsPreviewRequest request,string actor,CancellationToken cancellationToken);
    Task<AssignmentConfirmResult> ConfirmAssignmentAsync(ConfirmPreviewRequest request,string actor,bool canBulkAssign,CancellationToken cancellationToken);
    Task<AssignmentConfirmResult> ConfirmGroupDefaultsAsync(long groupId,ConfirmPreviewRequest request,string actor,bool canBulkAssign,CancellationToken cancellationToken);
    Task<IReadOnlyList<AssignmentHistoryItem>> GetStudentHistoryAsync(long hocVienId,CancellationToken cancellationToken);
    Task<PagedResult<AuditHistoryItem>> GetCourseHistoryAsync(long courseId,int page,int pageSize,CancellationToken cancellationToken);
    Task<AssignmentExportFile> ExportAsync(long courseId,CancellationToken cancellationToken);
    Task<AssignmentExportFile> TemplateAsync(long courseId,CancellationToken cancellationToken);
    Task<AssignmentImportPreviewResult> PreviewImportAsync(long courseId,string sourceProfileCode,string fileName,byte[] content,string actor,CancellationToken cancellationToken);
    Task<AssignmentImportConfirmResult> ConfirmImportAsync(long courseId,ConfirmPreviewRequest request,string actor,CancellationToken cancellationToken);
    Task<AssignmentExportFile> ImportResultAsync(long courseId,long sessionId,CancellationToken cancellationToken);
}

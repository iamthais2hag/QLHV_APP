using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QLHV.Application.Assignments;
using QLHV.Shared.Paging;

namespace QLHV.Api.Controllers;

public abstract class AssignmentControllerBase : ControllerBase
{
    protected string Actor => User.FindFirstValue(ClaimTypes.Name)?.Trim() is { Length: > 0 } actor
        ? actor
        : throw new AssignmentDomainException("UNAUTHORIZED","Không xác định được người thực hiện.",401);

    protected async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch(AssignmentDomainException ex) { return Problem<T>(ex); }
    }

    protected async Task<IActionResult> ExecuteNoContent(Func<Task> action)
    {
        try { await action(); return NoContent(); }
        catch(AssignmentDomainException ex) { return Problem(ex); }
    }

    protected ActionResult<T> Problem<T>(AssignmentDomainException ex)
    {
        var result=Problem(ex);
        return StatusCode(result.StatusCode ?? ex.StatusCode,result.Value);
    }

    protected ObjectResult Problem(AssignmentDomainException ex)
    {
        var details=new ProblemDetails
        {
            Status=ex.StatusCode,
            Title=ex.StatusCode==409 ? "Dữ liệu đã thay đổi hoặc không còn hợp lệ." : "Không thể xử lý yêu cầu.",
            Detail=ex.Message,
        };
        details.Extensions["code"]=ex.Code;
        details.Extensions["traceId"]=HttpContext.TraceIdentifier;
        return StatusCode(ex.StatusCode,details);
    }
}

[ApiController]
[Route("api/giao-vien")]
[Authorize(Policy=AssignmentPolicies.ViewCatalogs)]
public sealed class GiaoVienController : AssignmentControllerBase
{
    private readonly IAssignmentService _service;
    public GiaoVienController(IAssignmentService service)=>_service=service;

    [HttpGet]
    public Task<ActionResult<PagedResult<SourceTeacherItem>>> Search(
        [FromQuery] CatalogSearchRequest request,CancellationToken cancellationToken)
        =>Execute(()=>_service.SearchTeachersAsync(request,cancellationToken));
}

[ApiController]
[Route("api/giao-vien-ho-so")]
public sealed class GiaoVienHoSoController : AssignmentControllerBase
{
    private readonly IAssignmentService _service;
    public GiaoVienHoSoController(IAssignmentService service)=>_service=service;

    [HttpGet]
    [Authorize(Policy=AssignmentPolicies.ViewCatalogs)]
    public Task<ActionResult<PagedResult<DossierReceiverItem>>> Search(
        [FromQuery] CatalogSearchRequest request,CancellationToken cancellationToken)
        =>Execute(()=>_service.SearchDossierReceiversAsync(request,cancellationToken));

    [HttpPost]
    [Authorize(Policy=AssignmentPolicies.ManageDossierReceivers)]
    public Task<ActionResult<DossierReceiverItem>> Create(
        [FromBody] SaveDossierReceiverRequest request,CancellationToken cancellationToken)
        =>Task.FromResult(Problem<DossierReceiverItem>(MappingEvidenceRequired()));

    [HttpPut("{id:long}")]
    [Authorize(Policy=AssignmentPolicies.ManageDossierReceivers)]
    public Task<ActionResult<DossierReceiverItem>> Update(
        long id,[FromBody] SaveDossierReceiverRequest request,CancellationToken cancellationToken)
        =>Task.FromResult(Problem<DossierReceiverItem>(MappingEvidenceRequired()));

    [HttpPost("{id:long}/inactive")]
    [Authorize(Policy=AssignmentPolicies.ManageDossierReceivers)]
    public Task<ActionResult<DossierReceiverItem>> Inactivate(
        long id,[FromBody] RowVersionCommand request,CancellationToken cancellationToken)
        =>Task.FromResult(Problem<DossierReceiverItem>(MappingEvidenceRequired()));

    [HttpDelete("{id:long}")]
    [Authorize(Policy=AssignmentPolicies.ManageDossierReceivers)]
    public Task<IActionResult> Delete(
        long id,[FromBody] RowVersionCommand request,CancellationToken cancellationToken)
        =>Task.FromResult<IActionResult>(Problem(MappingEvidenceRequired()));

    [HttpGet("{id:long}/history")]
    [Authorize(Policy=AssignmentPolicies.ViewHistory)]
    public Task<ActionResult<DossierReceiverHistoryResult>> History(long id,CancellationToken cancellationToken)
        =>Task.FromResult(Problem<DossierReceiverHistoryResult>(MappingEvidenceRequired()));

    private static AssignmentDomainException MappingEvidenceRequired() => new(
        "DOSSIER_MAPPING_EVIDENCE_REQUIRED",
        "Giáo viên hồ sơ đang chờ bằng chứng mapping từ CSDL nguồn; không cho tạo hoặc sửa danh mục thủ công.",
        StatusCodes.Status409Conflict);
}

[ApiController]
[Route("api/xe-tap-lai")]
[Authorize(Policy=AssignmentPolicies.ViewCatalogs)]
public sealed class XeTapLaiController : AssignmentControllerBase
{
    private readonly IAssignmentService _service;
    public XeTapLaiController(IAssignmentService service)=>_service=service;

    [HttpGet]
    public Task<ActionResult<PagedResult<SourceVehicleItem>>> Search(
        [FromQuery] CatalogSearchRequest request,CancellationToken cancellationToken)
        =>Execute(()=>_service.SearchVehiclesAsync(request,cancellationToken));
}

[ApiController]
[Route("api/khoa-hoc")]
public sealed class KhoaHocController : AssignmentControllerBase
{
    private readonly IAssignmentService _service;
    private readonly IAuthorizationService _authorization;
    public KhoaHocController(IAssignmentService service,IAuthorizationService authorization)
    {
        _service=service;
        _authorization=authorization;
    }

    [HttpGet]
    [Authorize(Policy=AssignmentPolicies.ViewCatalogs)]
    public Task<ActionResult<PagedResult<CourseItem>>> Search(
        [FromQuery] CourseSearchRequest request,CancellationToken cancellationToken)
        =>Execute(()=>_service.SearchCoursesAsync(request,cancellationToken));

    [HttpGet("{id:long}/chi-tiet-phan-cong")]
    [Authorize(Policy=AssignmentPolicies.ViewCatalogs)]
    public Task<ActionResult<CourseAssignmentDetail>> Detail(
        long id,[FromQuery] CourseDetailRequest request,CancellationToken cancellationToken)
        =>Execute(()=>_service.GetCourseDetailAsync(id,request,cancellationToken));

    [HttpPost("{id:long}/nhom-dao-tao")]
    [Authorize(Policy=AssignmentPolicies.ManageGroups)]
    public async Task<ActionResult<TrainingGroupItem>> CreateGroup(
        long id,[FromBody] SaveTrainingGroupRequest request,CancellationToken cancellationToken)
    {
        try
        {
            var created=await _service.CreateGroupAsync(id,request,Actor,cancellationToken);
            return StatusCode(StatusCodes.Status201Created,created);
        }
        catch(AssignmentDomainException ex) { return Problem<TrainingGroupItem>(ex); }
    }

    [HttpPut("{id:long}/nhom-dao-tao/{groupId:long}")]
    [Authorize(Policy=AssignmentPolicies.ManageGroups)]
    public Task<ActionResult<TrainingGroupItem>> UpdateGroup(
        long id,long groupId,[FromBody] SaveTrainingGroupRequest request,CancellationToken cancellationToken)
        =>Execute(()=>_service.UpdateGroupAsync(id,groupId,request,Actor,cancellationToken));

    [HttpPost("{id:long}/nhom-dao-tao/{groupId:long}/inactive")]
    [Authorize(Policy=AssignmentPolicies.ManageGroups)]
    public Task<ActionResult<TrainingGroupItem>> InactivateGroup(
        long id,long groupId,[FromBody] RowVersionCommand request,CancellationToken cancellationToken)
        =>Execute(()=>_service.InactivateGroupAsync(id,groupId,request,Actor,cancellationToken));

    [HttpGet("{id:long}/phan-cong/history")]
    [Authorize(Policy=AssignmentPolicies.ViewHistory)]
    public Task<ActionResult<PagedResult<AuditHistoryItem>>> History(
        long id,[FromQuery] int page=1,[FromQuery] int pageSize=50,CancellationToken cancellationToken=default)
        =>Execute(()=>_service.GetCourseHistoryAsync(id,page,pageSize,cancellationToken));

    [HttpGet("{id:long}/phan-cong/export")]
    [Authorize(Policy=AssignmentPolicies.Export)]
    public async Task<IActionResult> Export(long id,CancellationToken cancellationToken)
    {
        try
        {
            var file=await _service.ExportAsync(id,cancellationToken);
            return File(file.Content,"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",file.FileName);
        }
        catch(AssignmentDomainException ex) { return Problem(ex); }
    }

    [HttpGet("{id:long}/phan-cong/import/template")]
    [Authorize(Policy=AssignmentPolicies.ImportPreview)]
    public async Task<IActionResult> Template(long id,CancellationToken cancellationToken)
    {
        try
        {
            if(!(await _authorization.AuthorizeAsync(User,AssignmentPolicies.Export)).Succeeded)
                return Forbid();
            var file=await _service.TemplateAsync(id,cancellationToken);
            return File(file.Content,"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",file.FileName);
        }
        catch(AssignmentDomainException ex) { return Problem(ex); }
    }

    [HttpPost("{id:long}/phan-cong/import/preview")]
    [Authorize(Policy=AssignmentPolicies.ImportPreview)]
    [RequestSizeLimit(AssignmentRules.MaxImportBytes+1024*1024)]
    public async Task<ActionResult<AssignmentImportPreviewResult>> PreviewImport(
        long id,[FromForm] IFormFile file,[FromForm] string sourceProfileCode,CancellationToken cancellationToken)
    {
        try
        {
            if(file is null || file.Length is <=0 or >AssignmentRules.MaxImportBytes)
                throw new AssignmentDomainException("INVALID","Tệp Excel rỗng hoặc vượt giới hạn 10 MB.");
            await using var stream=new MemoryStream((int)file.Length);
            await file.CopyToAsync(stream,cancellationToken);
            return Ok(await _service.PreviewImportAsync(id,sourceProfileCode,file.FileName,stream.ToArray(),Actor,cancellationToken));
        }
        catch(AssignmentDomainException ex) { return Problem<AssignmentImportPreviewResult>(ex); }
    }

    [HttpPost("{id:long}/phan-cong/import/confirm")]
    [Authorize(Policy=AssignmentPolicies.ImportConfirm)]
    public Task<ActionResult<AssignmentImportConfirmResult>> ConfirmImport(
        long id,[FromBody] ConfirmPreviewRequest request,CancellationToken cancellationToken)
        =>Execute(()=>_service.ConfirmImportAsync(id,request,Actor,cancellationToken));

    [HttpGet("{id:long}/phan-cong/import/{sessionId:long}/result")]
    [Authorize(Policy=AssignmentPolicies.Export)]
    public async Task<IActionResult> ImportResult(long id,long sessionId,CancellationToken cancellationToken)
    {
        try
        {
            var file=await _service.ImportResultAsync(id,sessionId,cancellationToken);
            return File(file.Content,"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",file.FileName);
        }
        catch(AssignmentDomainException ex) { return Problem(ex); }
    }
}

[ApiController]
[Route("api/nhom-dao-tao")]
public sealed class NhomDaoTaoController : AssignmentControllerBase
{
    private readonly IAssignmentService _service;
    private readonly IAuthorizationService _authorization;
    public NhomDaoTaoController(IAssignmentService service,IAuthorizationService authorization)
    {
        _service=service;
        _authorization=authorization;
    }

    [HttpPost("{id:long}/defaults/preview")]
    [Authorize(Policy=AssignmentPolicies.ManageGroups)]
    public async Task<ActionResult<AssignmentPreviewResult>> Preview(
        long id,[FromBody] GroupDefaultsPreviewRequest request,CancellationToken cancellationToken)
    {
        if(AssignmentRules.RequiresBulkGroupPermission(request.Mode) &&
           !(await _authorization.AuthorizeAsync(User,AssignmentPolicies.AssignBulk)).Succeeded)
            return Forbid();
        return await Execute(()=>_service.PreviewGroupDefaultsAsync(id,request,Actor,cancellationToken));
    }

    [HttpPost("{id:long}/defaults/confirm")]
    [Authorize(Policy=AssignmentPolicies.ManageGroups)]
    public async Task<ActionResult<AssignmentConfirmResult>> Confirm(
        long id,[FromBody] ConfirmPreviewRequest request,CancellationToken cancellationToken)
    {
        var canBulkAssign=(await _authorization.AuthorizeAsync(User,AssignmentPolicies.AssignBulk)).Succeeded;
        return await Execute(()=>_service.ConfirmGroupDefaultsAsync(
            id,request,Actor,canBulkAssign,cancellationToken));
    }
}

[ApiController]
[Route("api/phan-cong")]
public sealed class PhanCongController : AssignmentControllerBase
{
    private readonly IAssignmentService _service;
    private readonly IAuthorizationService _authorization;
    public PhanCongController(IAssignmentService service,IAuthorizationService authorization)
    {
        _service=service; _authorization=authorization;
    }

    [HttpPost("preview")]
    [Authorize(Policy=AssignmentPolicies.AssignSingle)]
    public async Task<ActionResult<AssignmentPreviewResult>> Preview(
        [FromBody] AssignmentPreviewRequest request,CancellationToken cancellationToken)
    {
        if(AssignmentRules.RequiresBulkPermission(request) &&
           !(await _authorization.AuthorizeAsync(User,AssignmentPolicies.AssignBulk)).Succeeded)
            return Forbid();
        return await Execute(()=>_service.PreviewAssignmentAsync(request,Actor,cancellationToken));
    }

    [HttpPost("confirm")]
    [Authorize(Policy=AssignmentPolicies.AssignSingle)]
    public async Task<ActionResult<AssignmentConfirmResult>> Confirm(
        [FromBody] ConfirmPreviewRequest request,CancellationToken cancellationToken)
    {
        var canBulkAssign=(await _authorization.AuthorizeAsync(User,AssignmentPolicies.AssignBulk)).Succeeded;
        return await Execute(()=>_service.ConfirmAssignmentAsync(request,Actor,canBulkAssign,cancellationToken));
    }
}

[ApiController]
[Route("api/hoc-vien")]
public sealed class HocVienAssignmentHistoryController : AssignmentControllerBase
{
    private readonly IAssignmentService _service;
    public HocVienAssignmentHistoryController(IAssignmentService service)=>_service=service;

    [HttpGet("{id:long}/phan-cong/history")]
    [Authorize(Policy=AssignmentPolicies.ViewHistory)]
    public Task<ActionResult<IReadOnlyList<AssignmentHistoryItem>>> History(long id,CancellationToken cancellationToken)
        =>Execute(()=>_service.GetStudentHistoryAsync(id,cancellationToken));
}

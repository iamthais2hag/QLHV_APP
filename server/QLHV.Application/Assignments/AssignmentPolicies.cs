namespace QLHV.Application.Assignments;

/// <summary>
/// Capability policies for the integrated course-assignment workflow.  They
/// intentionally stay separate even where the current role mapping is the
/// same, so permissions can be narrowed without changing API contracts.
/// </summary>
public static class AssignmentPolicies
{
    public const string ViewCatalogs = "Assignment.ViewCatalogs";
    public const string ManageDossierReceivers = "Assignment.ManageDossierReceivers";
    public const string ManageGroups = "Assignment.ManageGroups";
    public const string AssignSingle = "Assignment.AssignSingle";
    public const string AssignBulk = "Assignment.AssignBulk";
    public const string ImportPreview = "Assignment.ImportPreview";
    public const string ImportConfirm = "Assignment.ImportConfirm";
    public const string Export = "Assignment.Export";
    public const string ViewHistory = "Assignment.ViewHistory";
}

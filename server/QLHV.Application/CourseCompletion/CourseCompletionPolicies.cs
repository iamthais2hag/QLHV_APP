namespace QLHV.Application.CourseCompletion;

/// <summary>
/// Course-completion capabilities are intentionally independent from course
/// editing and assignment permissions.
/// </summary>
public static class CourseCompletionPolicies
{
    public const string ViewStatus = "Courses.ViewCompletionStatus";
    public const string Preview = "Courses.PreviewCompletion";
    public const string Complete = "Courses.Complete";
}

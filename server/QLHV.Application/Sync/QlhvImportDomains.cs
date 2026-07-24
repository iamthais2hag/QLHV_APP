namespace QLHV.Application.Sync;

public static class QlhvImportDomains
{
    public const string KhoaHoc = "KHOA_HOC";
    public const string GiaoVien = "GIAO_VIEN";
    public const string Relation = "KHOA_HOC_GIAO_VIEN";
    public const string HocVien = "HOC_VIEN";

    public static IReadOnlyList<string> Ordered { get; } =
        [KhoaHoc, GiaoVien, Relation, HocVien];

    public static IReadOnlyList<string> Optional { get; } =
        [KhoaHoc, GiaoVien, Relation];
}

public static class QlhvImportDomainStatuses
{
    public const string Executable = "EXECUTABLE";
    public const string Blocked = "BLOCKED";
    public const string SkippedSchemaNotReady = "SKIPPED_SCHEMA_NOT_READY";
    public const string SkippedSourceNotReady = "SKIPPED_SOURCE_NOT_READY";
    public const string SkippedDependencyNotReady = "SKIPPED_DEPENDENCY_NOT_READY";
    public const string Succeeded = "SUCCESS";
    public const string Failed = "FAILED";
    public const string NoOp = "NO_OP";
}

public static class QlhvImportOverallStatuses
{
    public const string Success = "SUCCESS";
    public const string PartialSuccess = "PARTIAL_SUCCESS";
    public const string Failed = "FAILED";
    public const string NoOp = "NO_OP";
}

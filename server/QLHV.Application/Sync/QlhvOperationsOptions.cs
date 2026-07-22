namespace QLHV.Application.Sync;

public sealed class QlhvOperationsOptions
{
    public const string SectionName = "QlhvOperations";

    public string? AdminKey { get; set; }

    public int QueueCapacity { get; set; } = 8;

    public int DatabaseCommandTimeoutSeconds { get; set; } = 3600;
}

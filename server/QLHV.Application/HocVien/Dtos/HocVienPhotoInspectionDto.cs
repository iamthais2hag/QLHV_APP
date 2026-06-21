namespace QLHV.Application.HocVien.Dtos;

public sealed class HocVienPhotoInspectionDto
{
    public string ExpectedRelativePath { get; init; } = string.Empty;

    public bool HasPhoto { get; init; }

    public string PhotoStatus { get; init; } = "Missing";

    public string Message { get; init; } = string.Empty;
}

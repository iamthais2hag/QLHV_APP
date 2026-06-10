namespace QLHV.Application.HocVien.Dtos;

public sealed class HocVienExportFileDto
{
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public byte[] Content { get; init; } = [];
}

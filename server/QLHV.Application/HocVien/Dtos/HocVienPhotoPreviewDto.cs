namespace QLHV.Application.HocVien.Dtos;

public sealed class HocVienPhotoPreviewDto
{
    public string ContentType { get; init; } = "image/jpeg";

    public byte[] Content { get; init; } = [];

    public int PixelWidth { get; init; }

    public int PixelHeight { get; init; }
}

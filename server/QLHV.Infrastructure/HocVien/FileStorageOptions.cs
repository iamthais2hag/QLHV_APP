namespace QLHV.Infrastructure.HocVien;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public string Root { get; set; } = "../..";

    public string HocVienPhotoFolder { get; set; } = "IM_GPLX";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
}

namespace QLHV.Application.HocVien.Photos;

public sealed class HocVienPhotoProcessingOptions
{
    public const string SectionName = "PhotoProcessing";

    public bool Enabled { get; set; }

    public string SourceRoot { get; set; } = @"D:\IM_GPLX";

    public string OutputRoot { get; set; } = @"D:\QLHV_APP\IM_GPLX";

    public string ModelPath { get; set; } = string.Empty;

    public string ModelSha256 { get; set; } = string.Empty;

    public string ModelLicense { get; set; } = string.Empty;

    public string ModelLicenseManifestPath { get; set; } = string.Empty;

    public string ModelLicenseManifestSha256 { get; set; } = string.Empty;

    public string BackgroundColor { get; set; } = "#0067B1";

    public bool AutoProcessAfterSync { get; set; }

    public double MinimumAutoApprovalConfidence { get; set; } = 0.85d;

    public int QueueCapacity { get; set; } = 256;

    public int InputWidth { get; set; } = 512;

    public int InputHeight { get; set; } = 512;

    public string InputName { get; set; } = "input";

    public string OutputName { get; set; } = "output";

    public int JpegQuality { get; set; } = 92;

    public long MaxSourceBytes { get; set; } = 25 * 1024 * 1024;
}

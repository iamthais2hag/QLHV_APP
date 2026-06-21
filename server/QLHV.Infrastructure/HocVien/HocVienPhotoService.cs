using ImageMagick;
using QLHV.Application.HocVien;
using QLHV.Application.HocVien.Dtos;

namespace QLHV.Infrastructure.HocVien;

public sealed class HocVienPhotoService : IHocVienPhotoService
{
    private readonly HocVienPhotoPathResolver _resolver;

    public HocVienPhotoService(HocVienPhotoPathResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task<HocVienPhotoPreviewDto?> GetPreviewAsync(
        HocVienListItemDto hocVien,
        CancellationToken cancellationToken = default)
    {
        if (!_resolver.TryResolve(hocVien.MaKhoa, hocVien.MaDangKy, out var fullPath) ||
            !File.Exists(fullPath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        return DecodeToJpeg(bytes);
    }

    public async Task<HocVienPhotoInspectionDto> InspectAsync(
        HocVienListItemDto hocVien,
        bool validateDecode,
        CancellationToken cancellationToken = default)
    {
        var expectedRelativePath = BuildExpectedRelativePath(hocVien.MaKhoa, hocVien.MaDangKy);
        if (!_resolver.TryResolve(hocVien.MaKhoa, hocVien.MaDangKy, out var fullPath))
        {
            return new HocVienPhotoInspectionDto
            {
                ExpectedRelativePath = expectedRelativePath,
                HasPhoto = false,
                PhotoStatus = "UnsafePath",
                Message = "Photo path could not be resolved safely.",
            };
        }

        if (!File.Exists(fullPath))
        {
            return new HocVienPhotoInspectionDto
            {
                ExpectedRelativePath = expectedRelativePath,
                HasPhoto = false,
                PhotoStatus = "Missing",
                Message = "Expected photo file was not found.",
            };
        }

        if (!validateDecode)
        {
            return new HocVienPhotoInspectionDto
            {
                ExpectedRelativePath = expectedRelativePath,
                HasPhoto = true,
                PhotoStatus = "HasPhoto",
                Message = "Photo file exists. Decode was not checked.",
            };
        }

        try
        {
            var preview = await GetPreviewAsync(hocVien, cancellationToken);
            return preview is null
                ? new HocVienPhotoInspectionDto
                {
                    ExpectedRelativePath = expectedRelativePath,
                    HasPhoto = false,
                    PhotoStatus = "Missing",
                    Message = "Expected photo file was not found.",
                }
                : new HocVienPhotoInspectionDto
                {
                    ExpectedRelativePath = expectedRelativePath,
                    HasPhoto = true,
                    PhotoStatus = "HasPhoto",
                    Message = "Photo file exists and can be decoded.",
                };
        }
        catch (NotSupportedException)
        {
            return new HocVienPhotoInspectionDto
            {
                ExpectedRelativePath = expectedRelativePath,
                HasPhoto = true,
                PhotoStatus = "Unsupported",
                Message = "Photo file exists but the image format is not supported by the server decoder.",
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new HocVienPhotoInspectionDto
            {
                ExpectedRelativePath = expectedRelativePath,
                HasPhoto = true,
                PhotoStatus = "Invalid",
                Message = "Photo file exists but could not be read or decoded.",
            };
        }
    }

    private static HocVienPhotoPreviewDto DecodeToJpeg(byte[] bytes)
    {
        try
        {
            using var image = new MagickImage(bytes);
            image.AutoOrient();
            image.Format = MagickFormat.Jpeg;
            image.Quality = 90;
            var width = image.Width;
            var height = image.Height;
            return new HocVienPhotoPreviewDto
            {
                ContentType = "image/jpeg",
                Content = image.ToByteArray(),
                PixelWidth = (int)Math.Min(width, int.MaxValue),
                PixelHeight = (int)Math.Min(height, int.MaxValue),
            };
        }
        catch (MagickException ex)
        {
            if (ex.GetType().Name.Contains("MissingDelegate", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("Photo format is not supported by the server decoder.", ex);
            }

            throw new InvalidDataException("Photo file exists but could not be decoded.", ex);
        }
    }

    private static string BuildExpectedRelativePath(string? maKhoa, string? maDangKy)
    {
        if (!HocVienPhotoPathResolver.IsSafeSegment(maKhoa) ||
            !HocVienPhotoPathResolver.IsSafeSegment(maDangKy))
        {
            return string.Empty;
        }

        return $"{maKhoa!.Trim()}/{maDangKy!.Trim()}.jp2";
    }

}

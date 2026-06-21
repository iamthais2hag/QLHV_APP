using Microsoft.Extensions.Options;
using QLHV.Application.HocVien.Dtos;
using QLHV.Infrastructure.HocVien;

namespace QLHV.Tests.HocVien;

public sealed class HocVienPhotoServiceTests
{
    [Fact]
    public async Task Inspect_missing_file_returns_missing_without_full_path()
    {
        using var fixture = new PhotoFixture();
        var service = fixture.CreateService();

        var result = await service.InspectAsync(CreateHocVien(), validateDecode: false);

        Assert.False(result.HasPhoto);
        Assert.Equal("Missing", result.PhotoStatus);
        Assert.Equal("66016K26A0001/DK001.jp2", result.ExpectedRelativePath);
        Assert.DoesNotContain(@":\", result.ExpectedRelativePath, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Root, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inspect_validate_decode_false_does_not_decode_invalid_photo()
    {
        using var fixture = new PhotoFixture();
        fixture.WritePhoto("66016K26A0001", "DK001", [1, 2, 3, 4, 5]);
        var service = fixture.CreateService();

        var result = await service.InspectAsync(CreateHocVien(), validateDecode: false);

        Assert.True(result.HasPhoto);
        Assert.Equal("HasPhoto", result.PhotoStatus);
        Assert.Contains("Decode was not checked", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inspect_validate_decode_true_marks_photo_that_cannot_decode()
    {
        using var fixture = new PhotoFixture();
        fixture.WritePhoto("66016K26A0001", "DK001", [1, 2, 3, 4, 5]);
        var service = fixture.CreateService();

        var result = await service.InspectAsync(CreateHocVien(), validateDecode: true);

        Assert.True(result.HasPhoto);
        Assert.Contains(result.PhotoStatus, new[] { "Invalid", "Unsupported" });
        Assert.Equal("66016K26A0001/DK001.jp2", result.ExpectedRelativePath);
    }

    [Fact]
    public async Task Inspect_unsafe_path_returns_unsafe_path_without_resolving_full_path()
    {
        using var fixture = new PhotoFixture();
        var service = fixture.CreateService();

        var result = await service.InspectAsync(CreateHocVien(maKhoa: "..\\bad"), validateDecode: false);

        Assert.False(result.HasPhoto);
        Assert.Equal("UnsafePath", result.PhotoStatus);
        Assert.Equal(string.Empty, result.ExpectedRelativePath);
        Assert.DoesNotContain(fixture.Root, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HocVienListItemDto CreateHocVien(
        string maKhoa = "66016K26A0001",
        string maDangKy = "DK001")
        => new()
        {
            HocVienId = 1,
            MaDangKy = maDangKy,
            HoVaTen = "Hoc vien 1",
            MaKhoa = maKhoa,
        };

    private sealed class PhotoFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public HocVienPhotoService CreateService()
        {
            var apiContentRoot = Path.Combine(Root, "server", "QLHV.Api");
            var resolver = new HocVienPhotoPathResolver(Options.Create(new FileStorageOptions
            {
                Root = "../..",
                HocVienPhotoFolder = "IM_GPLX",
                ContentRootPath = apiContentRoot,
            }));

            return new HocVienPhotoService(resolver);
        }

        public void WritePhoto(string maKhoa, string maDangKy, byte[] bytes)
        {
            var path = Path.Combine(Root, "IM_GPLX", maKhoa, $"{maDangKy}.jp2");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

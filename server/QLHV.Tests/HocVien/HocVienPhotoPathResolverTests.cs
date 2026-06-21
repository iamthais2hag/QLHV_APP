using Microsoft.Extensions.Options;
using QLHV.Infrastructure.HocVien;

namespace QLHV.Tests.HocVien;

public sealed class HocVienPhotoPathResolverTests
{
    [Fact]
    public void Resolver_builds_photo_path_under_configured_photo_root()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var resolver = CreateResolver(root);

        Assert.Equal(Path.GetFullPath(root), resolver.ProjectRoot);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "IM_GPLX"), resolver.PhotoRoot);

        var resolved = resolver.TryResolve(
            "66016K26A0001",
            "66016-20251229-145551540",
            out var fullPath);

        Assert.True(resolved);
        Assert.StartsWith(resolver.PhotoRoot, fullPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("IM_GPLX", "66016K26A0001", "66016-20251229-145551540.jp2"),
            fullPath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("..\\66016K26A0001", "66016-20251229-145551540")]
    [InlineData("66016K26A0001", "..\\66016-20251229-145551540")]
    [InlineData("66016K26A0001", "66016/20251229")]
    [InlineData("66016,K26A0001", "66016-20251229-145551540")]
    public void Resolver_rejects_unsafe_segments(string maKhoa, string maDangKy)
    {
        var resolver = CreateResolver(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        var resolved = resolver.TryResolve(maKhoa, maDangKy, out var fullPath);

        Assert.False(resolved);
        Assert.Equal(string.Empty, fullPath);
    }

    private static HocVienPhotoPathResolver CreateResolver(string root)
    {
        var apiContentRoot = Path.Combine(root, "server", "QLHV.Api");
        return new HocVienPhotoPathResolver(Options.Create(new FileStorageOptions
        {
            Root = "../..",
            HocVienPhotoFolder = "IM_GPLX",
            ContentRootPath = apiContentRoot,
        }));
    }
}

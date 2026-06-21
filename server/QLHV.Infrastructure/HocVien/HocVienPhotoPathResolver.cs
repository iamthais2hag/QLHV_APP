using Microsoft.Extensions.Options;

namespace QLHV.Infrastructure.HocVien;

public sealed class HocVienPhotoPathResolver
{
    private readonly FileStorageOptions _options;

    public HocVienPhotoPathResolver(IOptions<FileStorageOptions> options)
    {
        _options = options.Value;
    }

    public string ProjectRoot => ResolveProjectRoot();

    public string PhotoRoot => Path.GetFullPath(Path.Combine(ProjectRoot, _options.HocVienPhotoFolder));

    public bool TryResolve(string? maKhoa, string? maDangKy, out string fullPath)
    {
        fullPath = string.Empty;
        if (!IsSafeSegment(maKhoa) || !IsSafeSegment(maDangKy))
        {
            return false;
        }

        var root = PhotoRoot;
        var candidate = Path.GetFullPath(Path.Combine(root, maKhoa!, $"{maDangKy}.jp2"));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    public static bool IsSafeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains("..", StringComparison.Ordinal) ||
            trimmed.Contains(',', StringComparison.Ordinal) ||
            trimmed.Contains(Path.DirectorySeparatorChar) ||
            trimmed.Contains(Path.AltDirectorySeparatorChar) ||
            Path.IsPathRooted(trimmed))
        {
            return false;
        }

        return trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private string ResolveProjectRoot()
    {
        var configuredRoot = string.IsNullOrWhiteSpace(_options.Root) ? "../.." : _options.Root.Trim();
        var root = Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(_options.ContentRootPath, configuredRoot);
        return Path.GetFullPath(root);
    }
}

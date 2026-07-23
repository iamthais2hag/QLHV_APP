using Microsoft.Extensions.Options;
using QLHV.Application.HocVien.Photos;

namespace QLHV.Infrastructure.HocVien.Photos;

public sealed class SecureHocVienSourcePhotoPathResolver : IHocVienSourcePhotoPathResolver
{
    private readonly HocVienPhotoProcessingOptions _options;

    public SecureHocVienSourcePhotoPathResolver(IOptions<HocVienPhotoProcessingOptions> options)
    {
        _options = options.Value;
    }

    public HocVienSourcePhotoResolution Resolve(
        string? sourceImagePath,
        string? maKhoa,
        string sourceMaDk,
        bool sourceImagePathInvalid = false)
    {
        if (!TryGetRoot(_options.SourceRoot, out var root) ||
            !SecurePhotoPath.IsSafeSegment(sourceMaDk))
        {
            return Invalid();
        }

        if (sourceImagePathInvalid)
        {
            return Invalid();
        }

        var canonicalRelativePath = SecurePhotoPath.TryBuildRelative(
            maKhoa,
            sourceMaDk + ".jp2");
        if (!string.IsNullOrWhiteSpace(sourceImagePath))
        {
            if (!TryNormalizeExplicit(root, sourceImagePath, out var explicitFull, out var explicitRelative))
            {
                return Invalid();
            }
            if (!string.Equals(
                    Path.GetExtension(explicitFull),
                    ".jp2",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Invalid();
            }

            var isCurrent = canonicalRelativePath is not null &&
                            string.Equals(
                                SecurePhotoPath.NormalizeRelative(explicitRelative),
                                SecurePhotoPath.NormalizeRelative(canonicalRelativePath),
                                StringComparison.OrdinalIgnoreCase);
            if (SecurePhotoPath.IsContainedAndReparseSafe(root, explicitFull) &&
                File.Exists(explicitFull))
            {
                return new HocVienSourcePhotoResolution(
                    HocVienSourcePhotoStatuses.Found,
                    isCurrent ? HocVienSourcePhotoPathKinds.Current : HocVienSourcePhotoPathKinds.Legacy,
                    explicitFull,
                    SecurePhotoPath.NormalizeRelative(explicitRelative),
                    UsedFallback: false);
            }

            if (!SecurePhotoPath.IsContainedAndReparseSafe(root, explicitFull))
            {
                return Invalid();
            }

            if (canonicalRelativePath is null || isCurrent)
            {
                return new HocVienSourcePhotoResolution(
                    HocVienSourcePhotoStatuses.Missing,
                    isCurrent ? HocVienSourcePhotoPathKinds.Current : HocVienSourcePhotoPathKinds.Legacy,
                    explicitFull,
                    SecurePhotoPath.NormalizeRelative(explicitRelative),
                    UsedFallback: false);
            }
        }

        if (canonicalRelativePath is null ||
            !SecurePhotoPath.TryCombineContained(root, canonicalRelativePath, out var fallbackFull) ||
            !SecurePhotoPath.IsContainedAndReparseSafe(root, fallbackFull))
        {
            return Invalid();
        }

        return new HocVienSourcePhotoResolution(
            File.Exists(fallbackFull) ? HocVienSourcePhotoStatuses.Found : HocVienSourcePhotoStatuses.Missing,
            HocVienSourcePhotoPathKinds.Fallback,
            fallbackFull,
            SecurePhotoPath.NormalizeRelative(canonicalRelativePath),
            UsedFallback: true);
    }

    private static bool TryNormalizeExplicit(
        string root,
        string sourcePath,
        out string fullPath,
        out string relativePath)
    {
        fullPath = string.Empty;
        relativePath = string.Empty;
        var value = sourcePath.Trim();
        if (value.Length == 0 || value.IndexOf('\0') >= 0)
        {
            return false;
        }

        try
        {
            if (Path.IsPathRooted(value))
            {
                fullPath = Path.GetFullPath(value);
                if (!SecurePhotoPath.IsLexicallyContained(root, fullPath))
                {
                    return false;
                }

                relativePath = Path.GetRelativePath(root, fullPath);
                return SecurePhotoPath.IsSafeRelativePath(relativePath);
            }

            var normalized = SecurePhotoPath.NormalizeRelative(value);
            var segments = normalized.Split(
                Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length > 0 &&
                string.Equals(
                    segments[0],
                    new DirectoryInfo(root).Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                normalized = Path.Combine(segments.Skip(1).ToArray());
            }

            if (!SecurePhotoPath.IsSafeRelativePath(normalized) ||
                !SecurePhotoPath.TryCombineContained(root, normalized, out fullPath))
            {
                return false;
            }

            relativePath = normalized;
            return true;
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryGetRoot(string? configured, out string root)
    {
        root = string.Empty;
        if (string.IsNullOrWhiteSpace(configured) || !Path.IsPathRooted(configured))
        {
            return false;
        }

        try
        {
            root = SecurePhotoPath.TrimEndingSeparator(Path.GetFullPath(configured.Trim()));
            return !SecurePhotoPath.HasReparsePointInExistingChain(root);
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static HocVienSourcePhotoResolution Invalid() =>
        new(
            HocVienSourcePhotoStatuses.InvalidPath,
            HocVienSourcePhotoPathKinds.Legacy,
            null,
            null,
            UsedFallback: false);
}

public sealed class SecureHocVienPhotoOutputPathResolver : IHocVienPhotoOutputPathResolver
{
    private readonly HocVienPhotoProcessingOptions _options;

    public SecureHocVienPhotoOutputPathResolver(IOptions<HocVienPhotoProcessingOptions> options)
    {
        _options = options.Value;
    }

    public HocVienPhotoOutputResolution Resolve(
        string sourceProfileCode,
        string? maKhoa,
        string sourceMaDk)
    {
        var profile = sourceProfileCode?.Trim().ToUpperInvariant();
        if (profile is not ("CSDT_OTO" or "CSDT_MOTO") ||
            !SecurePhotoPath.IsSafeSegment(maKhoa) ||
            !SecurePhotoPath.IsSafeSegment(sourceMaDk))
        {
            return Unsafe("INVALID_OUTPUT_IDENTITY");
        }

        var relative = Path.Combine(profile, maKhoa!.Trim(), sourceMaDk.Trim() + ".jpg");
        return ResolveStored(relative);
    }

    public HocVienPhotoOutputResolution ResolveStored(string? relativePath)
    {
        if (!TryGetRoots(out var sourceRoot, out var outputRoot) ||
            !SecurePhotoPath.IsSafeRelativePath(relativePath) ||
            !SecurePhotoPath.TryCombineContained(outputRoot, relativePath!, out var fullPath))
        {
            return Unsafe("INVALID_OUTPUT_PATH");
        }

        if (SecurePhotoPath.IsLexicallyContained(sourceRoot, outputRoot) ||
            SecurePhotoPath.IsLexicallyContained(outputRoot, sourceRoot) ||
            string.Equals(sourceRoot, outputRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Unsafe("OUTPUT_ROOT_OVERLAPS_SOURCE_ROOT");
        }

        if (!SecurePhotoPath.IsContainedAndReparseSafe(outputRoot, fullPath, allowMissingRoot: true))
        {
            return Unsafe("OUTPUT_REPARSE_ESCAPE");
        }

        return new HocVienPhotoOutputResolution(
            true,
            fullPath,
            SecurePhotoPath.NormalizeRelative(relativePath!),
            null);
    }

    private bool TryGetRoots(out string sourceRoot, out string outputRoot)
    {
        sourceRoot = string.Empty;
        outputRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(_options.SourceRoot) ||
            string.IsNullOrWhiteSpace(_options.OutputRoot) ||
            !Path.IsPathRooted(_options.SourceRoot) ||
            !Path.IsPathRooted(_options.OutputRoot))
        {
            return false;
        }

        try
        {
            sourceRoot = SecurePhotoPath.TrimEndingSeparator(Path.GetFullPath(_options.SourceRoot.Trim()));
            outputRoot = SecurePhotoPath.TrimEndingSeparator(Path.GetFullPath(_options.OutputRoot.Trim()));
            return !SecurePhotoPath.HasReparsePointInExistingChain(sourceRoot) &&
                   !SecurePhotoPath.HasReparsePointInExistingChain(outputRoot);
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static HocVienPhotoOutputResolution Unsafe(string code) =>
        new(false, null, null, code);
}

internal static class SecurePhotoPath
{
    public static string NormalizeRelative(string value) =>
        value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

    public static bool IsSafeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var segment = value.Trim();
        return segment is not "." and not ".." &&
               !segment.Contains("..", StringComparison.Ordinal) &&
               !segment.Contains(':') &&
               !segment.Contains('\\') &&
               !segment.Contains('/') &&
               segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    public static bool IsSafeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
        {
            return false;
        }

        var normalized = NormalizeRelative(value.Trim());
        var segments = normalized.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0 && segments.All(IsSafeSegment);
    }

    public static string? TryBuildRelative(params string?[] segments) =>
        segments.All(IsSafeSegment)
            ? Path.Combine(segments.Select(value => value!.Trim()).ToArray())
            : null;

    public static bool TryCombineContained(string root, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (!IsSafeRelativePath(relativePath))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(Path.Combine(root, NormalizeRelative(relativePath)));
            return IsLexicallyContained(root, fullPath);
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static bool IsLexicallyContained(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return !Path.IsPathRooted(relative) &&
               relative is not ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    public static bool IsContainedAndReparseSafe(
        string root,
        string candidate,
        bool allowMissingRoot = false)
    {
        if (!IsLexicallyContained(root, candidate))
        {
            return false;
        }

        try
        {
            if (!Directory.Exists(root))
            {
                return allowMissingRoot;
            }

            if (IsReparsePoint(root))
            {
                return false;
            }

            var relative = Path.GetRelativePath(root, candidate);
            var current = root;
            foreach (var segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (!File.Exists(current) && !Directory.Exists(current))
                {
                    break;
                }

                if (!IsReparsePoint(current))
                {
                    continue;
                }

                var info = Directory.Exists(current)
                    ? (FileSystemInfo)new DirectoryInfo(current)
                    : new FileInfo(current);
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is null ||
                    !IsLexicallyContained(root, Path.GetFullPath(target.FullName)))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public static bool IsReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    public static bool HasReparsePointInExistingChain(string path)
    {
        try
        {
            DirectoryInfo? current = new(Path.GetFullPath(path));
            while (current is not null)
            {
                if (current.Exists && IsReparsePoint(current.FullName))
                {
                    return true;
                }

                current = current.Parent;
            }

            return false;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }
    }

    public static string TrimEndingSeparator(string path) =>
        Path.TrimEndingDirectorySeparator(path);
}

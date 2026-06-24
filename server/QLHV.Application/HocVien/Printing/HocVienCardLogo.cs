namespace QLHV.Application.HocVien.Printing;

public static class HocVienCardLogo
{
    private const string ResourceSuffix = ".HocVien.Printing.Assets.LOGO-THANH-CONG.png";
    private static readonly Lazy<byte[]> LogoContent = new(LoadContent);

    public static ReadOnlyMemory<byte> Content => LogoContent.Value;

    public static Stream OpenRead()
        => new MemoryStream(LogoContent.Value, writable: false);

    private static byte[] LoadContent()
    {
        var assembly = typeof(HocVienCardLogo).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        if (resourceName is null)
        {
            throw new InvalidOperationException("Khong tim thay logo the hoc vien trong tai nguyen nhung.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Khong the doc logo the hoc vien.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

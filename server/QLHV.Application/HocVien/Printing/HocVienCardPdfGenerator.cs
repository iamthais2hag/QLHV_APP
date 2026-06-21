using System.Globalization;
using System.Text;
using QLHV.Application.HocVien.Dtos;

namespace QLHV.Application.HocVien.Printing;

public sealed class HocVienCardPdfGenerator : IHocVienCardPdfGenerator
{
    private const double PageWidthPt = 841.88976378d;
    private const double PageHeightPt = 595.27559055d;

    public byte[] CreatePdf(
        IReadOnlyList<HocVienListItemDto> hocViens,
        IReadOnlyDictionary<int, HocVienPhotoPreviewDto>? photosByHocVienId = null)
    {
        var rows = hocViens.Count == 0
            ? Array.Empty<HocVienListItemDto>()
            : hocViens.ToArray();
        var pageCount = HocVienCardLayout.GetPageCount(rows.Length);
        var objects = new List<byte[]>();
        var imageRefs = BuildImageRefs(rows, pageCount, photosByHocVienId);

        AddObject(objects, "<< /Type /Catalog /Pages 2 0 R >>");

        var pageObjectNumbers = Enumerable
            .Range(0, pageCount)
            .Select(index => 3 + index * 2)
            .ToArray();
        AddObject(objects, $"<< /Type /Pages /Kids [{string.Join(" ", pageObjectNumbers.Select(n => $"{n} 0 R"))}] /Count {pageCount} >>");

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var pageObjectNumber = 3 + pageIndex * 2;
            var contentObjectNumber = pageObjectNumber + 1;
            var pageImages = imageRefs
                .Where(item => item.PageIndex == pageIndex)
                .ToArray();
            var content = BuildPageContent(rows, pageIndex, pageImages);
            var xObjects = pageImages.Length == 0
                ? string.Empty
                : $" /XObject << {string.Join(" ", pageImages.Select(image => $"/{image.Name} {image.ObjectNumber} 0 R"))} >>";
            AddObject(
                objects,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Format(PageWidthPt)} {Format(PageHeightPt)}] /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> /F2 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >> >>{xObjects} >> /Contents {contentObjectNumber} 0 R >>");
            AddStreamObject(objects, content);
        }

        foreach (var image in imageRefs)
        {
            AddJpegImageObject(objects, image.Photo);
        }

        return BuildPdf(objects);
    }

    private static string BuildPageContent(
        IReadOnlyList<HocVienListItemDto> hocViens,
        int pageIndex,
        IReadOnlyList<ImageRef> imageRefs)
    {
        var sb = new StringBuilder();
        var start = pageIndex * HocVienCardLayout.CardsPerPage;
        var end = Math.Min(start + HocVienCardLayout.CardsPerPage, hocViens.Count);

        for (var index = start; index < end; index++)
        {
            var slot = HocVienCardLayout.GetSlot(index - start);
            var image = imageRefs.FirstOrDefault(item => item.HocVienIndex == index);
            DrawCard(sb, slot, hocViens[index], image);
        }

        return sb.ToString();
    }

    private static void DrawCard(
        StringBuilder sb,
        CardSlot slot,
        HocVienListItemDto hocVien,
        ImageRef? image)
    {
        var x = HocVienCardLayout.MmToPoint(slot.XMm);
        var top = PageHeightPt - HocVienCardLayout.MmToPoint(slot.YMm);
        var width = HocVienCardLayout.MmToPoint(slot.WidthMm);
        var height = HocVienCardLayout.MmToPoint(slot.HeightMm);
        var y = top - height;

        Rect(sb, x, y, width, height);

        var photoX = x + HocVienCardLayout.MmToPoint(4d);
        var photoY = top - HocVienCardLayout.MmToPoint(5d) - HocVienCardLayout.MmToPoint(HocVienCardLayout.PhotoHeightMm);
        var photoWidth = HocVienCardLayout.MmToPoint(HocVienCardLayout.PhotoWidthMm);
        var photoHeight = HocVienCardLayout.MmToPoint(HocVienCardLayout.PhotoHeightMm);
        if (image is not null)
        {
            DrawImage(sb, image.Name, photoX, photoY, photoWidth, photoHeight);
        }

        Rect(sb, photoX, photoY, photoWidth, photoHeight);
        if (image is null)
        {
            Text(sb, "F1", 8, photoX + photoWidth / 2d - 8d, photoY + photoHeight / 2d - 3d, "Anh");
        }

        var textX = x + HocVienCardLayout.MmToPoint(37d);
        var textTop = top - HocVienCardLayout.MmToPoint(6d);
        var maxChars = 30;

        Text(sb, "F1", 5.2, textX, textTop, "SO XAY DUNG TINH GIA LAI");
        Text(sb, "F1", 5.2, textX, textTop - 8, "TRUNG TAM DAO TAO LAI XE THANH CONG");
        Text(sb, "F2", 8.2, textX, textTop - 23, "HOC VIEN TAP LAI XE");
        Text(sb, "F2", 7.4, textX, textTop - 40, Fit(Sanitize(hocVien.HoVaTen), maxChars));
        Text(sb, "F1", 6.8, textX, textTop - 56, $"TAP LAI XE HANG: {Fit(Sanitize(hocVien.MaHangDT ?? hocVien.HangGplxHoc), 12)}");
        Text(sb, "F1", 5.8, textX, textTop - 71, Fit(Sanitize(hocVien.MaKhoa), maxChars));
    }

    private static IReadOnlyList<ImageRef> BuildImageRefs(
        IReadOnlyList<HocVienListItemDto> rows,
        int pageCount,
        IReadOnlyDictionary<int, HocVienPhotoPreviewDto>? photosByHocVienId)
    {
        if (photosByHocVienId is null || photosByHocVienId.Count == 0)
        {
            return [];
        }

        var refs = new List<ImageRef>();
        var nextObjectNumber = 3 + pageCount * 2;
        for (var index = 0; index < rows.Count; index++)
        {
            if (!photosByHocVienId.TryGetValue(rows[index].HocVienId, out var photo) ||
                photo.Content.Length == 0 ||
                photo.PixelWidth <= 0 ||
                photo.PixelHeight <= 0 ||
                !photo.ContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            refs.Add(new ImageRef(
                $"Im{refs.Count + 1}",
                nextObjectNumber++,
                index / HocVienCardLayout.CardsPerPage,
                index,
                photo));
        }

        return refs;
    }

    private static void DrawImage(
        StringBuilder sb,
        string imageName,
        double x,
        double y,
        double width,
        double height)
    {
        sb.AppendLine("q");
        sb.AppendLine($"{Format(width)} 0 0 {Format(height)} {Format(x)} {Format(y)} cm");
        sb.AppendLine($"/{imageName} Do");
        sb.AppendLine("Q");
    }

    private static void Rect(StringBuilder sb, double x, double y, double width, double height)
    {
        sb.AppendLine("0 0 0 RG");
        sb.AppendLine("0.8 w");
        sb.AppendLine($"{Format(x)} {Format(y)} {Format(width)} {Format(height)} re S");
    }

    private static void Text(
        StringBuilder sb,
        string font,
        double size,
        double x,
        double y,
        string? value)
    {
        sb.AppendLine("BT");
        sb.AppendLine($"/{font} {Format(size)} Tf");
        sb.AppendLine($"{Format(x)} {Format(y)} Td");
        sb.AppendLine($"({EscapePdfText(value ?? string.Empty)}) Tj");
        sb.AppendLine("ET");
    }

    private static string Fit(string? value, int maxChars)
    {
        var text = value ?? string.Empty;
        if (text.Length <= maxChars)
        {
            return text;
        }

        return text[..Math.Max(0, maxChars - 1)] + ".";
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim()
            .Replace('Đ', 'D')
            .Replace('đ', 'd');
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(c <= 126 ? c : ' ');
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string EscapePdfText(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal);

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void AddObject(List<byte[]> objects, string content)
        => objects.Add(Encoding.ASCII.GetBytes(content));

    private static void AddStreamObject(List<byte[]> objects, string content)
    {
        var streamBytes = Encoding.ASCII.GetBytes(content);
        var header = Encoding.ASCII.GetBytes($"<< /Length {streamBytes.Length} >>\nstream\n");
        var footer = Encoding.ASCII.GetBytes("\nendstream");
        var bytes = new byte[header.Length + streamBytes.Length + footer.Length];
        Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
        Buffer.BlockCopy(streamBytes, 0, bytes, header.Length, streamBytes.Length);
        Buffer.BlockCopy(footer, 0, bytes, header.Length + streamBytes.Length, footer.Length);
        objects.Add(bytes);
    }

    private static void AddJpegImageObject(List<byte[]> objects, HocVienPhotoPreviewDto photo)
    {
        var header = Encoding.ASCII.GetBytes(
            $"<< /Type /XObject /Subtype /Image /Width {photo.PixelWidth} /Height {photo.PixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {photo.Content.Length} >>\nstream\n");
        var footer = Encoding.ASCII.GetBytes("\nendstream");
        var bytes = new byte[header.Length + photo.Content.Length + footer.Length];
        Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
        Buffer.BlockCopy(photo.Content, 0, bytes, header.Length, photo.Content.Length);
        Buffer.BlockCopy(footer, 0, bytes, header.Length + photo.Content.Length, footer.Length);
        objects.Add(bytes);
    }

    private static byte[] BuildPdf(IReadOnlyList<byte[]> objects)
    {
        using var stream = new MemoryStream();
        WriteAscii(stream, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };

        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, $"{index + 1} 0 obj\n");
            stream.Write(objects[index]);
            WriteAscii(stream, "\nendobj\n");
        }

        var xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {objects.Count + 1}\n");
        WriteAscii(stream, "0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            WriteAscii(stream, $"{offset:0000000000} 00000 n \n");
        }

        WriteAscii(
            stream,
            $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return stream.ToArray();
    }

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes);
    }

    private sealed record ImageRef(
        string Name,
        int ObjectNumber,
        int PageIndex,
        int HocVienIndex,
        HocVienPhotoPreviewDto Photo);
}

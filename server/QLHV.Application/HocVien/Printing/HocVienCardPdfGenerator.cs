using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using QLHV.Application.HocVien.Dtos;

namespace QLHV.Application.HocVien.Printing;

public sealed class HocVienCardPdfGenerator : IHocVienCardPdfGenerator
{
    private static readonly object FontSettingsLock = new();
    private static bool _fontSettingsInitialized;

    private readonly HocVienCardTemplate _template;

    public HocVienCardPdfGenerator(HocVienCardTemplate template)
    {
        _template = template;
    }

    public byte[] CreatePdf(
        IReadOnlyList<HocVienListItemDto> hocViens,
        IReadOnlyDictionary<int, HocVienPhotoPreviewDto>? photosByHocVienId = null)
    {
        EnsureFontSettings();

        using var document = new PdfDocument();
        document.Info.Title = "Thẻ học viên tập lái xe";
        document.Info.Subject = "A4 ngang, 12 thẻ mỗi trang";
        document.Info.Creator = "QLHV_APP";

        var fonts = new FontCache(_template.FontFamily);
        var rows = hocViens.ToArray();
        var pageCount = HocVienCardLayout.GetPageCount(rows.Length);

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var page = document.AddPage();
            page.Width = Mm(HocVienCardLayout.PageWidthMm);
            page.Height = Mm(HocVienCardLayout.PageHeightMm);

            using var graphics = XGraphics.FromPdfPage(page);
            var start = pageIndex * HocVienCardLayout.CardsPerPage;
            var end = Math.Min(start + HocVienCardLayout.CardsPerPage, rows.Length);

            for (var studentIndex = start; studentIndex < end; studentIndex++)
            {
                var student = rows[studentIndex];
                var slot = HocVienCardLayout.GetSlot(studentIndex - start);
                HocVienPhotoPreviewDto? photo = null;
                photosByHocVienId?.TryGetValue(student.HocVienId, out photo);
                DrawCard(graphics, fonts, slot, student, photo);
            }
        }

        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private void DrawCard(
        XGraphics graphics,
        FontCache fonts,
        CardSlot slot,
        HocVienListItemDto student,
        HocVienPhotoPreviewDto? photo)
    {
        var cardRect = Rect(slot.XMm, slot.YMm, slot.WidthMm, slot.HeightMm);
        graphics.DrawRectangle(new XPen(XColors.Black, 0.65d), cardRect);

        var photoRect = Rect(
            slot.XMm + _template.PhotoRect.XMm,
            slot.YMm + _template.PhotoRect.YMm,
            _template.PhotoRect.WidthMm,
            _template.PhotoRect.HeightMm);

        if (!TryDrawPhoto(graphics, photoRect, photo))
        {
            DrawPhotoPlaceholder(graphics, fonts, photoRect);
        }

        graphics.DrawRectangle(new XPen(XColors.Black, 0.55d), photoRect);

        var content = _template.CreateContent(student);
        var textLeftMm = slot.XMm + _template.TextLeftMm;
        var textWidthMm = slot.WidthMm - _template.TextLeftMm - _template.TextRightPaddingMm;

        for (var index = 0; index < _template.TextLines.Count; index++)
        {
            var line = _template.TextLines[index];
            var text = content.GetText(line.Kind);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var nextTopMm = index + 1 < _template.TextLines.Count
                ? _template.TextLines[index + 1].TopMm
                : HocVienCardLayout.CardHeightMm - 3d;
            var lineHeightMm = Math.Max(3d, nextTopMm - line.TopMm);
            var lineRect = Rect(
                textLeftMm,
                slot.YMm + line.TopMm,
                textWidthMm,
                lineHeightMm);
            var fitted = FitText(graphics, fonts, text, line, lineRect.Width);

            graphics.DrawString(
                fitted.Text,
                fitted.Font,
                XBrushes.Black,
                lineRect,
                XStringFormats.CenterLeft);
        }
    }

    private static bool TryDrawPhoto(
        XGraphics graphics,
        XRect destination,
        HocVienPhotoPreviewDto? photo)
    {
        if (photo is null || photo.Content.Length == 0)
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(photo.Content, writable: false);
            using var image = XImage.FromStream(stream);
            DrawImageCover(graphics, image, destination);
            return true;
        }
        catch (Exception ex) when (
            ex is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    private static void DrawImageCover(XGraphics graphics, XImage image, XRect destination)
    {
        var sourceWidth = Math.Max(1d, image.PointWidth);
        var sourceHeight = Math.Max(1d, image.PointHeight);
        var destinationRatio = destination.Width / destination.Height;
        var sourceRatio = sourceWidth / sourceHeight;

        double sourceX = 0d;
        double sourceY = 0d;
        double cropWidth = sourceWidth;
        double cropHeight = sourceHeight;

        if (sourceRatio > destinationRatio)
        {
            cropWidth = sourceHeight * destinationRatio;
            sourceX = (sourceWidth - cropWidth) / 2d;
        }
        else if (sourceRatio < destinationRatio)
        {
            cropHeight = sourceWidth / destinationRatio;
            sourceY = (sourceHeight - cropHeight) / 2d;
        }

        graphics.DrawImage(
            image,
            destination,
            new XRect(sourceX, sourceY, cropWidth, cropHeight),
            XGraphicsUnit.Point);
    }

    private static void DrawPhotoPlaceholder(XGraphics graphics, FontCache fonts, XRect rect)
    {
        graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(244, 247, 250)), rect);
        var crossPen = new XPen(XColor.FromArgb(195, 205, 215), 0.4d);
        graphics.DrawLine(crossPen, rect.Left, rect.Top, rect.Right, rect.Bottom);
        graphics.DrawLine(crossPen, rect.Right, rect.Top, rect.Left, rect.Bottom);
        graphics.DrawString(
            "ẢNH",
            fonts.Get(7d, bold: true),
            new XSolidBrush(XColor.FromArgb(85, 100, 115)),
            rect,
            XStringFormats.Center);
    }

    private static FittedText FitText(
        XGraphics graphics,
        FontCache fonts,
        string text,
        CardTextLine line,
        double maximumWidth)
    {
        var size = line.PreferredFontSizePt;
        XFont font;
        do
        {
            font = fonts.Get(size, line.Bold);
            if (graphics.MeasureString(text, font).Width <= maximumWidth || size <= line.MinimumFontSizePt)
            {
                break;
            }

            size = Math.Max(line.MinimumFontSizePt, size - 0.2d);
        }
        while (true);

        return new FittedText(Ellipsize(graphics, text, font, maximumWidth), font);
    }

    private static string Ellipsize(XGraphics graphics, string text, XFont font, double maximumWidth)
    {
        if (graphics.MeasureString(text, font).Width <= maximumWidth)
        {
            return text;
        }

        const string suffix = "…";
        var length = text.Length;
        while (length > 0)
        {
            var candidate = text[..length].TrimEnd() + suffix;
            if (graphics.MeasureString(candidate, font).Width <= maximumWidth)
            {
                return candidate;
            }

            length--;
        }

        return suffix;
    }

    private static XRect Rect(double xMm, double yMm, double widthMm, double heightMm)
        => new(MmPoint(xMm), MmPoint(yMm), MmPoint(widthMm), MmPoint(heightMm));

    private static XUnit Mm(double value)
        => XUnit.FromPoint(MmPoint(value));

    private static double MmPoint(double value)
        => HocVienCardLayout.MmToPoint(value);

    private static void EnsureFontSettings()
    {
        if (_fontSettingsInitialized)
        {
            return;
        }

        lock (FontSettingsLock)
        {
            if (_fontSettingsInitialized)
            {
                return;
            }

            if (!OperatingSystem.IsWindows())
            {
                throw new InvalidOperationException(
                    "In thẻ hiện yêu cầu font Arial hệ thống Windows. Cần cấu hình font resolver khi triển khai trên hệ điều hành khác.");
            }

            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            _fontSettingsInitialized = true;
        }
    }

    private sealed class FontCache
    {
        private readonly string _fontFamily;
        private readonly Dictionary<(int SizeTenths, bool Bold), XFont> _fonts = [];
        private readonly XPdfFontOptions _options = new(
            PdfFontEncoding.Unicode,
            PdfFontEmbedding.EmbedCompleteFontFile);

        public FontCache(string fontFamily)
        {
            _fontFamily = fontFamily;
        }

        public XFont Get(double size, bool bold)
        {
            var sizeTenths = (int)Math.Round(size * 10d, MidpointRounding.AwayFromZero);
            var key = (sizeTenths, bold);
            if (_fonts.TryGetValue(key, out var font))
            {
                return font;
            }

            font = new XFont(
                _fontFamily,
                sizeTenths / 10d,
                bold ? XFontStyleEx.Bold : XFontStyleEx.Regular,
                _options);
            _fonts[key] = font;
            return font;
        }
    }

    private sealed record FittedText(string Text, XFont Font);
}

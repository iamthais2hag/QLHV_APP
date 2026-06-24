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
        IReadOnlyDictionary<int, HocVienPhotoPreviewDto>? photosByHocVienId = null,
        HocVienCardTitleOptions? titleOptions = null)
    {
        EnsureFontSettings();

        using var document = new PdfDocument();
        document.Info.Title = "Thẻ học viên tập lái xe";
        document.Info.Subject = "A4 ngang, 12 thẻ mỗi trang";
        document.Info.Creator = "QLHV_APP";

        var fonts = new FontCache();
        var textLines = _template.ResolveTextLines(titleOptions);
        var rows = hocViens.ToArray();
        var pageCount = HocVienCardLayout.GetPageCount(rows.Length);
        using var logoStream = HocVienCardLogo.OpenRead();
        using var logo = XImage.FromStream(logoStream);

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
                DrawCard(graphics, fonts, logo, slot, student, photo, titleOptions, textLines);
            }
        }

        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private void DrawCard(
        XGraphics graphics,
        FontCache fonts,
        XImage logo,
        CardSlot slot,
        HocVienListItemDto student,
        HocVienPhotoPreviewDto? photo,
        HocVienCardTitleOptions? titleOptions,
        IReadOnlyList<CardTextLine> textLines)
    {
        var photoRect = Rect(
            slot.XMm + _template.PhotoRect.XMm,
            slot.YMm + _template.PhotoRect.YMm,
            _template.PhotoRect.WidthMm,
            _template.PhotoRect.HeightMm);

        if (!TryDrawPhoto(graphics, photoRect, photo))
        {
            DrawPhotoPlaceholder(graphics, fonts, photoRect);
        }

        var logoOptions = titleOptions?.Logo ?? HocVienCardLogoOptions.Official;
        if (logoOptions.Header.Enabled)
        {
            var headerLogo = _template.ResolveHeaderLogoRect(logoOptions);
            var headerLogoRect = Rect(
                slot.XMm + headerLogo.XMm,
                slot.YMm + headerLogo.YMm,
                headerLogo.WidthMm,
                headerLogo.HeightMm);
            DrawCircularLogo(graphics, logo, headerLogoRect);
        }

        if (logoOptions.Watermark.Enabled)
        {
            var watermark = _template.ResolveBodyWatermarkRect(logoOptions);
            var watermarkRect = Rect(
                slot.XMm + watermark.XMm,
                slot.YMm + watermark.YMm,
                watermark.WidthMm,
                watermark.HeightMm);
            DrawCircularLogo(graphics, logo, watermarkRect);
            graphics.DrawEllipse(
                new XSolidBrush(XColor.FromArgb(220, 255, 255, 255)),
                watermarkRect);
        }

        var content = _template.CreateContent(student, titleOptions);
        foreach (var line in textLines)
        {
            var text = content.GetText(line.Kind);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var lineRect = Rect(
                slot.XMm + line.Bounds.XMm,
                slot.YMm + line.Bounds.YMm,
                line.Bounds.WidthMm,
                line.Bounds.HeightMm);
            var fitted = FitText(graphics, fonts, text, line, lineRect.Width);

            graphics.DrawString(
                fitted.Text,
                fitted.Font,
                XBrushes.Black,
                lineRect,
                XStringFormats.Center);
        }

        var separatorPen = new XPen(XColors.Black, 0.55d);
        var headerBottom = slot.YMm + _template.HeaderRect.HeightMm;
        var photoRight = slot.XMm + _template.PhotoRect.XMm + _template.PhotoRect.WidthMm;
        graphics.DrawLine(
            separatorPen,
            MmPoint(slot.XMm),
            MmPoint(headerBottom),
            MmPoint(slot.XMm + slot.WidthMm),
            MmPoint(headerBottom));
        graphics.DrawLine(
            separatorPen,
            MmPoint(photoRight),
            MmPoint(headerBottom),
            MmPoint(photoRight),
            MmPoint(slot.YMm + slot.HeightMm));

        var cardRect = Rect(slot.XMm, slot.YMm, slot.WidthMm, slot.HeightMm);
        graphics.DrawRectangle(new XPen(XColors.Black, 0.65d), cardRect);
    }

    private static void DrawCircularLogo(XGraphics graphics, XImage logo, XRect destination)
    {
        var state = graphics.Save();
        var clip = new XGraphicsPath();
        clip.AddEllipse(destination);
        graphics.IntersectClip(clip);

        var cropMargin = Math.Min(logo.PointWidth, logo.PointHeight) * 0.015d;
        graphics.DrawImage(
            logo,
            destination,
            new XRect(
                cropMargin,
                cropMargin,
                logo.PointWidth - 2d * cropMargin,
                logo.PointHeight - 2d * cropMargin),
            XGraphicsUnit.Point);
        graphics.Restore(state);
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

    private void DrawPhotoPlaceholder(XGraphics graphics, FontCache fonts, XRect rect)
    {
        graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(244, 247, 250)), rect);
        var lineHeight = rect.Height / Math.Max(1, _template.MissingPhotoPlaceholderLines.Count);
        for (var index = 0; index < _template.MissingPhotoPlaceholderLines.Count; index++)
        {
            graphics.DrawString(
                _template.MissingPhotoPlaceholderLines[index],
                fonts.Get(_template.FontFamily, 8.5d, bold: index == 0, italic: false),
                new XSolidBrush(XColor.FromArgb(85, 100, 115)),
                new XRect(rect.X, rect.Y + index * lineHeight, rect.Width, lineHeight),
                XStringFormats.Center);
        }
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
            font = fonts.Get(line.FontFamily, size, line.Bold, line.Italic);
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
                    "In thẻ hiện yêu cầu font Times New Roman hệ thống Windows. Cần cấu hình font resolver khi triển khai trên hệ điều hành khác.");
            }

            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            _fontSettingsInitialized = true;
        }
    }

    private sealed class FontCache
    {
        private readonly Dictionary<(string FontFamily, int SizeTenths, bool Bold, bool Italic), XFont> _fonts = [];
        private readonly XPdfFontOptions _options = new(
            PdfFontEncoding.Unicode,
            PdfFontEmbedding.EmbedCompleteFontFile);

        public XFont Get(string fontFamily, double size, bool bold, bool italic)
        {
            var sizeTenths = (int)Math.Round(size * 10d, MidpointRounding.AwayFromZero);
            var key = (fontFamily, sizeTenths, bold, italic);
            if (_fonts.TryGetValue(key, out var font))
            {
                return font;
            }

            var style = (bold, italic) switch
            {
                (true, true) => XFontStyleEx.BoldItalic,
                (true, false) => XFontStyleEx.Bold,
                (false, true) => XFontStyleEx.Italic,
                _ => XFontStyleEx.Regular,
            };
            font = new XFont(fontFamily, sizeTenths / 10d, style, _options);
            _fonts[key] = font;
            return font;
        }
    }

    private sealed record FittedText(string Text, XFont Font);
}

using System.IO.Compression;
using ClosedXML.Excel;
using QLHV.Application.Assignments;

namespace QLHV.Tests.Assignments;

public sealed class AssignmentExcelTests
{
    private static readonly string[] ApprovedExportHeaders =
    [
        "STT",
        "Mã đăng ký",
        "Họ và tên",
        "Ngày sinh",
        "Giới tính",
        "Số CCCD",
        "Địa chỉ thường trú",
        "Hạng học",
        "Mã hạng học",
        "Số GPLX đã có",
        "Hạng GPLX đã có",
        "Người nhận hồ sơ",
        "Tên khóa",
        "Mã khóa",
        "Giáo viên đứng lớp",
        "Xe tập lái",
        "Xe bài số 10",
        "Mã giáo viên hồ sơ",
    ];

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Export_has_exact_approved_18_columns_and_preserves_text_and_date_types()
    {
        var file = AssignmentExcel.CreateExport(SampleData());

        using var workbook = Open(file.Content);
        var sheet = workbook.Worksheet("PhanCongHocVien");

        Assert.Equal(18, AssignmentExcel.ExportHeaders.Length);
        Assert.Equal(ApprovedExportHeaders, AssignmentExcel.ExportHeaders);
        Assert.Equal(
            ApprovedExportHeaders,
            Enumerable.Range(1, 18).Select(column => sheet.Cell(1, column).GetString()).ToArray());

        AssertText(sheet.Cell(2, 2), "000-DK-01");
        AssertText(sheet.Cell(2, 6), "001234567890");
        AssertText(sheet.Cell(2, 9), "B2");
        AssertText(sheet.Cell(2, 10), "000012345678");
        AssertText(sheet.Cell(2, 14), "OTO-K001");
        AssertText(sheet.Cell(2, 18), "HS-001");
        Assert.Equal(XLDataType.DateTime, sheet.Cell(2, 4).DataType);
        Assert.Equal(new DateTime(1992, 4, 3), sheet.Cell(2, 4).GetDateTime());
        Assert.Equal("dd/MM/yyyy", sheet.Cell(2, 4).Style.DateFormat.Format);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Export_neutralizes_formula_prefixes_in_every_user_controlled_text_cell()
    {
        var row = SampleData().Rows.Single() with
        {
            RegistrationCode = "=HYPERLINK(\"https://invalid\")",
            FullName = "+cmd|' /C calc'!A0",
            CitizenId = "-001234567890",
            PermanentAddress = " @SUM(1,1)",
        };
        var data = SampleData() with { Rows = [row] };

        using var workbook = Open(AssignmentExcel.CreateExport(data).Content);
        var sheet = workbook.Worksheet("PhanCongHocVien");

        AssertNeutralized(sheet.Cell(2, 2), "'=HYPERLINK(\"https://invalid\")");
        AssertNeutralized(sheet.Cell(2, 3), "'+cmd|' /C calc'!A0");
        AssertNeutralized(sheet.Cell(2, 6), "'-001234567890");
        AssertNeutralized(sheet.Cell(2, 7), "' @SUM(1,1)");
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Export_and_template_seal_course_profile_row_identity_and_lookups_in_very_hidden_sheet()
    {
        var data = SampleData();
        foreach (var content in new[]
                 {
                     AssignmentExcel.CreateExport(data).Content,
                     AssignmentExcel.CreateTemplate(data).Content,
                 })
        {
            using var workbook = Open(content);
            var technical = workbook.Worksheet(AssignmentExcel.TechnicalSheetName);

            Assert.Equal(XLWorksheetVisibility.VeryHidden, technical.Visibility);
            Assert.Equal("TemplateVersion", technical.Cell("A1").GetString());
            Assert.Equal(AssignmentExcel.TemplateVersion, technical.Cell("B1").GetString());
            Assert.Equal("NormalizationVersion", technical.Cell("A2").GetString());
            Assert.Equal(AssignmentExcel.NormalizationVersion, technical.Cell("B2").GetString());
            Assert.Equal("KhoaHocId", technical.Cell("A3").GetString());
            Assert.Equal("42", technical.Cell("B3").GetString());
            Assert.Equal("SourceProfileCode", technical.Cell("A4").GetString());
            Assert.Equal("CSDT_OTO", technical.Cell("B4").GetString());
            Assert.Equal("501", technical.Cell("A7").GetString());
            Assert.Equal("000-DK-01", technical.Cell("B7").GetString());
            Assert.Equal("AQIDBAUGBwg=", technical.Cell("C7").GetString());
            Assert.Equal("GIAO_VIEN_HO_SO", technical.Cell(1, 5).GetString());
            Assert.Equal("GIAO_VIEN_DUNG_LOP", technical.Cell(1, 8).GetString());
            Assert.Equal("XE_TAP", technical.Cell(1, 11).GetString());
            Assert.Equal("NHOM", technical.Cell(1, 14).GetString());
        }
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Template_prefills_exact_identity_and_uses_keep_for_blank_semantics()
    {
        var file = AssignmentExcel.CreateTemplate(SampleData());

        using (var workbook = Open(file.Content))
        {
            var sheet = workbook.Worksheet("NhapPhanCong");
            Assert.Equal(AssignmentExcel.ImportHeaders,
                Enumerable.Range(1, AssignmentExcel.ImportHeaders.Length)
                    .Select(column => sheet.Cell(1, column).GetString()).ToArray());
            AssertText(sheet.Cell(2, 1), "000-DK-01");
            AssertText(sheet.Cell(2, 2), "OTO-K001");
            AssertText(sheet.Cell(2, 8), "501");
            AssertText(sheet.Cell(2, 9), "AQIDBAUGBwg=");
            foreach (var column in Enumerable.Range(10, 5))
            {
                AssertText(sheet.Cell(2, column), AssignmentAction.Keep);
            }
        }

        var parsed = AssignmentExcel.Parse(file.Content);
        Assert.Equal(42, parsed.TechnicalCourseId);
        Assert.Equal("CSDT_OTO", parsed.TechnicalSourceProfileCode);
        var parsedRow = Assert.Single(parsed.Rows);
        Assert.Equal(501, parsedRow.HocVienId);
        Assert.All(new[]
        {
            parsedRow.GroupAction,
            parsedRow.DossierReceiverAction,
            parsedRow.ClassTeacherAction,
            parsedRow.TrainingVehicleAction,
            parsedRow.Figure10VehicleAction,
        }, action => Assert.Equal(AssignmentAction.Keep, action));
        Assert.Empty(parsedRow.ValidationMessages);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Import_blank_actions_mean_keep_and_formula_key_cells_are_invalid()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("NhapPhanCong");
        WriteRequiredHeaders(sheet);
        sheet.Cell(2, 1).FormulaA1 = "1+1";
        sheet.Cell(2, 2).SetValue("OTO-K001");
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var row = Assert.Single(AssignmentExcel.Parse(stream.ToArray()).Rows);

        Assert.All(new[]
        {
            row.GroupAction,
            row.DossierReceiverAction,
            row.ClassTeacherAction,
            row.TrainingVehicleAction,
            row.Figure10VehicleAction,
        }, action => Assert.Equal(AssignmentAction.Keep, action));
        Assert.Contains(row.ValidationMessages,
            message => message.Contains("MaDangKy", StringComparison.Ordinal) &&
                       message.Contains("công thức", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Import_rejects_external_relationships_before_workbook_parsing()
    {
        var bytes = CreateImportWorkbook(1);
        using (var stream = new MemoryStream())
        {
            stream.Write(bytes);
            stream.Position = 0;
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
            {
                var entry = archive.CreateEntry("xl/externalLinks/externalLink1.xml");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("<externalLink />");
            }

            bytes = stream.ToArray();
        }

        var exception = Assert.Throws<AssignmentDomainException>(() => AssignmentExcel.Parse(bytes));

        Assert.Equal("INVALID", exception.Code);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Import_rejects_macro_vba_and_embedded_object_parts()
    {
        var valid = CreateImportWorkbook(1);
        foreach (var entryName in new[]
                 {
                     "xl/vbaProject.bin",
                     "xl/embeddings/embeddedObject1.bin",
                     "xl/worksheets/oleObject1.bin",
                 })
        {
            AssertInvalid(AddArchiveEntry(valid, entryName, [0x01], CompressionLevel.NoCompression));
        }
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Import_rejects_relationship_parts_larger_than_two_mebibytes()
    {
        var content = AddArchiveEntry(
            CreateImportWorkbook(1),
            "custom/oversized.rels",
            new byte[2 * 1024 * 1024 + 1],
            CompressionLevel.NoCompression);

        AssertInvalid(content);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Import_rejects_high_compression_ratio_zip_bomb_entries()
    {
        var content = AddArchiveEntry(
            CreateImportWorkbook(1),
            "custom/high-ratio.bin",
            new byte[1024 * 1024],
            CompressionLevel.Optimal);

        AssertInvalid(content);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Import_accepts_exactly_500_archive_entries_and_rejects_501()
    {
        var valid = CreateImportWorkbook(1);
        var atLimit = WithArchiveEntryCount(valid, 500);
        Assert.Equal(500, ArchiveEntryCount(atLimit));
        Assert.Single(AssignmentExcel.Parse(atLimit).Rows);

        var overLimit = WithArchiveEntryCount(valid, 501);
        Assert.Equal(501, ArchiveEntryCount(overLimit));
        AssertInvalid(overLimit);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Import_file_size_guard_accepts_exact_limit_and_rejects_empty_or_one_byte_over()
    {
        var atLimit = PadPackageToLength(
            CreateImportWorkbook(1),
            checked((int)AssignmentRules.MaxImportBytes));
        Assert.Equal(AssignmentRules.MaxImportBytes, atLimit.Length);
        Assert.Single(AssignmentExcel.Parse(atLimit).Rows);

        AssertInvalid([]);
        var overLimit = new byte[AssignmentRules.MaxImportBytes + 1];
        Array.Copy(atLimit, overLimit, atLimit.Length);
        AssertInvalid(overLimit);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Import_requires_exact_positive_course_and_profile_metadata_in_A1_through_B4()
    {
        var template = AssignmentExcel.CreateTemplate(SampleData()).Content;
        var invalidMutations = new Action<IXLWorksheet>[]
        {
            sheet => sheet.Cell("A1").SetValue("templateversion"),
            sheet => sheet.Cell("B1").SetValue("HOCVIEN_ASSIGNMENT_V1"),
            sheet => sheet.Cell("A2").SetValue("Normalization"),
            sheet => sheet.Cell("B2").SetValue("V1"),
            sheet => sheet.Cell("A3").SetValue("CourseId"),
            sheet => sheet.Cell("B3").SetValue("0"),
            sheet => sheet.Cell("B3").SetValue("not-a-course-id"),
            sheet => sheet.Cell("A4").SetValue("Profile"),
            sheet => sheet.Cell("B4").SetValue("OTO"),
            sheet => sheet.Cell("B4").SetValue("CSDT_UNKNOWN"),
        };

        foreach (var mutate in invalidMutations)
        {
            var exception = Assert.Throws<AssignmentDomainException>(() =>
                AssignmentExcel.Parse(MutateTechnicalSheet(template, mutate)));
            Assert.Equal("INVALID", exception.Code);
        }
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Malformed_xlsx_is_mapped_to_invalid_domain_error()
    {
        var exception = Assert.Throws<AssignmentDomainException>(() =>
            AssignmentExcel.Parse([0x01, 0x02, 0x03, 0x04]));

        Assert.Equal("INVALID", exception.Code);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Import_accepts_exactly_5000_rows_and_rejects_5001_rows()
    {
        var atLimit = AssignmentExcel.Parse(CreateImportWorkbook(AssignmentRules.MaxImportRows));
        Assert.Equal(AssignmentRules.MaxImportRows, atLimit.Rows.Count);

        var exception = Assert.Throws<AssignmentDomainException>(() =>
            AssignmentExcel.Parse(CreateImportWorkbook(AssignmentRules.MaxImportRows + 1)));
        Assert.Equal("INVALID", exception.Code);
    }

    private static AssignmentExportData SampleData()
    {
        var row = new AssignmentExportRow(
            501,
            "CSDT_OTO",
            "000-DK-01",
            "Nguyễn Văn A",
            new DateOnly(1992, 4, 3),
            "Nam",
            "001234567890",
            "Số 1 Đường A",
            "Hạng B2",
            "B2",
            "000012345678",
            "B1",
            "Người nhận A",
            "Khóa OTO 001",
            "OTO-K001",
            "Giáo viên A",
            "51A00001",
            "51A00002",
            "HS-001",
            "N1",
            "GV01",
            701,
            "AQIDBAUGBwg=");
        var lookups = new AssignmentLookups(
            [new LookupRef(11, "HS-001", "Người nhận A", true)],
            [new LookupRef(12, "GV01", "Giáo viên A", true, false, "CSDT_OTO")],
            [new LookupRef(13, "51A00001", "51A-000.01", true, false, "CSDT_OTO")]);
        var groups = new[]
        {
            new TrainingGroupItem(
                91, "N1", "Nhóm 1", 1, "ACTIVE", true,
                lookups.Teachers[0], lookups.Vehicles[0], null, 1, "AQIDBAUGBwg="),
        };
        return new AssignmentExportData(42, "OTO-K001", "CSDT_OTO", [row], lookups, groups);
    }

    private static byte[] CreateImportWorkbook(int rowCount)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("NhapPhanCong");
        WriteRequiredHeaders(sheet);
        for (var index = 0; index < rowCount; index++)
        {
            var row = index + 2;
            sheet.Cell(row, 1).SetValue($"DK-{index:D5}");
            sheet.Cell(row, 2).SetValue("OTO-K001");
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] MutateTechnicalSheet(byte[] content, Action<IXLWorksheet> mutate)
    {
        using var workbook = Open(content);
        mutate(workbook.Worksheet(AssignmentExcel.TechnicalSheetName));
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] AddArchiveEntry(
        byte[] content,
        string entryName,
        byte[] payload,
        CompressionLevel compressionLevel)
    {
        using var stream = new MemoryStream();
        stream.Write(content);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName, compressionLevel);
            using var entryStream = entry.Open();
            entryStream.Write(payload);
        }

        return stream.ToArray();
    }

    private static byte[] WithArchiveEntryCount(byte[] content, int targetCount)
    {
        using var stream = new MemoryStream();
        stream.Write(content);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            if (archive.Entries.Count > targetCount)
            {
                throw new InvalidOperationException("Target entry count is below the base package count.");
            }

            for (var index = archive.Entries.Count; index < targetCount; index++)
            {
                archive.CreateEntry($"custom/entry-{index:D4}.bin", CompressionLevel.NoCompression);
            }
        }

        return stream.ToArray();
    }

    private static int ArchiveEntryCount(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return archive.Entries.Count;
    }

    private static byte[] PadPackageToLength(byte[] content, int targetLength)
    {
        const string entryName = "custom/size-boundary-padding.bin";
        var probe = AddArchiveEntry(content, entryName, [], CompressionLevel.NoCompression);
        var payloadLength = targetLength - probe.Length;
        if (payloadLength < 0)
        {
            throw new InvalidOperationException("Base workbook exceeds the requested package length.");
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = AddArchiveEntry(
                content,
                entryName,
                new byte[payloadLength],
                CompressionLevel.NoCompression);
            if (result.Length == targetLength)
            {
                return result;
            }

            payloadLength += targetLength - result.Length;
            if (payloadLength < 0)
            {
                break;
            }
        }

        throw new InvalidOperationException("Could not build an XLSX package at the exact byte limit.");
    }

    private static void AssertInvalid(byte[] content)
    {
        var exception = Assert.Throws<AssignmentDomainException>(() => AssignmentExcel.Parse(content));
        Assert.Equal("INVALID", exception.Code);
    }

    private static void WriteRequiredHeaders(IXLWorksheet sheet)
    {
        for (var index = 0; index < 7; index++)
        {
            sheet.Cell(1, index + 1).SetValue(AssignmentExcel.ImportHeaders[index]);
        }
    }

    private static XLWorkbook Open(byte[] content) => new(new MemoryStream(content));

    private static void AssertText(IXLCell cell, string expected)
    {
        Assert.Equal(expected, cell.GetString());
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.Equal("@", cell.Style.NumberFormat.Format);
    }

    private static void AssertNeutralized(IXLCell cell, string expected)
    {
        Assert.False(cell.HasFormula);
        Assert.StartsWith("'", expected, StringComparison.Ordinal);
        AssertText(cell, expected[1..]);
        Assert.True(cell.Style.IncludeQuotePrefix);
    }
}

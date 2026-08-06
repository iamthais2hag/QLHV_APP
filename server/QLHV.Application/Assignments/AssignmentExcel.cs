using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;

namespace QLHV.Application.Assignments;

public sealed record ParsedAssignmentWorkbook(
    string Sha256,
    long? TechnicalCourseId,
    string? TechnicalSourceProfileCode,
    IReadOnlyList<ParsedAssignmentImportRow> Rows);

public static class AssignmentExcel
{
    public const string TemplateVersion = "HOCVIEN_ASSIGNMENT_V2";
    public const string NormalizationVersion = "V2";
    public const string TechnicalSheetName = "_QLHV_TECH";

    public static readonly string[] ExportHeaders =
    [
        "STT", "Mã đăng ký", "Họ và tên", "Ngày sinh", "Giới tính", "Số CCCD",
        "Địa chỉ thường trú", "Hạng học", "Mã hạng học", "Số GPLX đã có",
        "Hạng GPLX đã có", "Người nhận hồ sơ", "Tên khóa", "Mã khóa",
        "Giáo viên đứng lớp", "Xe tập lái", "Xe bài số 10", "Mã giáo viên hồ sơ",
    ];

    public static readonly string[] ImportHeaders =
    [
        "MaDangKy", "MaKhoa", "MaNhom", "MaGiaoVienHoSo", "MaGiaoVienDungLop",
        "BienSoXeTap", "BienSoXeBaiSo10", "HocVienId", "AssignmentRowVersion",
        "ActionNhom", "ActionGiaoVienHoSo", "ActionGiaoVienDungLop",
        "ActionXeTap", "ActionXeBaiSo10",
    ];

    private static readonly string[] RequiredImportHeaders = ImportHeaders[..7];

    public static AssignmentExportFile CreateExport(AssignmentExportData data, string suffix = "phan-cong")
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("PhanCongHocVien");
        WriteHeader(sheet, ExportHeaders);
        for (var index = 0; index < data.Rows.Count; index++)
        {
            var row = data.Rows[index];
            var excelRow = index + 2;
            sheet.Cell(excelRow,1).SetValue(index+1);
            SetText(sheet.Cell(excelRow,2),row.RegistrationCode);
            SetText(sheet.Cell(excelRow,3),row.FullName);
            if(row.DateOfBirth.HasValue)
            {
                sheet.Cell(excelRow,4).SetValue(row.DateOfBirth.Value.ToDateTime(TimeOnly.MinValue));
                sheet.Cell(excelRow,4).Style.DateFormat.Format="dd/MM/yyyy";
            }
            SetText(sheet.Cell(excelRow,5),row.Gender);
            SetText(sheet.Cell(excelRow,6),row.CitizenId);
            SetText(sheet.Cell(excelRow,7),row.PermanentAddress);
            SetText(sheet.Cell(excelRow,8),row.TrainingClass);
            SetText(sheet.Cell(excelRow,9),row.TrainingClassCode);
            SetText(sheet.Cell(excelRow,10),row.ExistingLicenseNumber);
            SetText(sheet.Cell(excelRow,11),row.ExistingLicenseClass);
            SetText(sheet.Cell(excelRow,12),row.DossierReceiverName);
            SetText(sheet.Cell(excelRow,13),row.CourseName);
            SetText(sheet.Cell(excelRow,14),row.CourseCode);
            SetText(sheet.Cell(excelRow,15),row.ClassTeacherName);
            SetText(sheet.Cell(excelRow,16),row.TrainingVehiclePlate);
            SetText(sheet.Cell(excelRow,17),row.Figure10VehiclePlate);
            SetText(sheet.Cell(excelRow,18),row.DossierReceiverCode);
        }
        FormatVisibleSheet(sheet,ExportHeaders.Length,data.Rows.Count+1);
        WriteTechnicalSheet(workbook,data,includeRowIdentity:true);
        return Save(workbook,$"KhoaHoc_{SafeFileName(data.CourseCode)}_{suffix}_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
    }

    public static AssignmentExportFile CreateTemplate(AssignmentExportData data)
    {
        using var workbook=new XLWorkbook();
        var sheet=workbook.AddWorksheet("NhapPhanCong");
        WriteHeader(sheet,ImportHeaders);
        for(var index=0;index<data.Rows.Count;index++)
        {
            var source=data.Rows[index]; var row=index+2;
            SetText(sheet.Cell(row,1),source.RegistrationCode); SetText(sheet.Cell(row,2),source.CourseCode);
            SetText(sheet.Cell(row,3),source.GroupCode); SetText(sheet.Cell(row,4),source.DossierReceiverCode);
            SetText(sheet.Cell(row,5),source.ClassTeacherCode); SetText(sheet.Cell(row,6),source.TrainingVehiclePlate);
            SetText(sheet.Cell(row,7),source.Figure10VehiclePlate);
            SetText(sheet.Cell(row,8),source.HocVienId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SetText(sheet.Cell(row,9),source.AssignmentRowVersion);
            for(var column=10;column<=14;column++) SetText(sheet.Cell(row,column),AssignmentAction.Keep);
        }
        var sampleRows=Math.Max(100,Math.Min(AssignmentRules.MaxImportRows,data.Rows.Count+50));
        for(var row=2;row<=sampleRows+1;row++)
        {
            foreach(var column in Enumerable.Range(1,ImportHeaders.Length))
                sheet.Cell(row,column).Style.NumberFormat.Format="@";
        }
        FormatVisibleSheet(sheet,ImportHeaders.Length,Math.Max(2,data.Rows.Count+1));
        WriteTechnicalSheet(workbook,data,includeRowIdentity:true);
        return Save(workbook,$"MauPhanCong_{SafeFileName(data.CourseCode)}.xlsx");
    }

    public static ParsedAssignmentWorkbook Parse(byte[] content)
    {
        try
        {
            return ParseCore(content);
        }
        catch(AssignmentDomainException)
        {
            throw;
        }
        catch(Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            throw new AssignmentDomainException(
                "INVALID",
                "File XLSX bị lỗi hoặc không đúng định dạng được hỗ trợ.");
        }
    }

    private static ParsedAssignmentWorkbook ParseCore(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if(content.LongLength is <=0 or >AssignmentRules.MaxImportBytes)
            throw new AssignmentDomainException("INVALID",$"File phải có dung lượng 1-{AssignmentRules.MaxImportBytes:N0} byte.");
        ValidatePackage(content);
        using var stream=new MemoryStream(content,writable:false);
        using var workbook=new XLWorkbook(stream);
        var visible=workbook.Worksheets.Where(sheet=>sheet.Visibility==XLWorksheetVisibility.Visible).ToArray();
        if(visible.Length!=1)
            throw new AssignmentDomainException("INVALID","Workbook phải có đúng một sheet hiển thị.");
        var sheet=visible[0];
        var lastColumn=sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if(lastColumn>100)
            throw new AssignmentDomainException("INVALID","Workbook có quá nhiều cột.");
        var headers=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        for(var column=1;column<=lastColumn;column++)
        {
            var header=sheet.Cell(1,column).GetString().Trim();
            if(header.Length==0) continue;
            if(!headers.TryAdd(header,column))
                throw new AssignmentDomainException("INVALID",$"Header bị lặp: {header}.");
        }
        foreach(var required in RequiredImportHeaders)
            if(!headers.ContainsKey(required))
                throw new AssignmentDomainException("INVALID",$"Thiếu header bắt buộc: {required}.");
        var lastRow=sheet.LastRowUsed()?.RowNumber() ?? 1;
        if(lastRow-1>AssignmentRules.MaxImportRows)
            throw new AssignmentDomainException("INVALID",$"File vượt quá {AssignmentRules.MaxImportRows:N0} dòng.");
        var rows=new List<ParsedAssignmentImportRow>();
        for(var rowNumber=2;rowNumber<=lastRow;rowNumber++)
        {
            if(headers.Values.All(column=>sheet.Cell(rowNumber,column).IsEmpty())) continue;
            var messages=new List<string>();
            string Read(string name,int max=1000,bool key=false)
            {
                if(!headers.TryGetValue(name,out var column)) return string.Empty;
                var cell=sheet.Cell(rowNumber,column);
                if(key && cell.HasFormula) messages.Add($"{name}: không cho phép công thức.");
                var value=cell.GetFormattedString().Trim();
                if(value.Length>max) messages.Add($"{name}: vượt quá {max} ký tự.");
                if(key && AssignmentRules.IsFormula(value)) messages.Add($"{name}: ký tự đầu có nguy cơ công thức.");
                return value;
            }
            string Normalized(string name,int max,bool key=true)
            {
                var raw=Read(name,max,key);
                try { return AssignmentRules.NormalizeOptional(raw,max) ?? string.Empty; }
                catch(AssignmentDomainException ex) { messages.Add($"{name}: {ex.Message}"); return raw; }
            }
            var registration=Normalized("MaDangKy",50);
            var courseCode=Normalized("MaKhoa",50);
            if(registration.Length==0) messages.Add("MaDangKy là bắt buộc.");
            if(courseCode.Length==0) messages.Add("MaKhoa là bắt buộc.");
            var hocVienText=Normalized("HocVienId",30);
            long? hocVienId=null;
            if(hocVienText.Length>0 && (!long.TryParse(hocVienText,out var parsedId) || parsedId<=0))
                messages.Add("HocVienId không hợp lệ.");
            else if(hocVienText.Length>0) hocVienId=long.Parse(hocVienText,System.Globalization.CultureInfo.InvariantCulture);
            string Action(string name,bool inheritAllowed)
            {
                var value=Read(name,30,true).ToUpperInvariant();
                if(value.Length==0) value=AssignmentAction.Keep;
                if(!AssignmentAction.IsValid(value,inheritAllowed)) messages.Add($"{name}: action không hợp lệ.");
                return value;
            }
            rows.Add(new ParsedAssignmentImportRow(
                rowNumber,registration,courseCode,NullIfBlank(Normalized("MaNhom",50)),
                NullIfBlank(Normalized("MaGiaoVienHoSo",50)),NullIfBlank(Normalized("MaGiaoVienDungLop",20)),
                NullIfBlank(Normalized("BienSoXeTap",20)),NullIfBlank(Normalized("BienSoXeBaiSo10",20)),
                hocVienId,NullIfBlank(Normalized("AssignmentRowVersion",100)),Action("ActionNhom",false),
                Action("ActionGiaoVienHoSo",false),Action("ActionGiaoVienDungLop",true),
                Action("ActionXeTap",true),Action("ActionXeBaiSo10",true),messages.Distinct().ToArray()));
        }
        if(rows.Count==0) throw new AssignmentDomainException("INVALID","File không có dòng dữ liệu.");
        long? technicalCourseId=null;
        string? technicalProfile=null;
        if(workbook.TryGetWorksheet(TechnicalSheetName,out var technical))
        {
            if(!string.Equals(technical.Cell("A1").GetString(),"TemplateVersion",StringComparison.Ordinal) ||
               !string.Equals(technical.Cell("A2").GetString(),"NormalizationVersion",StringComparison.Ordinal) ||
               !string.Equals(technical.Cell("B1").GetString(),TemplateVersion,StringComparison.Ordinal) ||
               !string.Equals(technical.Cell("B2").GetString(),NormalizationVersion,StringComparison.Ordinal))
                throw new AssignmentDomainException(
                    "INVALID","Metadata kỹ thuật của template không hợp lệ.");
            if(!string.Equals(technical.Cell("A3").GetString(),"KhoaHocId",StringComparison.Ordinal) ||
               !string.Equals(technical.Cell("A4").GetString(),"SourceProfileCode",StringComparison.Ordinal) ||
               !long.TryParse(technical.Cell("B3").GetString(),out var parsedCourseId) ||
               parsedCourseId<=0)
                throw new AssignmentDomainException(
                    "INVALID","Phạm vi kỹ thuật của template không hợp lệ.");
            if(technical.Cell("A1").GetString()=="TemplateVersion" &&
               technical.Cell("B1").GetString()!=TemplateVersion)
                throw new AssignmentDomainException("INVALID","TemplateVersion không được hỗ trợ.");
            technicalCourseId=parsedCourseId;
            technicalProfile=AssignmentRules.NormalizeProfile(
                technical.Cell("B4").GetString(),required:true);
        }
        return new ParsedAssignmentWorkbook(Convert.ToHexString(SHA256.HashData(content)),technicalCourseId,technicalProfile,rows);
    }

    private static void ValidatePackage(byte[] content)
    {
        using var stream=new MemoryStream(content,writable:false);
        using var archive=new ZipArchive(stream,ZipArchiveMode.Read,leaveOpen:false);
        if(archive.Entries.Count is 0 or >500)
            throw new AssignmentDomainException("INVALID","Cấu trúc XLSX không hợp lệ.");
        long total=0;
        foreach(var entry in archive.Entries)
        {
            total=checked(total+entry.Length);
            if(total>50L*1024*1024)
                throw new AssignmentDomainException("INVALID","XLSX giải nén vượt giới hạn an toàn.");
            var name=entry.FullName.Replace('\\','/');
            if(name.Contains("../",StringComparison.Ordinal) || name.StartsWith("/",StringComparison.Ordinal) ||
               name.Contains("vbaProject",StringComparison.OrdinalIgnoreCase) ||
               name.Contains("externalLinks/",StringComparison.OrdinalIgnoreCase) ||
               name.Contains("embeddings/",StringComparison.OrdinalIgnoreCase) ||
               name.Contains("oleObject",StringComparison.OrdinalIgnoreCase))
                throw new AssignmentDomainException("INVALID","XLSX chứa liên kết, macro hoặc đối tượng nhúng không được phép.");
            if(entry.CompressedLength>0 && entry.Length/Math.Max(1,entry.CompressedLength)>200)
                throw new AssignmentDomainException("INVALID","XLSX có tỷ lệ nén bất thường.");
            if(name.EndsWith(".rels",StringComparison.OrdinalIgnoreCase))
            {
                if(entry.Length>2*1024*1024)
                    throw new AssignmentDomainException("INVALID","XLSX chứa relationship quá lớn để kiểm tra an toàn.");
                using var reader=new StreamReader(entry.Open(),Encoding.UTF8,detectEncodingFromByteOrderMarks:true,leaveOpen:false);
                var xml=reader.ReadToEnd();
                if(xml.Contains("TargetMode=\"External\"",StringComparison.OrdinalIgnoreCase))
                    throw new AssignmentDomainException("INVALID","XLSX chứa external relationship.");
            }
        }
    }

    private static void WriteTechnicalSheet(XLWorkbook workbook,AssignmentExportData data,bool includeRowIdentity)
    {
        var sheet=workbook.AddWorksheet(TechnicalSheetName);
        sheet.Cell("A1").Value="TemplateVersion"; sheet.Cell("B1").Value=TemplateVersion;
        sheet.Cell("A2").Value="NormalizationVersion"; sheet.Cell("B2").Value=NormalizationVersion;
        sheet.Cell("A3").Value="KhoaHocId"; sheet.Cell("B3").SetValue(data.CourseId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sheet.Cell("A4").Value="SourceProfileCode"; sheet.Cell("B4").SetValue(data.SourceProfileCode);
        sheet.Cell("A6").Value="HocVienId"; sheet.Cell("B6").Value="MaDangKy"; sheet.Cell("C6").Value="AssignmentRowVersion";
        if(includeRowIdentity)
        {
            for(var index=0;index<data.Rows.Count;index++)
            {
                var row=data.Rows[index]; var excelRow=index+7;
                SetText(sheet.Cell(excelRow,1),row.HocVienId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                SetText(sheet.Cell(excelRow,2),row.RegistrationCode); SetText(sheet.Cell(excelRow,3),row.AssignmentRowVersion);
            }
        }
        var catalogColumn=5;
        WriteLookupSection(sheet,catalogColumn,"GIAO_VIEN_HO_SO",data.Lookups.DossierReceivers);
        WriteLookupSection(sheet,catalogColumn+3,"GIAO_VIEN_DUNG_LOP",data.Lookups.Teachers);
        WriteLookupSection(sheet,catalogColumn+6,"XE_TAP",data.Lookups.Vehicles);
        WriteLookupSection(sheet,catalogColumn+9,"NHOM",data.Groups.Select(group=>
            new LookupRef(group.GroupId,group.MaNhom,group.TenNhom,group.IsActive)).ToArray());
        sheet.Visibility=XLWorksheetVisibility.VeryHidden;
    }

    private static void WriteLookupSection(IXLWorksheet sheet,int startColumn,string title,IReadOnlyList<LookupRef> values)
    {
        sheet.Cell(1,startColumn).Value=title; sheet.Cell(2,startColumn).Value="Id";
        sheet.Cell(2,startColumn+1).Value="Code"; sheet.Cell(2,startColumn+2).Value="Label";
        for(var index=0;index<values.Count;index++)
        {
            var row=index+3; SetText(sheet.Cell(row,startColumn),values[index].Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SetText(sheet.Cell(row,startColumn+1),values[index].Code); SetText(sheet.Cell(row,startColumn+2),values[index].Label);
        }
    }

    private static void WriteHeader(IXLWorksheet sheet,IReadOnlyList<string> headers)
    {
        for(var index=0;index<headers.Count;index++) sheet.Cell(1,index+1).Value=headers[index];
        var range=sheet.Range(1,1,1,headers.Count);
        range.Style.Font.Bold=true; range.Style.Font.FontColor=XLColor.White;
        range.Style.Fill.BackgroundColor=XLColor.FromHtml("#185ABD");
        range.Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;
    }

    private static void FormatVisibleSheet(IXLWorksheet sheet,int columnCount,int lastRow)
    {
        sheet.SheetView.FreezeRows(1); sheet.Range(1,1,Math.Max(1,lastRow),columnCount).SetAutoFilter();
        sheet.Columns(1,columnCount).AdjustToContents(1,Math.Min(lastRow,500));
        foreach(var column in sheet.Columns(1,columnCount)) column.Width=Math.Clamp(column.Width+1,10,45);
    }

    private static void SetText(IXLCell cell,string? value)
    {
        cell.Style.NumberFormat.Format="@";
        cell.SetValue(AssignmentRules.NeutralizeFormula(value));
    }

    private static AssignmentExportFile Save(XLWorkbook workbook,string fileName)
    {
        using var stream=new MemoryStream(); workbook.SaveAs(stream,validate:true);
        return new AssignmentExportFile(fileName,stream.ToArray());
    }

    private static string SafeFileName(string value)=>string.Concat(value.Select(character=>
        Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
    private static string? NullIfBlank(string? value)=>string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

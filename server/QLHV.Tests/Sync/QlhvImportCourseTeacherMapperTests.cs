using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Tests.Sync;

public sealed class QlhvImportCourseTeacherMapperTests
{
    [Fact]
    public void Same_raw_teacher_in_two_profiles_has_two_deterministic_target_ids_not_name_identity()
    {
        var teacher = Teacher("GV000001", "Nguyen Van", "An");

        var oto = Assert.IsType<QlhvImportGiaoVienWriteModel>(
            QlhvImportCourseTeacherMapper.MapGiaoVien(teacher, "CSDT_OTO").Model);
        var moto = Assert.IsType<QlhvImportGiaoVienWriteModel>(
            QlhvImportCourseTeacherMapper.MapGiaoVien(teacher, "CSDT_MOTO").Model);

        Assert.Equal("GV000001", oto.SourceMaGV);
        Assert.Equal("CSDT_OTO:GV000001", oto.MaGV);
        Assert.Equal("CSDT_MOTO:GV000001", moto.MaGV);
        Assert.NotEqual(oto.MaGV, moto.MaGV);

        var sameNameDifferentKey = Assert.IsType<QlhvImportGiaoVienWriteModel>(
            QlhvImportCourseTeacherMapper.MapGiaoVien(
                Teacher("GV000002", "Nguyen Van", "An"),
                "CSDT_OTO").Model);
        Assert.NotEqual(oto.MaGV, sameNameDifferentKey.MaGV);
    }

    [Theory]
    [InlineData(@"C:\teacher\signature.png")]
    [InlineData("../teacher.png")]
    [InlineData("safe/../teacher.png")]
    public void Unsafe_teacher_photo_path_is_not_copied(string path)
    {
        var source = Teacher("GV000001", "Nguyen Van", "An");
        source = CopyWithPhoto(source, path);

        var result = QlhvImportCourseTeacherMapper.MapGiaoVien(source, "CSDT_OTO");

        Assert.NotNull(result.Model);
        Assert.Null(result.Model!.AnhRelativePath);
        Assert.Contains(result.Warnings, warning => warning.Contains("AnhCD", StringComparison.Ordinal));
    }

    [Fact]
    public void Teacher_hash_includes_address_and_xml_fields_with_actual_schema_types()
    {
        var first = Teacher("GV000001", "Nguyen Van", "An");
        var changed = new QlhvGiaoVienSourceRow
        {
            MaGV = first.MaGV, MaCSDT = first.MaCSDT, MaSoGTVT = first.MaSoGTVT,
            HoTenDem = first.HoTenDem, TenGV = first.TenGV, NgaySinh = first.NgaySinh,
            SoCMT = first.SoCMT, NgayCapGPLX = first.NgayCapGPLX, TrangThai = first.TrangThai,
            NoiCT_MaDVHC = "12345", NoiCT_MaDVQL = "66",
            MaFileTiepNhanXML = "xml-2", ThoiGianTiepNhanXML = new DateTime(2026, 7, 23),
            ThamNien_LaiXe = 12,
        };

        var before = Assert.IsType<QlhvImportGiaoVienWriteModel>(
            QlhvImportCourseTeacherMapper.MapGiaoVien(first, "CSDT_OTO").Model);
        var after = Assert.IsType<QlhvImportGiaoVienWriteModel>(
            QlhvImportCourseTeacherMapper.MapGiaoVien(changed, "CSDT_OTO").Model);

        Assert.NotEqual(before.SourceHash, after.SourceHash);
        Assert.Equal(12, after.ThamNienLaiXe);
        Assert.Equal("12345", after.NoiCtMaDvhc);
        Assert.Equal("xml-2", after.MaFileTiepNhanXml);
    }

    [Fact]
    public void Relation_preserves_all_source_identity_parts_and_assignment_flag_in_hash()
    {
        var source = Relation(isCourseTeacher: true);
        var mapped = Assert.IsType<QlhvImportKhoaHocGiaoVienWriteModel>(
            QlhvImportCourseTeacherMapper.MapRelation(source, "CSDT_OTO").Model);
        var changed = Assert.IsType<QlhvImportKhoaHocGiaoVienWriteModel>(
            QlhvImportCourseTeacherMapper.MapRelation(Relation(isCourseTeacher: false), "CSDT_OTO").Model);

        Assert.Equal(17, mapped.SourceMaLichLV);
        Assert.Equal("66029K01", mapped.SourceMaKhoaHoc);
        Assert.Equal("GV000001", mapped.SourceMaGV);
        Assert.Equal("CSDT_OTO:GV000001", mapped.MaGV);
        Assert.True(mapped.IsKhoaHocGiaoVien);
        Assert.NotEqual(mapped.SourceHash, changed.SourceHash);
    }

    private static QlhvGiaoVienSourceRow Teacher(string key, string familyName, string givenName)
        => new()
        {
            MaGV = key,
            MaCSDT = "66029",
            MaSoGTVT = "66029",
            HoTenDem = familyName,
            TenGV = givenName,
            NgaySinh = "19800102",
            SoCMT = "001234567890",
            NgayCapGPLX = new DateTime(2020, 1, 1),
            TrangThai = true,
        };

    private static QlhvGiaoVienSourceRow CopyWithPhoto(QlhvGiaoVienSourceRow source, string path)
        => new()
        {
            MaGV = source.MaGV, MaCSDT = source.MaCSDT, MaSoGTVT = source.MaSoGTVT,
            HoTenDem = source.HoTenDem, TenGV = source.TenGV, NgaySinh = source.NgaySinh,
            SoCMT = source.SoCMT, NgayCapGPLX = source.NgayCapGPLX,
            TrangThai = source.TrangThai, AnhCD = path,
        };

    private static QlhvKhoaHocGiaoVienSourceRow Relation(bool isCourseTeacher)
        => new()
        {
            MaLichLV = 17,
            MaKH = "66029K01",
            MaGV = "GV000001",
            TenGV = "Nguyen Van An",
            MaMonHoc = 3,
            SoHV = 25,
            TrangThai = true,
            IsKhoaHocGiaoVien = isCourseTeacher,
        };
}

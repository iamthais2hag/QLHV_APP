using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync.Mapping;

public sealed record QlhvImportMapResult<T>(
    T? Model,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings)
    where T : class;

public static class QlhvImportCourseTeacherMapper
{
    public static QlhvImportMapResult<QlhvImportKhoaHocWriteModel> MapKhoaHoc(
        QlhvKhoaHocSourceRow source,
        string sourceProfileCode)
    {
        ArgumentNullException.ThrowIfNull(source);
        var profile = Required(sourceProfileCode, nameof(sourceProfileCode));
        var sourceKey = Trim(source.MaKH);
        if (sourceKey is null)
        {
            return Blocked<QlhvImportKhoaHocWriteModel>("KhoaHoc co dong thieu MaKH/SourceMaKhoaHoc.");
        }

        var values = new CourseValues(
            sourceKey,
            Trim(source.TenKH),
            Trim(source.MaCSDT),
            Trim(source.MaSoGTVT),
            Trim(source.HangGPLX),
            Trim(source.HangDT),
            Trim(source.SoQD_KhaiGiang),
            source.NgayQD_KhaiGiang,
            source.NgayKG,
            source.NgayBG,
            Trim(source.MucTieuDT),
            source.NgayThi,
            source.NgaySH,
            source.TongSoHV,
            source.SoHVTotNghiep,
            source.SoHVDuocCapGPLX,
            source.ThoiGianDT,
            source.SoNgayOnKT,
            source.SoNgayThucHoc,
            source.SoNgayNghiLe,
            source.TongSoNgay,
            Trim(source.GhiChu),
            source.TrangThai,
            source.TT_Xuly,
            source.HTDaoTao);

        var model = new QlhvImportKhoaHocWriteModel(
            profile,
            sourceKey,
            Hash(values.AsHashFields()),
            values.MaKhoa,
            values.TenKhoa,
            values.MaCSDT,
            values.MaSoGTVT,
            values.HangGPLX,
            values.HangDaoTao,
            values.SoQuyetDinhKhaiGiang,
            values.NgayQuyetDinhKhaiGiang,
            values.NgayKhaiGiang,
            values.NgayBeGiang,
            values.MucTieuDaoTao,
            values.NgayThi,
            values.NgaySatHach,
            values.TongSoHocVien,
            values.SoHocVienTotNghiep,
            values.SoHocVienDuocCapGPLX,
            values.ThoiGianDaoTao,
            values.SoNgayOnKiemTra,
            values.SoNgayThucHoc,
            values.SoNgayNghiLe,
            values.TongSoNgay,
            values.GhiChu,
            values.TrangThaiNguon,
            values.TtXuLy,
            values.HinhThucDaoTao);
        return Success(model);
    }

    public static QlhvImportMapResult<QlhvImportGiaoVienWriteModel> MapGiaoVien(
        QlhvGiaoVienSourceRow source,
        string sourceProfileCode)
    {
        ArgumentNullException.ThrowIfNull(source);
        var profile = Required(sourceProfileCode, nameof(sourceProfileCode));
        var sourceKey = Trim(source.MaGV);
        if (sourceKey is null)
        {
            return Blocked<QlhvImportGiaoVienWriteModel>("GiaoVien co dong thieu MaGV/SourceMaGV.");
        }

        var hoTenDem = Trim(source.HoTenDem);
        var tenGv = Trim(source.TenGV);
        var hoTen = string.Join(" ", new[] { hoTenDem, tenGv }.Where(value => value is not null));
        if (string.IsNullOrWhiteSpace(hoTen))
        {
            return Blocked<QlhvImportGiaoVienWriteModel>($"GiaoVien {sourceKey} thieu ho ten bat buoc.");
        }

        var warnings = new List<string>();
        var ngaySinh = ParseSourceDate(source.NgaySinh, sourceKey, warnings);
        var anhRelativePath = NormalizeRelativePath(source.AnhCD, sourceKey, warnings);
        var targetMaGv = BuildTargetMaGv(profile, sourceKey);
        var values = new TeacherValues(
            targetMaGv,
            hoTen,
            hoTenDem,
            tenGv,
            ngaySinh,
            Trim(source.SoCMT),
            Trim(source.GioiTinh),
            Trim(source.DienThoai),
            Trim(source.NoiCT),
            Trim(source.NoiCT_MaDVHC),
            Trim(source.NoiCT_MaDVQL),
            Trim(source.HinhThuc_TuyenDung),
            Trim(source.TrinhDo_VanHoa),
            Trim(source.TrinhDo_ChuyenMon),
            Trim(source.TrinhDo_SuPham),
            Trim(source.HangGPLX),
            source.NgayCapGPLX,
            source.NgayHHGPLX,
            source.ThamNien_LaiXe,
            Trim(source.CacMonHoc),
            anhRelativePath,
            Trim(source.GhiChu),
            Trim(source.MaCSDT),
            Trim(source.MaSoGTVT),
            Trim(source.SoQD_GCN),
            source.NgayQD_GCN,
            Trim(source.LoaiHinh_DaoTao),
            Trim(source.CacHangGPLXDuocDT),
            Trim(source.CauTaoSuaChua),
            Trim(source.DaoDucLaixe),
            Trim(source.NghiepVuVanTai),
            Trim(source.LuatGTDB),
            Trim(source.KyThuatLaixe),
            Trim(source.MaFileTiepNhanXML),
            source.ThoiGianTiepNhanXML,
            Trim(source.NoiCapGCN),
            Trim(source.LoaiGiaoVien),
            Trim(source.CacHangDaCo),
            source.TrangThai);
        var model = new QlhvImportGiaoVienWriteModel(
            profile,
            sourceKey,
            Hash(values.AsHashFields()),
            values.MaGV,
            values.HoTen,
            values.HoTenDem,
            values.TenGV,
            values.NgaySinh,
            values.SoCCCD,
            values.GioiTinh,
            values.DienThoai,
            values.DiaChi,
            values.NoiCtMaDvhc,
            values.NoiCtMaDvql,
            values.HinhThucTuyenDung,
            values.TrinhDoVanHoa,
            values.TrinhDoChuyenMon,
            values.TrinhDoSuPham,
            values.HangGPLX,
            values.NgayCapGPLX,
            values.NgayHetHanGPLX,
            values.ThamNienLaiXe,
            values.MonHocGiangDay,
            values.AnhRelativePath,
            values.GhiChu,
            values.MaCSDT,
            values.MaSoGTVT,
            values.SoQuyetDinhGCN,
            values.NgayQuyetDinhGCN,
            values.LoaiHinhDaoTao,
            values.CacHangGPLXDuocDaoTao,
            values.CauTaoSuaChua,
            values.DaoDucLaiXe,
            values.NghiepVuVanTai,
            values.LuatGTDB,
            values.KyThuatLaiXe,
            values.MaFileTiepNhanXml,
            values.ThoiGianTiepNhanXml,
            values.NoiCapGCN,
            values.LoaiGiaoVien,
            values.CacHangDaCo,
            values.TrangThaiNguon);
        return new QlhvImportMapResult<QlhvImportGiaoVienWriteModel>(model, Array.Empty<string>(), warnings);
    }

    public static QlhvImportMapResult<QlhvImportKhoaHocGiaoVienWriteModel> MapRelation(
        QlhvKhoaHocGiaoVienSourceRow source,
        string sourceProfileCode)
    {
        ArgumentNullException.ThrowIfNull(source);
        var profile = Required(sourceProfileCode, nameof(sourceProfileCode));
        var maKhoa = Trim(source.MaKH);
        var sourceMaGv = Trim(source.MaGV);
        var blockers = new List<string>();
        if (source.MaLichLV <= 0) blockers.Add("KhoaHoc_GiaoVien co SourceMaLichLV khong hop le.");
        if (maKhoa is null) blockers.Add($"KhoaHoc_GiaoVien {source.MaLichLV} thieu MaKH.");
        if (sourceMaGv is null) blockers.Add($"KhoaHoc_GiaoVien {source.MaLichLV} thieu MaGV.");
        if (blockers.Count > 0)
        {
            return new QlhvImportMapResult<QlhvImportKhoaHocGiaoVienWriteModel>(
                null,
                blockers,
                Array.Empty<string>());
        }

        var values = new RelationValues(
            maKhoa!,
            BuildTargetMaGv(profile, sourceMaGv!),
            Trim(source.TenGV),
            source.MaMonHoc?.ToString(CultureInfo.InvariantCulture),
            Trim(source.TenMonHoc),
            Trim(source.BienSoXe),
            Trim(source.LoaiGV),
            source.SoHV,
            source.NgayHL,
            source.NgayHetHL,
            source.NgayBD,
            source.NgayKT,
            source.IsKhoaHocGiaoVien,
            Trim(source.GhiChu),
            source.TrangThai);
        var model = new QlhvImportKhoaHocGiaoVienWriteModel(
            profile,
            source.MaLichLV,
            maKhoa!,
            sourceMaGv!,
            Hash(values.AsHashFields()),
            values.MaKhoa,
            values.MaGV,
            values.TenGV,
            values.MaMonHoc,
            values.TenMonHoc,
            values.BienSoXe,
            values.LoaiGV,
            values.SoHocVien,
            values.NgayHieuLuc,
            values.NgayHetHieuLuc,
            values.NgayBatDau,
            values.NgayKetThuc,
            values.IsKhoaHocGiaoVien,
            values.GhiChu,
            values.TrangThaiNguon);
        return Success(model);
    }

    public static string BuildTargetMaGv(string sourceProfileCode, string sourceMaGv)
    {
        var value = $"{Required(sourceProfileCode, nameof(sourceProfileCode)).ToUpperInvariant()}:{Required(sourceMaGv, nameof(sourceMaGv))}";
        if (value.Length > 20)
        {
            throw new InvalidOperationException("MaGV phan vung vuot qua do dai target 20 ky tu.");
        }

        return value;
    }

    private static DateTime? ParseSourceDate(string? raw, string sourceKey, ICollection<string> warnings)
    {
        var value = Trim(raw);
        if (value is null) return null;
        if (DateTime.TryParseExact(
                value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed.Date;
        }

        warnings.Add($"GiaoVien {sourceKey}: NgaySinh khong dung dinh dang yyyyMMdd; giu NULL.");
        return null;
    }

    private static string? NormalizeRelativePath(
        string? raw,
        string sourceKey,
        ICollection<string> warnings)
    {
        var value = Trim(raw);
        if (value is null) return null;

        var normalized = value.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (Path.IsPathRooted(value) ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            segments.Any(segment => segment is "." or ".."))
        {
            warnings.Add(
                $"GiaoVien {sourceKey}: AnhCD khong phai duong dan tuong doi an toan; giu NULL.");
            return null;
        }

        return string.Join('/', segments);
    }

    private static string Hash(IEnumerable<string?> values)
    {
        var canonical = string.Join("|", values.Select(value =>
        {
            var normalized = value ?? string.Empty;
            return $"{normalized.Length}:{normalized}";
        }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Required(string? value, string name) =>
        Trim(value) ?? throw new InvalidOperationException($"Thieu gia tri bat buoc: {name}.");
    private static string D(DateTime? value) => value?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
    private static string I(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    private static string B(bool value) => value ? "1" : "0";

    private static QlhvImportMapResult<T> Success<T>(T model) where T : class =>
        new(model, Array.Empty<string>(), Array.Empty<string>());
    private static QlhvImportMapResult<T> Blocked<T>(string blocker) where T : class =>
        new(null, new[] { blocker }, Array.Empty<string>());

    private sealed record CourseValues(
        string MaKhoa, string? TenKhoa, string? MaCSDT, string? MaSoGTVT, string? HangGPLX,
        string? HangDaoTao, string? SoQuyetDinhKhaiGiang, DateTime? NgayQuyetDinhKhaiGiang,
        DateTime? NgayKhaiGiang, DateTime? NgayBeGiang, string? MucTieuDaoTao, DateTime? NgayThi,
        DateTime? NgaySatHach, int? TongSoHocVien, int? SoHocVienTotNghiep,
        int? SoHocVienDuocCapGPLX, int? ThoiGianDaoTao, int? SoNgayOnKiemTra,
        int? SoNgayThucHoc, int? SoNgayNghiLe, int? TongSoNgay, string? GhiChu,
        bool TrangThaiNguon, int? TtXuLy, int? HinhThucDaoTao)
    {
        public IEnumerable<string?> AsHashFields() => new[]
        {
            MaKhoa, TenKhoa, MaCSDT, MaSoGTVT, HangGPLX, HangDaoTao, SoQuyetDinhKhaiGiang,
            D(NgayQuyetDinhKhaiGiang), D(NgayKhaiGiang), D(NgayBeGiang), MucTieuDaoTao,
            D(NgayThi), D(NgaySatHach), I(TongSoHocVien), I(SoHocVienTotNghiep),
            I(SoHocVienDuocCapGPLX), I(ThoiGianDaoTao), I(SoNgayOnKiemTra),
            I(SoNgayThucHoc), I(SoNgayNghiLe), I(TongSoNgay), GhiChu, B(TrangThaiNguon),
            I(TtXuLy), I(HinhThucDaoTao),
        };
    }

    private sealed record TeacherValues(
        string MaGV, string HoTen, string? HoTenDem, string? TenGV, DateTime? NgaySinh,
        string? SoCCCD, string? GioiTinh, string? DienThoai, string? DiaChi,
        string? NoiCtMaDvhc, string? NoiCtMaDvql,
        string? HinhThucTuyenDung, string? TrinhDoVanHoa, string? TrinhDoChuyenMon,
        string? TrinhDoSuPham, string? HangGPLX, DateTime? NgayCapGPLX,
        DateTime? NgayHetHanGPLX, int? ThamNienLaiXe, string? MonHocGiangDay,
        string? AnhRelativePath, string? GhiChu, string? MaCSDT, string? MaSoGTVT,
        string? SoQuyetDinhGCN, DateTime? NgayQuyetDinhGCN, string? LoaiHinhDaoTao,
        string? CacHangGPLXDuocDaoTao, string? CauTaoSuaChua, string? DaoDucLaiXe,
        string? NghiepVuVanTai, string? LuatGTDB, string? KyThuatLaiXe,
        string? MaFileTiepNhanXml, DateTime? ThoiGianTiepNhanXml, string? NoiCapGCN,
        string? LoaiGiaoVien, string? CacHangDaCo, bool TrangThaiNguon)
    {
        public IEnumerable<string?> AsHashFields() => new[]
        {
            MaGV, HoTen, HoTenDem, TenGV, D(NgaySinh), SoCCCD, GioiTinh, DienThoai, DiaChi,
            NoiCtMaDvhc, NoiCtMaDvql,
            HinhThucTuyenDung, TrinhDoVanHoa, TrinhDoChuyenMon, TrinhDoSuPham, HangGPLX,
            D(NgayCapGPLX), D(NgayHetHanGPLX), I(ThamNienLaiXe), MonHocGiangDay,
            AnhRelativePath, GhiChu, MaCSDT, MaSoGTVT, SoQuyetDinhGCN,
            D(NgayQuyetDinhGCN), LoaiHinhDaoTao, CacHangGPLXDuocDaoTao, CauTaoSuaChua,
            DaoDucLaiXe, NghiepVuVanTai, LuatGTDB, KyThuatLaiXe, MaFileTiepNhanXml,
            D(ThoiGianTiepNhanXml), NoiCapGCN, LoaiGiaoVien,
            CacHangDaCo, B(TrangThaiNguon),
        };
    }

    private sealed record RelationValues(
        string MaKhoa, string MaGV, string? TenGV, string? MaMonHoc, string? TenMonHoc,
        string? BienSoXe, string? LoaiGV, int? SoHocVien, DateTime? NgayHieuLuc,
        DateTime? NgayHetHieuLuc, DateTime? NgayBatDau, DateTime? NgayKetThuc,
        bool IsKhoaHocGiaoVien, string? GhiChu, bool TrangThaiNguon)
    {
        public IEnumerable<string?> AsHashFields() => new[]
        {
            MaKhoa, MaGV, TenGV, MaMonHoc, TenMonHoc, BienSoXe, LoaiGV, I(SoHocVien),
            D(NgayHieuLuc), D(NgayHetHieuLuc), D(NgayBatDau), D(NgayKetThuc),
            B(IsKhoaHocGiaoVien), GhiChu,
            B(TrangThaiNguon),
        };
    }
}

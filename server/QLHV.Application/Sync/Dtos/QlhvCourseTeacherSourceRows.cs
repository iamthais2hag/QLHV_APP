namespace QLHV.Application.Sync.Dtos;

/// <summary>One course row read from a fixed CSDT backup database.</summary>
public sealed class QlhvKhoaHocSourceRow
{
    public string MaKH { get; init; } = string.Empty;
    public string MaCSDT { get; init; } = string.Empty;
    public string MaSoGTVT { get; init; } = string.Empty;
    public string? TenKH { get; init; }
    public string? HangGPLX { get; init; }
    public string? HangDT { get; init; }
    public string? SoQD_KhaiGiang { get; init; }
    public DateTime? NgayQD_KhaiGiang { get; init; }
    public DateTime? NgayKG { get; init; }
    public DateTime? NgayBG { get; init; }
    public string? MucTieuDT { get; init; }
    public DateTime? NgayThi { get; init; }
    public DateTime? NgaySH { get; init; }
    public int? TongSoHV { get; init; }
    public int? SoHVTotNghiep { get; init; }
    public int? SoHVDuocCapGPLX { get; init; }
    public int? ThoiGianDT { get; init; }
    public int? SoNgayOnKT { get; init; }
    public int? SoNgayThucHoc { get; init; }
    public int? SoNgayNghiLe { get; init; }
    public int? TongSoNgay { get; init; }
    public string? GhiChu { get; init; }
    public bool TrangThai { get; init; }
    public int? TT_Xuly { get; init; }
    public int? HTDaoTao { get; init; }
}

/// <summary>One teacher row read from a fixed CSDT backup database.</summary>
public sealed class QlhvGiaoVienSourceRow
{
    public string MaGV { get; init; } = string.Empty;
    public string MaSoGTVT { get; init; } = string.Empty;
    public string MaCSDT { get; init; } = string.Empty;
    public string? HoTenDem { get; init; }
    public string? TenGV { get; init; }
    public string? NgaySinh { get; init; }
    public string? AnhCD { get; init; }
    public string? SoCMT { get; init; }
    public string? NoiCT { get; init; }
    public string? NoiCT_MaDVHC { get; init; }
    public string? NoiCT_MaDVQL { get; init; }
    public string? GioiTinh { get; init; }
    public string? DienThoai { get; init; }
    public string? HinhThuc_TuyenDung { get; init; }
    public string? TrinhDo_VanHoa { get; init; }
    public string? TrinhDo_ChuyenMon { get; init; }
    public string? TrinhDo_SuPham { get; init; }
    public string? HangGPLX { get; init; }
    public DateTime? NgayCapGPLX { get; init; }
    public int? ThamNien_LaiXe { get; init; }
    public string? SoQD_GCN { get; init; }
    public DateTime? NgayQD_GCN { get; init; }
    public string? LoaiHinh_DaoTao { get; init; }
    public string? GhiChu { get; init; }
    public bool TrangThai { get; init; }
    public string? CacHangGPLXDuocDT { get; init; }
    public string? CauTaoSuaChua { get; init; }
    public string? DaoDucLaixe { get; init; }
    public string? NghiepVuVanTai { get; init; }
    public string? LuatGTDB { get; init; }
    public string? KyThuatLaixe { get; init; }
    public string? MaFileTiepNhanXML { get; init; }
    public DateTime? ThoiGianTiepNhanXML { get; init; }
    public DateTime? NgayHHGPLX { get; init; }
    public string? NoiCapGCN { get; init; }
    public string? CacMonHoc { get; init; }
    public string? LoaiGiaoVien { get; init; }
    public string? CacHangDaCo { get; init; }
}

/// <summary>One course-teacher assignment row from the backup snapshot.</summary>
public sealed class QlhvKhoaHocGiaoVienSourceRow
{
    public int MaLichLV { get; init; }
    public string MaKH { get; init; } = string.Empty;
    public string MaGV { get; init; } = string.Empty;
    public string? TenGV { get; init; }
    public string? BienSoXe { get; init; }
    public string? LoaiGV { get; init; }
    public int? SoHV { get; init; }
    public DateTime? NgayHL { get; init; }
    public DateTime? NgayHetHL { get; init; }
    public string? GhiChu { get; init; }
    public bool TrangThai { get; init; }
    public DateTime? NgayBD { get; init; }
    public DateTime? NgayKT { get; init; }
    public bool IsKhoaHocGiaoVien { get; init; }
    public int? MaMonHoc { get; init; }
    public string? TenMonHoc { get; init; }
}

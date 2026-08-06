namespace QLHV.Infrastructure.Sync.Realtime;

internal enum CsdtRealtimeColumnOwner
{
    V2,
    V1,
    Shared,
    Immutable,
}

internal enum CsdtRealtimeMergeRule
{
    Direct,
    PreserveWhenV1LifecycleActive,
    TrainingState,
}

internal sealed record CsdtRealtimeColumnRule(
    string Name,
    CsdtRealtimeColumnOwner Owner,
    bool AllowInsert,
    bool AllowUpdate,
    bool ReadForward,
    CsdtRealtimeMergeRule MergeRule = CsdtRealtimeMergeRule.Direct);

/// <summary>
/// The single forward V2-to-V1 column authority map. Every source column must
/// be explicitly classified. A schema addition therefore cannot silently gain
/// INSERT or UPDATE rights.
/// </summary>
internal sealed class CsdtRealtimeDomainColumnPolicy
{
    private readonly IReadOnlyDictionary<string, CsdtRealtimeColumnRule> _rules;

    internal CsdtRealtimeDomainColumnPolicy(
        string domain,
        IEnumerable<CsdtRealtimeColumnRule> rules,
        bool automaticWritesEnabled = true)
    {
        Domain = domain;
        AutomaticWritesEnabled = automaticWritesEnabled;
        var materialized = rules.ToArray();
        var duplicate = materialized
            .GroupBy(rule => rule.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Forward ownership policy for {domain} classifies {duplicate.Key} more than once.");
        }

        _rules = materialized.ToDictionary(rule => rule.Name, StringComparer.Ordinal);
    }

    internal string Domain { get; }

    internal bool AutomaticWritesEnabled { get; }

    internal IReadOnlyCollection<CsdtRealtimeColumnRule> Rules => _rules.Values.ToArray();

    internal CsdtRealtimeColumnRule GetRequired(string column)
        => _rules.TryGetValue(column, out var rule)
            ? rule
            : throw new CsdtRealtimeSchemaException(
                $"UNCLASSIFIED_FORWARD_COLUMN: dbo.{Domain}.{column} has no V2-to-V1 ownership policy.");

    internal void ValidateSourceSchema(CsdtRealtimeTableMetadata metadata)
    {
        foreach (var column in metadata.Columns)
        {
            _ = GetRequired(column.Name);
        }
    }

    internal IReadOnlyList<CsdtRealtimeColumnMetadata> SelectForwardReadColumns(
        CsdtRealtimeTableMetadata metadata)
    {
        ValidateSourceSchema(metadata);
        var selected = metadata.Columns
            .Where(column => !column.IsComputed && GetRequired(column.Name).ReadForward)
            .OrderBy(column => column.ColumnId)
            .ToArray();
        EnsureKeysAreSelected(metadata, selected);
        return selected;
    }

    internal IReadOnlyList<CsdtRealtimeColumnMetadata> SelectInsertColumns(
        CsdtRealtimeTableMetadata metadata)
    {
        ValidateSourceSchema(metadata);
        return AutomaticWritesEnabled
            ? metadata.Columns
                .Where(column =>
                    !column.IsComputed &&
                    GetRequired(column.Name).AllowInsert)
                .OrderBy(column => column.ColumnId)
                .ToArray()
            : [];
    }

    internal IReadOnlyList<CsdtRealtimeColumnMetadata> SelectUpdateColumns(
        CsdtRealtimeTableMetadata metadata)
    {
        ValidateSourceSchema(metadata);
        return AutomaticWritesEnabled
            ? metadata.Columns
                .Where(column =>
                    !column.IsComputed &&
                    GetRequired(column.Name).AllowUpdate)
                .OrderBy(column => column.ColumnId)
                .ToArray()
            : [];
    }

    internal IReadOnlyList<CsdtRealtimeColumnMetadata> SelectHashColumns(
        CsdtRealtimeTableMetadata metadata)
    {
        var names = SelectInsertColumns(metadata)
            .Concat(SelectUpdateColumns(metadata))
            .Concat(metadata.PrimaryKey)
            .Select(column => column.Name)
            .ToHashSet(StringComparer.Ordinal);
        return metadata.Columns
            .Where(column => names.Contains(column.Name))
            .OrderBy(column => column.ColumnId)
            .ToArray();
    }

    private static void EnsureKeysAreSelected(
        CsdtRealtimeTableMetadata metadata,
        IReadOnlyList<CsdtRealtimeColumnMetadata> selected)
    {
        var selectedNames = selected.Select(column => column.Name).ToHashSet(StringComparer.Ordinal);
        var missing = metadata.PrimaryKey
            .Where(column => !selectedNames.Contains(column.Name))
            .Select(column => column.Name)
            .ToArray();
        if (missing.Length != 0)
        {
            throw new CsdtRealtimeSchemaException(
                $"Forward ownership policy for dbo.{metadata.Domain.TableName} does not read key columns: " +
                string.Join(", ", missing));
        }
    }
}

internal static class CsdtRealtimeColumnOwnershipPolicy
{
    internal static IReadOnlySet<string> V1OwnedDomains { get; } =
        new HashSet<string>(
            [
                "BaoCaoII",
                "KySH",
                "DM_LyDoTCBC2",
                "DM_DiemSatHach",
                "DM_KetQuaSatHach",
            ],
            StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, CsdtRealtimeDomainColumnPolicy> Policies =
        BuildPolicies();

    internal static CsdtRealtimeDomainColumnPolicy GetRequired(string domain)
    {
        if (V1OwnedDomains.Contains(domain))
        {
            throw new CsdtRealtimeSchemaException(
                $"V1_OWNED_DOMAIN: dbo.{domain} is excluded from forward V2-to-V1 synchronization.");
        }

        return Policies.TryGetValue(domain, out var policy)
            ? policy
            : throw new CsdtRealtimeSchemaException(
                $"UNCLASSIFIED_FORWARD_DOMAIN: dbo.{domain} has no V2-to-V1 ownership policy.");
    }

    private static IReadOnlyDictionary<string, CsdtRealtimeDomainColumnPolicy> BuildPolicies()
    {
        var policies = new[]
        {
            Policy(
                "DM_DonViGTVT",
                Key("MaDV"),
                V2(
                    "MaDVQL", "LoaiDV", "TenDV", "CoQuanQL", "LoaiTTSH",
                    "CacHangGPLX", "DienThoai", "Fax", "DiaChi", "LuuLuongDT",
                    "SoGP", "NgayGP", "LanhDao", "GhiChu", "TrangThai", "Website",
                    "DienTichSanTap", "NgayHHGP", "DiaDiemDaoTao", "MaDvOld"),
                IgnoredImmutable("NguoiTao", "NguoiSua", "NgayTao", "NgaySua")),
            Policy(
                "GiaoVien",
                Key("MaGV"),
                V2(
                    "MaSoGTVT", "MaCSDT", "HoTenDem", "TenGV", "NgaySinh", "AnhCD",
                    "SoCMT", "NoiCT", "NoiCT_MaDVHC", "NoiCT_MaDVQL", "GioiTinh",
                    "DienThoai", "HinhThuc_TuyenDung", "TrinhDo_VanHoa",
                    "TrinhDo_ChuyenMon", "TrinhDo_SuPham", "HangGPLX", "NgayCapGPLX",
                    "ThamNien_LaiXe", "SoQD_GCN", "NgayQD_GCN", "LoaiHinh_DaoTao",
                    "GhiChu", "TrangThai", "CacHangGPLXDuocDT", "CauTaoSuaChua",
                    "DaoDucLaixe", "NghiepVuVanTai", "LuatGTDB", "KyThuatLaixe",
                    "MaFileTiepNhanXML", "ThoiGianTiepNhanXML", "NgayHHGPLX",
                    "NoiCapGCN", "CacMonHoc", "LoaiGiaoVien", "CacHangDaCo"),
                IgnoredImmutable("NguoiTao", "NguoiSua", "NgayTao", "NgaySua")),
            Policy(
                "KhoaHoc",
                Key("MaKH"),
                V2(
                    "MaCSDT", "MaSoGTVT", "TenKH", "HangGPLX", "HangDT",
                    "SoQD_KhaiGiang", "NgayQD_KhaiGiang", "NgayKG", "NgayBG",
                    "MucTieuDT", "NgayThi", "NgaySH", "TongSoHV", "SoHVTotNghiep",
                    "SoHVDuocCapGPLX", "ThoiGianDT", "SoNgayOnKT", "SoNgayThucHoc",
                    "SoNgayNghiLe", "TongSoNgay", "GhiChu", "TrangThai", "TT_Xuly",
                    "HTDaoTao"),
                IgnoredImmutable("NguoiTao", "NguoiSua", "NgayTao", "NgaySua")),
            Policy(
                "KhoaHoc_GiaoVien",
                Key("MaLichLV"),
                V2(
                    "MaKH", "MaGV", "TenGV", "BienSoXe", "LoaiGV", "SoHV",
                    "NgayHL", "NgayHetHL", "GhiChu", "TrangThai", "NgayBD", "NgayKT",
                    "IsKhoaHocGiaoVien", "MaMonHoc", "TenMonHoc"),
                IgnoredImmutable("NguoiTao", "NguoiSua", "NgayTao", "NgaySua")),
            Policy(
                "BaoCaoI",
                Key("MaBCI"),
                V2(
                    "MaCSDT", "MaKH", "SoBaoCao", "NgayBaoCao", "SoGP", "NgayCapGP",
                    "LuuLuongGP", "SoHocSinh", "NgayKG", "NgayBG", "NgayTiepNhan",
                    "NguoiTiepNhan", "ThoiGianTiepNhan", "ThoiGianDaoTao", "LuuLuong",
                    "BoTriHocVienXeTap", "GhiChu", "TrangThai", "SoHSCanhBao", "TT_Xuly"),
                IgnoredImmutable("NguoiTao", "NguoiSua", "NgayTao", "NgaySua")),
            Policy(
                "NguoiLX",
                Key("MaDK"),
                V2(
                    "DonViNhanHSo", "HoDemNLX", "TenNLX", "HoVaTen", "MaQuocTich",
                    "NgaySinh", "NoiTT", "NoiTT_MaDVHC", "NoiTT_MaDVQL", "NoiCT",
                    "NoiCT_MaDVHC", "NoiCT_MaDVQL", "SoCMT", "NgayCapCMT",
                    "NoiCapCMT", "GhiChu", "TrangThai", "GioiTinh", "HoVaTenIn",
                    "SO_CMND_CU"),
                IgnoredImmutable("NguoiTao", "NguoiSua", "NgayTao", "NgaySua"),
                ClassifiedButNotWritable("HosoDvcc4")),
            BuildDossierPolicy(),
            BuildLicencePolicy(),
            Policy(
                "NguoiLXHS_GiayTo",
                Key("MaGT", "MaDK"),
                V2("SoHoSo", "TenGT", "TrangThai")),
        };

        return policies.ToDictionary(policy => policy.Domain, StringComparer.Ordinal);
    }

    private static CsdtRealtimeDomainColumnPolicy BuildDossierPolicy()
        => Policy(
            "NguoiLX_HoSo",
            Key("MaDK"),
            V2(
                "SoHoSo", "MaCSDT", "MaSoGTVT", "MaDVNhanHSo", "NgayNhanHSo",
                "NguoiNhanHSo", "NgayHenTra", "MaLoaiHs", "DuongDanAnh",
                "ChatLuongAnh", "NgayThuNhanAnh", "NguoiThuNhanAnh", "SoGPLXDaCo",
                "HangGPLXDaCo", "DonViCapGPLXDaCo", "NoiCapGPLXDaCo",
                "NgayCapGPLXDaCo", "NgayHHGPLXDaCo", "NgayTTGPLXDaCo",
                "DonViHocLX", "NamHocLX", "HangGPLX", "SoNamLX", "SoKmLXAnToan",
                "LyDoCapDoi", "MucDichCapDoi", "HangDaoTao", "SoGiayCNTN", "SoCCN",
                "BC1_TuoiTS", "BC1_ThamNien", "NgayKTBC1", "NguoiKTBC1", "KQ_BC1",
                "KQ_BC1_GhiChu", "VaoSoCNNSo", "NgayVaoSoCNN", "XepLoaiTotNghiep",
                "NgayCapCCN", "SoQuyetDinhTN", "NgayRaQDTN", "SoSoTN", "NgayVaoSoTN",
                "NgayInGiayTN", "NamcapLandau", "MaTrichNgang", "KQLyThuyet",
                "KQThucHanh", "TongQDThucHanh", "KetLuanCSDT", "DiemKQLyThuyet",
                "DiemKQThucHanh", "TGBatDau", "TGKetThuc", "TGThucHanhHinh",
                "TGThucHanhDuong"),
            V2(
                CsdtRealtimeMergeRule.PreserveWhenV1LifecycleActive,
                "MaKhoaHoc"),
            Shared(
                CsdtRealtimeMergeRule.PreserveWhenV1LifecycleActive,
                "GiayCNSK", "MaBC1", "GhiChu", "TrangThai", "GiaiTrinh"),
            Shared(CsdtRealtimeMergeRule.TrainingState, "TT_XuLy"),
            V1(
                "NoiDungSH", "MaBC2", "KetQuaBC2", "MaLyDoTCBC2", "MaKySH",
                "SoBD", "LanSH", "SoQDSH", "NgayQDSH", "KetQua_LyThuyet",
                "NhanXet_LyThuyet", "KetQuaSHM", "NhanXet_MoPhong", "KetQua_Hinh",
                "NhanXet_Hinh", "KetQua_Duong", "NhanXet_Duong", "KetQuaSH",
                "SoQDTT", "NgayQDTT", "NguoiKy", "SoGPLXTmp", "NgayKTBC2",
                "NguoiKTBC2", "MaIn", "KetQuaDoiSanhTW", "GhiChuKQDSTW", "ChuKy",
                "TT_XuLy_Old", "CHON_IN_GPLX", "KetQuaPDSo", "DAT_QDThucHanh",
                "DAT_TGThucHanh", "DAT_KQCuc", "DAT_ThoiGianLayKQ",
                "LyDoTuChoiKQDT"),
            IgnoredImmutable("NguoiTao", "NguoiSua", "NgayTao", "NgaySua", "IDs"),
            ClassifiedButNotWritable(
                "MaHTCap", "Transfer_flag", "CoQuanQuanLyGPLX",
                "QDThucHanhHinh", "HosoDvcc4"));

    private static CsdtRealtimeDomainColumnPolicy BuildLicencePolicy()
        => Policy(
            "NguoiLX_GPLX",
            automaticWritesEnabled: false,
            Key("MaDK"),
            V1NotRead(
                "SoGPLX", "HangGPLX", "SoHoSo", "SoGPLXCu", "NoiCapGPLX",
                "NgayCapGPLX", "CoQuanQLGPLX", "NgayHHGPLX", "NgayTTGPLX",
                "MoTaVN", "MoTaEN", "NguoiKy", "MaHTCap", "NoiHocGPLX",
                "NamHocGPLX", "DuongDanAnh", "HoTenDem", "TenNLX", "HoVaTen",
                "NgaySinh", "MaQuocTich", "NoiCT", "NoiCT_MaDVHC",
                "NoiCT_MaDVQL", "SoCMT", "SoSeri", "NoiIn", "NgayIn", "NgayTra",
                "NguoiTra", "NoiTra", "GhiChu", "NguoiTao", "NguoiSua", "NgayTao",
                "NgaySua", "TrangThai", "GioiTinh", "NgayTT_A1", "NgayTT_A2",
                "NgayTT_A3", "NgayTT_A4", "NgayTT_B1", "NgayTT_B2", "NgayTT_C",
                "NgayTT_D", "NgayTT_E", "NgayTT_F", "NgayTT_FB2", "NgayTT_FC",
                "NgayTT_FD", "NgayTT_FE"));

    private static CsdtRealtimeDomainColumnPolicy Policy(
        string domain,
        params IEnumerable<CsdtRealtimeColumnRule>[] groups)
        => Policy(domain, automaticWritesEnabled: true, groups);

    private static CsdtRealtimeDomainColumnPolicy Policy(
        string domain,
        bool automaticWritesEnabled,
        params IEnumerable<CsdtRealtimeColumnRule>[] groups)
        => new(domain, groups.SelectMany(group => group), automaticWritesEnabled);

    private static IEnumerable<CsdtRealtimeColumnRule> Key(params string[] names)
        => names.Select(name => new CsdtRealtimeColumnRule(
            name,
            CsdtRealtimeColumnOwner.Immutable,
            AllowInsert: true,
            AllowUpdate: false,
            ReadForward: true));

    private static IEnumerable<CsdtRealtimeColumnRule> V2(params string[] names)
        => V2(CsdtRealtimeMergeRule.Direct, names);

    private static IEnumerable<CsdtRealtimeColumnRule> V2(
        CsdtRealtimeMergeRule mergeRule,
        params string[] names)
        => names.Select(name => new CsdtRealtimeColumnRule(
            name,
            CsdtRealtimeColumnOwner.V2,
            AllowInsert: true,
            AllowUpdate: true,
            ReadForward: true,
            mergeRule));

    private static IEnumerable<CsdtRealtimeColumnRule> Shared(
        CsdtRealtimeMergeRule mergeRule,
        params string[] names)
        => names.Select(name => new CsdtRealtimeColumnRule(
            name,
            CsdtRealtimeColumnOwner.Shared,
            AllowInsert: true,
            AllowUpdate: true,
            ReadForward: true,
            mergeRule));

    private static IEnumerable<CsdtRealtimeColumnRule> V1(params string[] names)
        => names.Select(name => new CsdtRealtimeColumnRule(
            name,
            CsdtRealtimeColumnOwner.V1,
            AllowInsert: false,
            AllowUpdate: false,
            ReadForward: true));

    private static IEnumerable<CsdtRealtimeColumnRule> V1NotRead(params string[] names)
        => names.Select(name => new CsdtRealtimeColumnRule(
            name,
            CsdtRealtimeColumnOwner.V1,
            AllowInsert: false,
            AllowUpdate: false,
            ReadForward: false));

    private static IEnumerable<CsdtRealtimeColumnRule> IgnoredImmutable(params string[] names)
        => names.Select(name => new CsdtRealtimeColumnRule(
            name,
            CsdtRealtimeColumnOwner.Immutable,
            AllowInsert: false,
            AllowUpdate: false,
            ReadForward: false));

    private static IEnumerable<CsdtRealtimeColumnRule> ClassifiedButNotWritable(params string[] names)
        => names.Select(name => new CsdtRealtimeColumnRule(
            name,
            CsdtRealtimeColumnOwner.V2,
            AllowInsert: false,
            AllowUpdate: false,
            ReadForward: false));
}

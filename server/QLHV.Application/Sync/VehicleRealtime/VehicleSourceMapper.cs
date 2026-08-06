using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace QLHV.Application.Sync.VehicleRealtime;

public static class VehicleSourceNormalizer
{
    private static readonly HashSet<char> PlateSeparators = ['.', '-', ' ', '\t'];
    private static readonly HashSet<char> SecondarySeparators = ['.', '-', ' ', '\t'];

    public static string? TrimToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Normalize(NormalizationForm.FormC).Trim();
    }

    /// <summary>
    /// Search/collision key only. It must never replace the exact source primary key.
    /// </summary>
    public static string NormalizePlateCollisionKey(string value)
        => RemoveSeparators(value, PlateSeparators);

    public static string? NormalizeSecondaryCollisionKey(string? value)
    {
        var trimmed = TrimToNull(value);
        return trimmed is null
            ? null
            : RemoveSeparators(trimmed, SecondarySeparators);
    }

    private static string RemoveSeparators(string value, IReadOnlySet<char> separators)
    {
        var normalized = value.Normalize(NormalizationForm.FormC).Trim();
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (!separators.Contains(character) && !char.IsWhiteSpace(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }
}

public static class VehicleSourceMapper
{
    private static readonly IReadOnlyDictionary<string, int> TargetLengths =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["BienSoXe"] = 20,
            ["SoDK"] = 50,
            ["NhanHieu"] = 100,
            ["LoaiXe"] = 100,
            ["MacXe"] = 100,
            ["HangXe"] = 50,
            ["HangGPLXXe"] = 20,
            ["MauXe"] = 100,
            ["SoDongCo"] = 50,
            ["SoKhung"] = 50,
            ["SoGPXTL"] = 50,
            ["CoQuanCapGPXTL"] = 255,
            ["TuyenDuong"] = 500,
            ["ChatLuong"] = 100,
            ["GhiChuV2"] = 500,
            ["MaCSDT"] = 6,
            ["MaSoGTVT"] = 6,
            ["SourceCreatedBy"] = 60,
            ["SourceUpdatedBy"] = 60,
            ["SourceMaFileTiepNhanXml"] = 100,
        };

    public static VehicleMappingResult Map(
        VehicleSourceRow source,
        string sourceProfileCode)
    {
        ArgumentNullException.ThrowIfNull(source);
        var blockers = new List<string>();
        var warnings = new List<string>();
        VehicleRealtimeRoute route;
        try
        {
            route = VehicleRealtimeRouteCatalog.GetRequired(sourceProfileCode);
        }
        catch (ArgumentException)
        {
            return Blocked(VehicleRealtimeReviewCodes.InvalidSourceIdentity);
        }

        var sourcePlate = VehicleSourceNormalizer.TrimToNull(source.BienSoXe);
        if (sourcePlate is null || sourcePlate.Length > 10)
        {
            blockers.Add(VehicleRealtimeReviewCodes.InvalidSourceIdentity);
        }

        var maCsdt = VehicleSourceNormalizer.TrimToNull(source.MaCSDT);
        var maSoGtvt = VehicleSourceNormalizer.TrimToNull(source.MaSoGTVT);
        if (!string.Equals(maCsdt, route.ExpectedMaCsdt, StringComparison.Ordinal) ||
            maSoGtvt is null)
        {
            blockers.Add(VehicleRealtimeReviewCodes.WrongSourcePartition);
        }

        var soDk = T(source.SoDK);
        var nhanHieu = T(source.NhanHieu);
        var loaiXe = T(source.LoaiXe);
        var macXe = T(source.MacXe);
        var hangXe = T(source.HangXe);
        var hangGplxXe = T(source.HangGPLXXe);
        var mauXe = T(source.MauXe);
        var soDongCo = T(source.SoDongCo);
        var soKhung = T(source.SoKhung);
        var soGpxTl = T(source.SoGPXTL);
        var coQuanCapGpxTl = T(source.CoQuanCapGPXTL);
        var tuyenDuong = T(source.TuyenDuong);
        var chatLuong = T(source.ChatLuong);
        var ghiChu = T(source.GhiChu);
        var sourceCreatedBy = T(source.NguoiTao);
        var sourceUpdatedBy = T(source.NguoiSua);
        var sourceXml = T(source.MaFileTiepNhanXML);

        CheckLength("BienSoXe", sourcePlate, blockers);
        CheckLength("SoDK", soDk, blockers);
        CheckLength("NhanHieu", nhanHieu, blockers);
        CheckLength("LoaiXe", loaiXe, blockers);
        CheckLength("MacXe", macXe, blockers);
        CheckLength("HangXe", hangXe, blockers);
        CheckLength("HangGPLXXe", hangGplxXe, blockers);
        CheckLength("MauXe", mauXe, blockers);
        CheckLength("SoDongCo", soDongCo, blockers);
        CheckLength("SoKhung", soKhung, blockers);
        CheckLength("SoGPXTL", soGpxTl, blockers);
        CheckLength("CoQuanCapGPXTL", coQuanCapGpxTl, blockers);
        CheckLength("TuyenDuong", tuyenDuong, blockers);
        CheckLength("ChatLuong", chatLuong, blockers);
        CheckLength("GhiChuV2", ghiChu, blockers);
        CheckLength("MaCSDT", maCsdt, blockers);
        CheckLength("MaSoGTVT", maSoGtvt, blockers);
        CheckLength("SourceCreatedBy", sourceCreatedBy, blockers);
        CheckLength("SourceUpdatedBy", sourceUpdatedBy, blockers);
        CheckLength("SourceMaFileTiepNhanXml", sourceXml, blockers);

        if (blockers.Count != 0)
        {
            return new VehicleMappingResult(
                null,
                blockers.Distinct(StringComparer.Ordinal).ToArray(),
                warnings);
        }

        var imagePath = T(source.DuongDanAnh);
        var imagePathHash = imagePath is null ? null : HashText(imagePath);
        if (imagePath is not null)
        {
            warnings.Add(VehicleRealtimeWarnings.ManagedImageCopyRequired);
        }

        var identity = VehicleSourceIdentity.Create(route.SourceProfileCode, sourcePlate!);
        var normalizedPlate =
            VehicleSourceNormalizer.NormalizePlateCollisionKey(identity.SourceBienSoXe);
        if (normalizedPlate.Length == 0)
        {
            return Blocked(VehicleRealtimeReviewCodes.InvalidSourceIdentity);
        }

        var lifecycle = source.TrangThai
            ? VehicleRealtimeLifecycles.Active
            : VehicleRealtimeLifecycles.SourceInactive;
        var hashFields = new string?[]
        {
            identity.SourceProfileCode,
            identity.SourceBienSoXe,
            normalizedPlate,
            maCsdt,
            maSoGtvt,
            soDk,
            VehicleSourceNormalizer.NormalizeSecondaryCollisionKey(soDk),
            B(source.SoHuu),
            nhanHieu,
            loaiXe,
            macXe,
            hangXe,
            hangGplxXe,
            mauXe,
            I(source.NamSX),
            soDongCo,
            VehicleSourceNormalizer.NormalizeSecondaryCollisionKey(soDongCo),
            soKhung,
            VehicleSourceNormalizer.NormalizeSecondaryCollisionKey(soKhung),
            B(source.GiayPhepXTL),
            soGpxTl,
            coQuanCapGpxTl,
            D(source.NgayCapGPXTL),
            D(source.NgayHHGPXTL),
            B(source.HeThongPP),
            B(source.BaoHiem),
            tuyenDuong,
            chatLuong,
            D(source.NgayCapGCNKD),
            D(source.NgayHHGCNKD),
            ghiChu,
            B(source.TrangThai),
            sourceCreatedBy,
            sourceUpdatedBy,
            D(source.NgayTao),
            D(source.NgaySua),
            imagePathHash,
            sourceXml,
            D(source.ThoiGianTiepNhanXML),
        };
        var model = new VehicleSourceWriteModel(
            identity,
            normalizedPlate,
            HashFields(hashFields),
            route.SourceDatabaseName,
            maCsdt!,
            maSoGtvt!,
            identity.SourceBienSoXe,
            soDk,
            VehicleSourceNormalizer.NormalizeSecondaryCollisionKey(soDk),
            source.SoHuu,
            !source.SoHuu,
            source.SoHuu,
            nhanHieu,
            loaiXe,
            macXe,
            hangXe,
            hangGplxXe,
            mauXe,
            source.NamSX,
            soDongCo,
            VehicleSourceNormalizer.NormalizeSecondaryCollisionKey(soDongCo),
            soKhung,
            VehicleSourceNormalizer.NormalizeSecondaryCollisionKey(soKhung),
            source.GiayPhepXTL,
            soGpxTl,
            coQuanCapGpxTl,
            source.NgayCapGPXTL?.Date,
            source.NgayHHGPXTL?.Date,
            source.HeThongPP,
            source.BaoHiem,
            tuyenDuong,
            chatLuong,
            source.NgayCapGCNKD?.Date,
            source.NgayHHGCNKD?.Date,
            ghiChu,
            source.TrangThai,
            lifecycle,
            sourceCreatedBy,
            sourceUpdatedBy,
            source.NgayTao,
            source.NgaySua,
            imagePathHash,
            sourceXml,
            source.ThoiGianTiepNhanXML);
        return new VehicleMappingResult(model, Array.Empty<string>(), warnings);
    }

    public static string ComputeMappingFingerprint()
    {
        var fields = VehicleRealtimeTargetOwnership.SourceOwnedColumns
            .OrderBy(value => value, StringComparer.Ordinal)
            .Prepend("VEHICLE_REALTIME_MAPPING_V1")
            .Concat(
                [
                    "SOURCE:dbo.XeTap",
                    "IDENTITY:SourceProfileCode+exact-trimmed-BienSoXe",
                    "COLLISION:normalized-plate+SoDK+SoKhung+SoDongCo",
                    "IMAGE:hash-only-preserve-AnhRelativePath",
                    "MISSING:no-hard-delete",
                ]);
        return HashFields(fields);
    }

    private static VehicleMappingResult Blocked(string code)
        => new(null, [code], Array.Empty<string>());

    private static string? T(string? value) => VehicleSourceNormalizer.TrimToNull(value);

    private static void CheckLength(
        string field,
        string? value,
        ICollection<string> blockers)
    {
        if (value is not null && value.Length > TargetLengths[field])
        {
            blockers.Add($"{VehicleRealtimeReviewCodes.SourceValueTooLong}:{field}");
        }
    }

    private static string HashText(string value)
        => Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string HashFields(IEnumerable<string?> fields)
    {
        var canonical = string.Join(
            "|",
            fields.Select(value =>
            {
                var normalized = value ?? string.Empty;
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{normalized.Length}:{normalized}");
            }));
        return HashText(canonical);
    }

    private static string? B(bool? value)
        => value.HasValue ? value.Value ? "1" : "0" : null;

    private static string? I(int? value)
        => value?.ToString(CultureInfo.InvariantCulture);

    private static string? D(DateTime? value)
        => value?.ToString("O", CultureInfo.InvariantCulture);
}

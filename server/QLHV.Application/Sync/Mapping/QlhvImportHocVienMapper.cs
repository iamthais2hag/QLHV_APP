using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync.Mapping;

/// <summary>
/// Maps a CSDT snapshot row to the import-only App_HocVien payload, including
/// validated photo metadata. It intentionally uses a dedicated hash format.
/// </summary>
public static class QlhvImportHocVienMapper
{
    public const string UnsafePhotoPathCode = "QLHV_IMPORT_PHOTO_PATH_UNSAFE";
    public const string PhotoPathMapping =
        "NguoiLX_HoSo.DuongDanAnh -> App_HocVien.AnhRelativePath";

    public sealed record MapResult(
        QlhvImportHocVienWriteModel? Model,
        IReadOnlyList<HocVienDataWarningDto> Warnings,
        IReadOnlyList<string> Blockers,
        bool ShouldSkip)
    {
        public bool HasBlockers => Blockers.Count > 0;
    }

    public static MapResult MapAndValidate(
        V2HocVienSourceRow source,
        HocVienSourceIdentityContext sourceIdentity)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceIdentity);

        var baseResult = HocVienSyncMapper.MapAndValidate(source, sourceIdentity);
        if (baseResult.ShouldSkip || baseResult.Model is null)
        {
            return new MapResult(
                null,
                baseResult.Warnings,
                Array.Empty<string>(),
                ShouldSkip: true);
        }

        var baseModel = baseResult.Model;
        if (!TryNormalizePhotoPath(
                source.DuongDanAnh,
                baseModel.MaKhoa,
                baseModel.SourceMaDK,
                out var relativePhotoPath))
        {
            return new MapResult(
                null,
                baseResult.Warnings,
                new[]
                {
                    $"{UnsafePhotoPathCode}: Khong the map an toan cot {PhotoPathMapping}. " +
                    "DuongDanAnh phai nam duoi IM_GPLX va khop MaKhoa/SourceMaDK.",
                },
                ShouldSkip: false);
        }

        var modelWithoutHash = new QlhvImportHocVienWriteModel
        {
            SourceProfileCode = baseModel.SourceProfileCode,
            SourceMaDK = baseModel.SourceMaDK,
            SourceSystem = baseModel.SourceSystem,
            SourceVersion = baseModel.SourceVersion,
            MaDK = baseModel.MaDK,
            MaKhoa = baseModel.MaKhoa,
            TenKhoa = baseModel.TenKhoa,
            MaHangDT = baseModel.MaHangDT,
            HangGPLXHoc = baseModel.HangGPLXHoc,
            HoTen = baseModel.HoTen,
            NgaySinh = baseModel.NgaySinh,
            GioiTinh = baseModel.GioiTinh,
            SoCCCD = baseModel.SoCCCD,
            DiaChiThuongTru = baseModel.DiaChiThuongTru,
            SoGPLXDaCo = baseModel.SoGPLXDaCo,
            HangGPLXDaCo = baseModel.HangGPLXDaCo,
            NguoiNhanHoSo = baseModel.NguoiNhanHoSo,
            AnhRelativePath = relativePhotoPath,
            ChatLuongAnh = source.ChatLuongAnh,
            NgayThuNhanAnh = source.NgayThuNhanAnh,
            NguoiThuNhanAnh = Trim(source.NguoiThuNhanAnh),
            SourceOfTruth = baseModel.SourceOfTruth,
        };

        return new MapResult(
            CopyWithHash(modelWithoutHash, ComputeHash(modelWithoutHash)),
            baseResult.Warnings,
            Array.Empty<string>(),
            ShouldSkip: false);
    }

    private static bool TryNormalizePhotoPath(
        string? sourcePath,
        string? maKhoa,
        string sourceMaDk,
        out string? relativePath)
    {
        relativePath = null;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return true;
        }

        if (!IsSafeSegment(maKhoa) || !IsSafeSegment(sourceMaDk))
        {
            return false;
        }

        var segments = sourcePath
            .Trim()
            .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 3)
        {
            return false;
        }

        var imGplxIndex = Array.FindLastIndex(
            segments,
            value => string.Equals(value, "IM_GPLX", StringComparison.OrdinalIgnoreCase));
        if (imGplxIndex < 0 || imGplxIndex != segments.Length - 3)
        {
            return false;
        }

        var expectedFileName = sourceMaDk + ".jp2";
        if (!string.Equals(segments[^2], maKhoa, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(segments[^1], expectedFileName, StringComparison.OrdinalIgnoreCase) ||
            !IsSafeSegment(segments[^2]) ||
            !IsSafeSegment(segments[^1]))
        {
            return false;
        }

        relativePath = $"{maKhoa}/{expectedFileName}";
        return true;
    }

    private static bool IsSafeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed is not "." and not ".." &&
               !trimmed.Contains("..", StringComparison.Ordinal) &&
               !trimmed.Contains('\\') &&
               !trimmed.Contains('/') &&
               !trimmed.Contains(':') &&
               trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static string ComputeHash(QlhvImportHocVienWriteModel model)
    {
        var fields = new[]
        {
            N(model.MaDK),
            N(model.MaKhoa),
            N(model.TenKhoa),
            N(model.MaHangDT),
            N(model.HangGPLXHoc),
            N(model.HoTen),
            model.NgaySinh?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            N(model.GioiTinh),
            N(model.SoCCCD),
            N(model.DiaChiThuongTru),
            N(model.SoGPLXDaCo),
            N(model.HangGPLXDaCo),
            N(model.NguoiNhanHoSo),
            N(model.AnhRelativePath),
            model.ChatLuongAnh?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            model.NgayThuNhanAnh?.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture) ??
                string.Empty,
            N(model.NguoiThuNhanAnh),
            N(model.SourceOfTruth),
        };

        var canonical = string.Join("|", fields.Select(value => $"{value.Length}:{value}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static QlhvImportHocVienWriteModel CopyWithHash(
        QlhvImportHocVienWriteModel source,
        string hash)
        => new()
        {
            SourceProfileCode = source.SourceProfileCode,
            SourceMaDK = source.SourceMaDK,
            SourceSystem = source.SourceSystem,
            SourceVersion = source.SourceVersion,
            MaDK = source.MaDK,
            MaKhoa = source.MaKhoa,
            TenKhoa = source.TenKhoa,
            MaHangDT = source.MaHangDT,
            HangGPLXHoc = source.HangGPLXHoc,
            HoTen = source.HoTen,
            NgaySinh = source.NgaySinh,
            GioiTinh = source.GioiTinh,
            SoCCCD = source.SoCCCD,
            DiaChiThuongTru = source.DiaChiThuongTru,
            SoGPLXDaCo = source.SoGPLXDaCo,
            HangGPLXDaCo = source.HangGPLXDaCo,
            NguoiNhanHoSo = source.NguoiNhanHoSo,
            AnhRelativePath = source.AnhRelativePath,
            ChatLuongAnh = source.ChatLuongAnh,
            NgayThuNhanAnh = source.NgayThuNhanAnh,
            NguoiThuNhanAnh = source.NguoiThuNhanAnh,
            SourceOfTruth = source.SourceOfTruth,
            V2RowHash = hash,
        };

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string N(string? value) => value ?? string.Empty;
}

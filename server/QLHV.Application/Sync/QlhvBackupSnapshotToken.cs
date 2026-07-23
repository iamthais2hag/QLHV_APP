using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public static class QlhvBackupSnapshotToken
{
    public const string ExtendedPropertyName = "QLHV_BackupSnapshotToken";

    public static string CreateAfterRefresh(
        QlhvOperationSourceDefinition source,
        QlhvOperationRowCountsDto rows,
        DateTime completedAtUtc)
    {
        var nonce = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        return Hash(
            "refresh",
            source.SourceType,
            source.BackupDatabaseName,
            completedAtUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            rows.NguoiLX.ToString(CultureInfo.InvariantCulture),
            rows.NguoiLXHoSo.ToString(CultureInfo.InvariantCulture),
            rows.KhoaHoc.ToString(CultureInfo.InvariantCulture),
            nonce);
    }

    public static string CreateMetadataFallback(
        string databaseName,
        DateTime createDateUtc,
        QlhvOperationRowCountsDto rows)
        => Hash(
            "metadata",
            databaseName,
            createDateUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            rows.NguoiLX.ToString(CultureInfo.InvariantCulture),
            rows.NguoiLXHoSo.ToString(CultureInfo.InvariantCulture),
            rows.KhoaHoc.ToString(CultureInfo.InvariantCulture));

    public static string CreateImportMetadataFallback(
        string databaseName,
        DateTime createDateUtc,
        QlhvOperationRowCountsDto rows,
        int giaoVienRows,
        int khoaHocGiaoVienRows)
        => Hash(
            "metadata-import",
            databaseName,
            createDateUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            rows.NguoiLX.ToString(CultureInfo.InvariantCulture),
            rows.NguoiLXHoSo.ToString(CultureInfo.InvariantCulture),
            rows.KhoaHoc.ToString(CultureInfo.InvariantCulture),
            giaoVienRows.ToString(CultureInfo.InvariantCulture),
            khoaHocGiaoVienRows.ToString(CultureInfo.InvariantCulture));

    private static string Hash(params string[] values)
    {
        var canonical = string.Join("|", values);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

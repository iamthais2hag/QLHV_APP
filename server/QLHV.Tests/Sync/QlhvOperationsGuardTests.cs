using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Tests.Sync;

public sealed class QlhvOperationsGuardTests
{
    [Fact]
    public void Source_catalog_exposes_only_fixed_oto_and_moto_mappings()
    {
        var oto = QlhvOperationSourceCatalog.GetRequired("oto");
        Assert.Equal("OTO", oto.SourceType);
        Assert.Equal("CSDL_OTO", oto.LiveDatabaseName);
        Assert.Equal("CSDL_OTO_BAK", oto.BackupDatabaseName);
        Assert.Equal("66029", oto.MaCsdt);
        Assert.Equal("CSDT_OTO", oto.SourceProfileCode);
        Assert.Equal("CSDT_OTO_BAK", oto.BackupReadProfileCode);

        var moto = QlhvOperationSourceCatalog.GetRequired("MOTO");
        Assert.Equal("MOTO", moto.SourceType);
        Assert.Equal("CSDL_MOTO", moto.LiveDatabaseName);
        Assert.Equal("CSDL_MOTO_BAK", moto.BackupDatabaseName);
        Assert.Equal("66030", moto.MaCsdt);
        Assert.Equal("CSDT_MOTO", moto.SourceProfileCode);
        Assert.Equal("CSDT_MOTO_BAK", moto.BackupReadProfileCode);

        Assert.Equal(2, QlhvOperationSourceCatalog.All.Count);
        Assert.Throws<ArgumentException>(() => QlhvOperationSourceCatalog.GetRequired("CUSTOM"));
    }

    [Fact]
    public void Qlhv_write_contract_no_longer_accepts_operations_key_or_confirmation_text()
    {
        Assert.DoesNotContain(
            typeof(QlhvRefreshBackupRequest).GetProperties(),
            property => property.Name is "ConfirmText" or "OperationsKey");
        Assert.DoesNotContain(
            typeof(QlhvImportExecuteRequest).GetProperties(),
            property => property.Name is "ConfirmText" or "OperationsKey");
        Assert.DoesNotContain(
            typeof(QlhvOperationsOptions).GetProperties(),
            property => property.Name == "AdminKey");
    }

    [Fact]
    public void Metadata_snapshot_token_is_stable_but_refresh_token_is_unique()
    {
        var rows = new QlhvOperationRowCountsDto
        {
            NguoiLX = 46,
            NguoiLXHoSo = 46,
            KhoaHoc = 3,
        };
        var createdAt = new DateTime(2026, 7, 22, 1, 2, 3, DateTimeKind.Utc);

        var firstFallback = QlhvBackupSnapshotToken.CreateMetadataFallback(
            "CSDL_OTO_BAK",
            createdAt,
            rows);
        var secondFallback = QlhvBackupSnapshotToken.CreateMetadataFallback(
            "CSDL_OTO_BAK",
            createdAt,
            rows);
        Assert.Equal(firstFallback, secondFallback);

        var source = QlhvOperationSourceCatalog.GetRequired("OTO");
        var firstRefresh = QlhvBackupSnapshotToken.CreateAfterRefresh(source, rows, createdAt);
        var secondRefresh = QlhvBackupSnapshotToken.CreateAfterRefresh(source, rows, createdAt);
        Assert.NotEqual(firstRefresh, secondRefresh);
        Assert.NotEqual(firstFallback, firstRefresh);
    }
}

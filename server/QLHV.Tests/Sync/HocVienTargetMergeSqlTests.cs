using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class HocVienTargetMergeSqlTests
{
    [Fact]
    public void Staging_table_uses_source_identity_primary_key()
    {
        Assert.Contains("SourceProfileCode NVARCHAR(50)   NOT NULL", HocVienTargetMergeSql.CreateStagingTable);
        Assert.Contains("SourceMaDK        NVARCHAR(50)   NOT NULL", HocVienTargetMergeSql.CreateStagingTable);
        Assert.Contains("SourceSystem      NVARCHAR(50)   NOT NULL", HocVienTargetMergeSql.CreateStagingTable);
        Assert.Contains("SourceVersion     NVARCHAR(50)   NULL", HocVienTargetMergeSql.CreateStagingTable);
        Assert.Contains("PRIMARY KEY (SourceProfileCode, SourceMaDK)", HocVienTargetMergeSql.CreateStagingTable);
    }

    [Fact]
    public void Merge_uses_source_identity_not_madk_only()
    {
        Assert.Contains(
            "ON tgt.SourceProfileCode = src.SourceProfileCode",
            HocVienTargetMergeSql.MergeStatement);
        Assert.Contains(
            "AND tgt.SourceMaDK = src.SourceMaDK",
            HocVienTargetMergeSql.MergeStatement);
        Assert.DoesNotContain(
            "ON tgt.MaDK = src.MaDK",
            HocVienTargetMergeSql.MergeStatement);
    }

    [Fact]
    public void Merge_inserts_source_identity_columns()
    {
        Assert.Contains(
            "SourceProfileCode, SourceMaDK, SourceSystem, SourceVersion",
            HocVienTargetMergeSql.MergeStatement);
        Assert.Contains(
            "src.SourceProfileCode, src.SourceMaDK, src.SourceSystem, src.SourceVersion",
            HocVienTargetMergeSql.MergeStatement);
    }
}


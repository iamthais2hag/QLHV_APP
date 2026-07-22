using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Tests.Sync;

public sealed class QlhvImportHocVienMapperTests
{
    private static readonly HocVienSourceIdentityContext OtoIdentity = new("CSDT_OTO", "V2");

    [Fact]
    public void Blank_photo_path_maps_to_null_without_a_blocker()
    {
        var result = QlhvImportHocVienMapper.MapAndValidate(
            Source(duongDanAnh: "  "),
            OtoIdentity);

        Assert.False(result.ShouldSkip);
        Assert.False(result.HasBlockers);
        Assert.NotNull(result.Model);
        Assert.Null(result.Model!.AnhRelativePath);
    }

    [Theory]
    [InlineData(@"D:\IM_GPLX\66029K01\66029-001.jp2")]
    [InlineData("/mnt/photos/IM_GPLX/66029K01/66029-001.jp2")]
    [InlineData(@"IM_GPLX\66029K01\66029-001.JP2")]
    public void Photo_path_under_im_gplx_is_stored_as_a_canonical_relative_path(string sourcePath)
    {
        var result = QlhvImportHocVienMapper.MapAndValidate(
            Source(duongDanAnh: sourcePath),
            OtoIdentity);

        Assert.False(result.HasBlockers);
        Assert.NotNull(result.Model);
        Assert.Equal("66029K01/66029-001.jp2", result.Model!.AnhRelativePath);
    }

    [Theory]
    [InlineData(@"D:\OTHER\66029K01\66029-001.jp2")]
    [InlineData(@"D:\IM_GPLX\OTHER-KHOA\66029-001.jp2")]
    [InlineData(@"D:\IM_GPLX\66029K01\OTHER-MADK.jp2")]
    [InlineData(@"D:\IM_GPLX\nested\66029K01\66029-001.jp2")]
    public void Unsafe_or_mismatched_photo_path_returns_a_named_blocker(string sourcePath)
    {
        var result = QlhvImportHocVienMapper.MapAndValidate(
            Source(duongDanAnh: sourcePath),
            OtoIdentity);

        Assert.False(result.ShouldSkip);
        Assert.True(result.HasBlockers);
        Assert.Null(result.Model);
        var blocker = Assert.Single(result.Blockers);
        Assert.Contains(QlhvImportHocVienMapper.PhotoPathMapping, blocker, StringComparison.Ordinal);
    }

    [Fact]
    public void Photo_metadata_is_mapped_and_trimmed()
    {
        var capturedAt = new DateTime(2026, 7, 22, 8, 9, 10, DateTimeKind.Unspecified);
        var result = QlhvImportHocVienMapper.MapAndValidate(
            Source(
                duongDanAnh: @"D:\IM_GPLX\66029K01\66029-001.jp2",
                chatLuongAnh: 95,
                ngayThuNhanAnh: capturedAt,
                nguoiThuNhanAnh: "  nhan vien anh  "),
            OtoIdentity);

        Assert.NotNull(result.Model);
        Assert.Equal(95, result.Model!.ChatLuongAnh);
        Assert.Equal(capturedAt, result.Model.NgayThuNhanAnh);
        Assert.Equal("nhan vien anh", result.Model.NguoiThuNhanAnh);
    }

    [Fact]
    public void Import_hash_is_stable_and_changes_with_photo_fields()
    {
        var capturedAt = new DateTime(2026, 7, 22, 8, 9, 10, 123, DateTimeKind.Unspecified);
        var withoutPhoto = Map(Source());
        var withPhoto = Map(Source(
            duongDanAnh: @"D:\IM_GPLX\66029K01\66029-001.jp2",
            chatLuongAnh: 90,
            ngayThuNhanAnh: capturedAt,
            nguoiThuNhanAnh: "user-a"));
        var changedQuality = Map(Source(
            duongDanAnh: @"D:\IM_GPLX\66029K01\66029-001.jp2",
            chatLuongAnh: 91,
            ngayThuNhanAnh: capturedAt,
            nguoiThuNhanAnh: "user-a"));
        var changedCapturedAt = Map(Source(
            duongDanAnh: @"D:\IM_GPLX\66029K01\66029-001.jp2",
            chatLuongAnh: 90,
            ngayThuNhanAnh: capturedAt.AddSeconds(1),
            nguoiThuNhanAnh: "user-a"));
        var changedUser = Map(Source(
            duongDanAnh: @"D:\IM_GPLX\66029K01\66029-001.jp2",
            chatLuongAnh: 90,
            ngayThuNhanAnh: capturedAt,
            nguoiThuNhanAnh: "user-b"));
        var sameAgain = Map(Source(
            duongDanAnh: @"D:\IM_GPLX\66029K01\66029-001.jp2",
            chatLuongAnh: 90,
            ngayThuNhanAnh: capturedAt,
            nguoiThuNhanAnh: "user-a"));

        Assert.NotEqual(withoutPhoto.V2RowHash, withPhoto.V2RowHash);
        Assert.NotEqual(withPhoto.V2RowHash, changedQuality.V2RowHash);
        Assert.NotEqual(withPhoto.V2RowHash, changedCapturedAt.V2RowHash);
        Assert.NotEqual(withPhoto.V2RowHash, changedUser.V2RowHash);
        Assert.Equal(withPhoto.V2RowHash, sameAgain.V2RowHash);
    }

    [Fact]
    public void Mapper_preserves_the_requested_logical_source_identity()
    {
        var result = QlhvImportHocVienMapper.MapAndValidate(Source(), OtoIdentity);

        Assert.NotNull(result.Model);
        Assert.Equal("CSDT_OTO", result.Model!.SourceProfileCode);
        Assert.Equal("66029-001", result.Model.SourceMaDK);
        Assert.Equal("V2", result.Model.SourceSystem);
    }

    private static QlhvImportHocVienWriteModel Map(V2HocVienSourceRow source)
    {
        var result = QlhvImportHocVienMapper.MapAndValidate(source, OtoIdentity);
        Assert.False(result.HasBlockers);
        return Assert.IsType<QlhvImportHocVienWriteModel>(result.Model);
    }

    private static V2HocVienSourceRow Source(
        string? duongDanAnh = null,
        int? chatLuongAnh = null,
        DateTime? ngayThuNhanAnh = null,
        string? nguoiThuNhanAnh = null)
        => new()
        {
            MaDK = "66029-001",
            MaKhoaHoc = "66029K01",
            TenKH = "Khoa OTO",
            HangDaoTao = "B2",
            TenHangDT = "Hang B2",
            HoVaTen = "Nguyen Van A",
            NgaySinh = new DateTime(1990, 1, 2),
            SoCMT = "001234567890",
            GioiTinh = "M",
            NoiTT = "Dia chi",
            SoGPLXDaCo = "GPLX-1",
            HangGPLXDaCo = "A1",
            NguoiNhanHoSo = "Nhan vien",
            DuongDanAnh = duongDanAnh,
            ChatLuongAnh = chatLuongAnh,
            NgayThuNhanAnh = ngayThuNhanAnh,
            NguoiThuNhanAnh = nguoiThuNhanAnh,
        };
}

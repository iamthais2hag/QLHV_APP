using QLHV.Application.Sync.TeacherVehicleProjection;

namespace QLHV.Tests.Sync;

public sealed class TeacherVehicleProjectionMappingTests
{
    [Fact]
    public void Course_vehicle_uses_profile_and_source_schedule_as_stable_identity()
    {
        var result = QlhvKhoaHocXeTapMapper.Map(
            new QlhvKhoaHocXeTapSourceRow
            {
                MaLichSD = 17,
                MaKH = "KH-01",
                BienSoXe = "66A12345",
                MaGV = "GV01",
                TenGV = "Teacher",
                TrangThai = true,
                IsKhoaHocXeTap = true,
            },
            "CSDT_OTO");

        Assert.True(result.IsSafe);
        Assert.Equal("CSDT_OTO", result.Model!.SourceProfileCode);
        Assert.Equal(17, result.Model.SourceMaLichSD);
        Assert.Equal("CSDT_OTO:GV01", result.Model.MaGV);
        Assert.Equal(64, result.Model.SourceHash.Length);
    }

    [Theory]
    [InlineData(0, "KH-01", "66A12345", "COURSE_VEHICLE_IDENTITY_INVALID")]
    [InlineData(1, "", "66A12345", "COURSE_VEHICLE_COURSE_MISSING")]
    [InlineData(1, "KH-01", "", "COURSE_VEHICLE_PLATE_MISSING")]
    public void Course_vehicle_invalid_source_identity_is_blocked(
        int scheduleId,
        string course,
        string plate,
        string expected)
    {
        var result = QlhvKhoaHocXeTapMapper.Map(
            new QlhvKhoaHocXeTapSourceRow
            {
                MaLichSD = scheduleId,
                MaKH = course,
                BienSoXe = plate,
            },
            "CSDT_OTO");

        Assert.False(result.IsSafe);
        Assert.Contains(expected, result.Blockers);
    }

    [Fact]
    public void Mapping_fingerprint_is_deterministic()
    {
        Assert.Equal(
            QlhvKhoaHocXeTapMapper.MappingFingerprint(),
            QlhvKhoaHocXeTapMapper.MappingFingerprint());
        Assert.Equal(64, QlhvKhoaHocXeTapMapper.MappingFingerprint().Length);
    }
}

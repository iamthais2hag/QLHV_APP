using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace QLHV.Application.Sync.TeacherVehicleProjection;

public static class TeacherVehicleProjectionDomains
{
    public const string Teacher = "TEACHER";
    public const string Vehicle = "VEHICLE";
    public const string CourseTeacher = "COURSE_TEACHER";
    public const string CourseVehicle = "COURSE_VEHICLE";
    public const string ContractVersion = "TVP_V1";

    public static IReadOnlyList<string> Ordered { get; } =
        [Teacher, Vehicle, CourseTeacher, CourseVehicle];
}

public sealed record TeacherVehicleProjectionBacklog(
    string SourceProfileCode,
    long SourceCurrentVersion,
    IReadOnlyDictionary<string, long> DomainCheckpoints)
{
    public bool HasPending => DomainCheckpoints.Values.Any(value => value < SourceCurrentVersion);
}

public sealed record TeacherVehicleProjectionDomainResult(
    string Domain,
    long CheckpointBefore,
    long CheckpointAfter,
    int SourceRows,
    int InsertedRows,
    int UpdatedRows,
    int InactiveRows,
    int NoChangeRows,
    string VerificationHash,
    string Outcome);

public sealed record TeacherVehicleProjectionCycleResult(
    string SourceProfileCode,
    IReadOnlyList<TeacherVehicleProjectionDomainResult> Domains)
{
    public bool Mutated => Domains.Any(item =>
        item.InsertedRows + item.UpdatedRows + item.InactiveRows > 0);

    public string Outcome => Mutated ? "PROJECTION_APPLIED" : "PROJECTION_NO_CHANGE";
}

public sealed record TeacherVehicleProjectionBootstrapRequest(
    Guid BootstrapId,
    string SourceProfileCode,
    string ArtifactSha256);

public interface ITeacherVehicleProjectionCoordinator
{
    Task<TeacherVehicleProjectionBacklog> ReadBacklogAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken = default);

    Task<TeacherVehicleProjectionCycleResult> ProcessPendingAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken = default);

    Task<TeacherVehicleProjectionCycleResult> BootstrapAsync(
        TeacherVehicleProjectionBootstrapRequest request,
        CancellationToken cancellationToken = default);
}

public record QlhvKhoaHocXeTapSourceRow
{
    public int MaLichSD { get; init; }
    public string MaKH { get; init; } = string.Empty;
    public string BienSoXe { get; init; } = string.Empty;
    public string? MaGV { get; init; }
    public string? MaHV { get; init; }
    public string? DiaDiem { get; init; }
    public string? GhiChu { get; init; }
    public bool TrangThai { get; init; }
    public DateTime? NgayBD { get; init; }
    public DateTime? NgayKT { get; init; }
    public bool IsKhoaHocXeTap { get; init; }
    public string? TenHV { get; init; }
    public string? TenGV { get; init; }
}

public sealed record QlhvKhoaHocXeTapWriteModel(
    string SourceProfileCode,
    long SourceMaLichSD,
    string SourceMaKhoaHoc,
    string SourceBienSoXe,
    string SourceHash,
    string MaKhoa,
    string BienSoXe,
    string? MaGV,
    string? SourceMaHocVien,
    string? DiaDiem,
    string? TenHocVien,
    string? TenGV,
    DateTime? NgayBatDau,
    DateTime? NgayKetThuc,
    string? GhiChu,
    bool IsKhoaHocXeTap,
    bool TrangThaiNguon);

public sealed record QlhvKhoaHocXeTapMapResult(
    QlhvKhoaHocXeTapWriteModel? Model,
    IReadOnlyList<string> Blockers)
{
    public bool IsSafe => Model is not null && Blockers.Count == 0;
}

public static class QlhvKhoaHocXeTapMapper
{
    public static QlhvKhoaHocXeTapMapResult Map(
        QlhvKhoaHocXeTapSourceRow source,
        string sourceProfileCode)
    {
        ArgumentNullException.ThrowIfNull(source);
        var blockers = new List<string>();
        var profile = Trim(sourceProfileCode);
        var course = Trim(source.MaKH);
        var plate = Trim(source.BienSoXe);
        var sourceTeacher = Trim(source.MaGV);
        if (profile is not ("CSDT_OTO" or "CSDT_MOTO"))
            blockers.Add("COURSE_VEHICLE_PROFILE_UNSUPPORTED");
        if (source.MaLichSD <= 0) blockers.Add("COURSE_VEHICLE_IDENTITY_INVALID");
        if (course is null) blockers.Add("COURSE_VEHICLE_COURSE_MISSING");
        if (plate is null) blockers.Add("COURSE_VEHICLE_PLATE_MISSING");
        if (plate is { Length: > 10 }) blockers.Add("COURSE_VEHICLE_PLATE_TOO_LONG");
        if (blockers.Count != 0)
            return new(null, blockers);

        var targetTeacher = sourceTeacher is null
            ? null
            : $"{profile!.ToUpperInvariant()}:{sourceTeacher}";
        if (targetTeacher is { Length: > 20 })
            return new(null, ["COURSE_VEHICLE_TEACHER_KEY_TOO_LONG"]);

        var fields = new string?[]
        {
            course, plate, targetTeacher, Trim(source.MaHV), Trim(source.DiaDiem),
            Trim(source.TenHV), Trim(source.TenGV),
            D(source.NgayBD), D(source.NgayKT), Trim(source.GhiChu),
            source.IsKhoaHocXeTap ? "1" : "0",
            source.TrangThai ? "1" : "0",
        };
        return new(
            new QlhvKhoaHocXeTapWriteModel(
                profile!, source.MaLichSD, course!, plate!, Hash(fields),
                course!, plate!, targetTeacher, Trim(source.MaHV), Trim(source.DiaDiem),
                Trim(source.TenHV), Trim(source.TenGV),
                source.NgayBD?.Date, source.NgayKT?.Date, Trim(source.GhiChu),
                source.IsKhoaHocXeTap, source.TrangThai),
            Array.Empty<string>());
    }

    public static string MappingFingerprint()
        => Hash(
            [
                "COURSE_VEHICLE_MAPPING_V1",
                "IDENTITY:SourceProfileCode+MaLichSD",
                "COURSE:exact-MaKH",
                "VEHICLE:exact-BienSoXe",
                "TEACHER:profile-prefixed-MaGV",
                "LEARNER:exact-source-MaHV",
                "DELETE:soft-inactive",
            ]);

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? D(DateTime? value)
        => value?.ToString("O", CultureInfo.InvariantCulture);

    private static string Hash(IEnumerable<string?> values)
    {
        var canonical = string.Join("|", values.Select(value =>
        {
            var normalized = value ?? string.Empty;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{normalized.Length}:{normalized}");
        }));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}

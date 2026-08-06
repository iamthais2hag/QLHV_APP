namespace QLHV.Application.Sync.Realtime.ControlPlane;

/// <summary>
/// Identifies the SQL Server equality contract used by the typed ownership
/// claim relation. The supporting varbinary token is never authoritative.
/// </summary>
public static class TargetEqualityProof
{
    public const ushort Version = 1;

    public const string ProofId =
        "TYPED_OWNER_SQLSERVER_SQL_LATIN1_GENERAL_CP1_CI_AS_V1";

    public const string ProofStatus = "TYPED_CLAIM";

    public const string Collation = "SQL_Latin1_General_CP1_CI_AS";
}

/// <summary>
/// A fixed, table-specific target key. Values are kept in their source form
/// and are converted to the exact target SQL types by the repository.
/// </summary>
public sealed class TypedTargetKeyClaim
{
    private TypedTargetKeyClaim(
        string tableName,
        string? dmDonViGtvtMaDv = null,
        string? giaoVienMaGv = null,
        string? khoaHocMaKh = null,
        int? khoaHocGiaoVienMaLichLv = null,
        string? baoCaoIMaBci = null,
        string? nguoiLxMaDk = null,
        string? nguoiLxHoSoMaDk = null,
        int? giayToMaGt = null,
        string? giayToMaDk = null)
    {
        TableName = tableName;
        DmDonViGtvtMaDv = dmDonViGtvtMaDv;
        GiaoVienMaGv = giaoVienMaGv;
        KhoaHocMaKh = khoaHocMaKh;
        KhoaHocGiaoVienMaLichLv = khoaHocGiaoVienMaLichLv;
        BaoCaoIMaBci = baoCaoIMaBci;
        NguoiLxMaDk = nguoiLxMaDk;
        NguoiLxHoSoMaDk = nguoiLxHoSoMaDk;
        GiayToMaGt = giayToMaGt;
        GiayToMaDk = giayToMaDk;
    }

    public string TableName { get; }

    public string? DmDonViGtvtMaDv { get; }

    public string? GiaoVienMaGv { get; }

    public string? KhoaHocMaKh { get; }

    public int? KhoaHocGiaoVienMaLichLv { get; }

    public string? BaoCaoIMaBci { get; }

    public string? NguoiLxMaDk { get; }

    public string? NguoiLxHoSoMaDk { get; }

    public int? GiayToMaGt { get; }

    public string? GiayToMaDk { get; }

    public static TypedTargetKeyClaim ForDmDonViGtvt(string maDv)
        => new(
            "DM_DonViGTVT",
            dmDonViGtvtMaDv: ValidateText(maDv, 6, nameof(maDv)));

    public static TypedTargetKeyClaim ForGiaoVien(string maGv)
        => new(
            "GiaoVien",
            giaoVienMaGv: ValidateText(maGv, 8, nameof(maGv)));

    public static TypedTargetKeyClaim ForKhoaHoc(string maKh)
        => new(
            "KhoaHoc",
            khoaHocMaKh: ValidateText(maKh, 13, nameof(maKh)));

    public static TypedTargetKeyClaim ForKhoaHocGiaoVien(int maLichLv)
        => new("KhoaHoc_GiaoVien", khoaHocGiaoVienMaLichLv: maLichLv);

    public static TypedTargetKeyClaim ForBaoCaoI(string maBci)
        => new(
            "BaoCaoI",
            baoCaoIMaBci: ValidateText(maBci, 18, nameof(maBci)));

    public static TypedTargetKeyClaim ForNguoiLx(string maDk)
        => new(
            "NguoiLX",
            nguoiLxMaDk: ValidateText(maDk, 25, nameof(maDk)));

    public static TypedTargetKeyClaim ForNguoiLxHoSo(string maDk)
        => new(
            "NguoiLX_HoSo",
            nguoiLxHoSoMaDk: ValidateText(maDk, 25, nameof(maDk)));

    public static TypedTargetKeyClaim ForNguoiLxHsGiayTo(int maGt, string maDk)
        => new(
            "NguoiLXHS_GiayTo",
            giayToMaGt: maGt,
            giayToMaDk: ValidateText(maDk, 25, nameof(maDk)));

    public void ValidateForRoute(MembershipRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        CsdtControlPlaneCatalog.ValidateRoute(route);
        if (!string.Equals(TableName, route.TableName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Typed target key shape does not match the fixed route table.",
                nameof(route));
        }
    }

    public void ValidateForTargetProfile(string targetProfile)
    {
        if (targetProfile is not (
                "OTO_V1" or
                "OTO_V1_BAK" or
                "MOTO_V1" or
                "MOTO_V1_BAK"))
        {
            throw new ArgumentException(
                "Target profile is outside the fixed typed-claim allowlist.",
                nameof(targetProfile));
        }
    }

    public override string ToString()
        => $"TypedTargetKeyClaim(Table={TableName}, ProofVersion={TargetEqualityProof.Version}, Redacted=true)";

    private static string ValidateText(string value, int maxLength, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Typed target key exceeds varchar({maxLength}).");
        }

        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Typed target key cannot contain the undefined Windows-collation NUL character.",
                parameterName);
        }

        return value;
    }
}

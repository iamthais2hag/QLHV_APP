using QLHV.Application.Sync.Realtime;

namespace QLHV.Infrastructure.Sync.Realtime;

internal enum CsdtRealtimeDomainGroup
{
    Reference,
    Course,
    Learner,
}

internal sealed record CsdtRealtimeDomainDefinition(
    string Name,
    string TableName,
    IReadOnlyList<string> KeyColumns,
    string PartitionPredicate,
    CsdtRealtimeDomainGroup Group,
    bool IsOptional = false,
    IReadOnlyList<string>? IdentityCollisionGuardColumns = null)
{
    public string QualifiedTableName => $"dbo.[{TableName}]";
}

internal static class CsdtRealtimeDomainCatalog
{
    public static IReadOnlyList<CsdtRealtimeDomainDefinition> Ordered { get; } =
    [
        new(
            "DM_DonViGTVT",
            "DM_DonViGTVT",
            ["MaDV"],
            "CONVERT(varbinary(8000), src.[MaDV]) = CONVERT(varbinary(8000), @MaCSDT)",
            CsdtRealtimeDomainGroup.Reference),
        new(
            "GiaoVien",
            "GiaoVien",
            ["MaGV"],
            "CONVERT(varbinary(8000), src.[MaCSDT]) = CONVERT(varbinary(8000), @MaCSDT)",
            CsdtRealtimeDomainGroup.Course,
            IsOptional: true),
        new(
            "KhoaHoc",
            "KhoaHoc",
            ["MaKH"],
            "CONVERT(varbinary(8000), src.[MaCSDT]) = CONVERT(varbinary(8000), @MaCSDT)",
            CsdtRealtimeDomainGroup.Course),
        new(
            "KhoaHoc_GiaoVien",
            "KhoaHoc_GiaoVien",
            ["MaLichLV"],
            """
            EXISTS
            (
                SELECT 1
                FROM dbo.[KhoaHoc] AS kh
                WHERE CONVERT(varbinary(8000), kh.[MaKH]) = CONVERT(varbinary(8000), src.[MaKH])
                  AND CONVERT(varbinary(8000), kh.[MaCSDT]) = CONVERT(varbinary(8000), @MaCSDT)
            )
            """,
            CsdtRealtimeDomainGroup.Course,
            IsOptional: true,
            IdentityCollisionGuardColumns: ["MaKH", "MaGV"]),
        new(
            "BaoCaoI",
            "BaoCaoI",
            ["MaBCI"],
            "CONVERT(varbinary(8000), src.[MaCSDT]) = CONVERT(varbinary(8000), @MaCSDT)",
            CsdtRealtimeDomainGroup.Course),
        new(
            "NguoiLX",
            "NguoiLX",
            ["MaDK"],
            """
            CONVERT(varbinary(8000), src.[DonViNhanHSo]) = CONVERT(varbinary(8000), @MaCSDT)
            OR CONVERT(varbinary(8000), LEFT(src.[MaDK], 6)) =
               CONVERT(varbinary(8000), @MaCSDT + '-')
            """,
            CsdtRealtimeDomainGroup.Learner),
        new(
            "NguoiLX_HoSo",
            "NguoiLX_HoSo",
            ["MaDK"],
            """
            CONVERT(varbinary(8000), src.[MaCSDT]) = CONVERT(varbinary(8000), @MaCSDT)
            OR CONVERT(varbinary(8000), LEFT(src.[MaDK], 6)) =
               CONVERT(varbinary(8000), @MaCSDT + '-')
            """,
            CsdtRealtimeDomainGroup.Learner),
        new(
            "NguoiLX_GPLX",
            "NguoiLX_GPLX",
            ["MaDK"],
            """
            EXISTS
            (
                SELECT 1
                FROM dbo.[NguoiLX_HoSo] AS hs
                WHERE CONVERT(varbinary(8000), hs.[MaDK]) = CONVERT(varbinary(8000), src.[MaDK])
                  AND
                  (
                      CONVERT(varbinary(8000), hs.[MaCSDT]) = CONVERT(varbinary(8000), @MaCSDT)
                      OR CONVERT(varbinary(8000), LEFT(hs.[MaDK], 6)) =
                         CONVERT(varbinary(8000), @MaCSDT + '-')
                  )
            )
            """,
            CsdtRealtimeDomainGroup.Learner),
        new(
            "NguoiLXHS_GiayTo",
            "NguoiLXHS_GiayTo",
            ["MaGT", "MaDK"],
            """
            EXISTS
            (
                SELECT 1
                FROM dbo.[NguoiLX_HoSo] AS hs
                WHERE CONVERT(varbinary(8000), hs.[MaDK]) = CONVERT(varbinary(8000), src.[MaDK])
                  AND
                  (
                      CONVERT(varbinary(8000), hs.[MaCSDT]) = CONVERT(varbinary(8000), @MaCSDT)
                      OR CONVERT(varbinary(8000), LEFT(hs.[MaDK], 6)) =
                         CONVERT(varbinary(8000), @MaCSDT + '-')
                  )
            )
            """,
            CsdtRealtimeDomainGroup.Learner),
    ];

    private static readonly IReadOnlyDictionary<string, CsdtRealtimeDomainDefinition> ByName =
        Ordered.ToDictionary(item => item.Name, StringComparer.Ordinal);

    static CsdtRealtimeDomainCatalog()
    {
        var forbidden = Ordered
            .Where(domain => CsdtRealtimeColumnOwnershipPolicy.V1OwnedDomains.Contains(domain.Name))
            .Select(domain => domain.Name)
            .ToArray();
        if (forbidden.Length != 0)
        {
            throw new InvalidOperationException(
                "Forward catalog contains V1-owned domains: " + string.Join(", ", forbidden));
        }
    }

    public static CsdtRealtimeDomainDefinition GetRequired(string domain)
    {
        if (CsdtRealtimeColumnOwnershipPolicy.V1OwnedDomains.Contains(domain))
        {
            throw new ArgumentException(
                "Realtime domain is V1-owned and excluded from the forward catalog.",
                nameof(domain));
        }

        return ByName.TryGetValue(domain, out var definition)
            ? definition
            : throw new ArgumentException("Realtime domain is not in the fixed allowlist.", nameof(domain));
    }

    public static IReadOnlyList<CsdtRealtimeDomainDefinition> Group(CsdtRealtimeDomainGroup group)
        => Ordered.Where(item => item.Group == group).ToArray();
}

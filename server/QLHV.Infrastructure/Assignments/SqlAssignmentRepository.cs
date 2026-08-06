using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Assignments;
using QLHV.Application.Sync.Connections;
using QLHV.Shared.Paging;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Infrastructure.Assignments;

/// <summary>
/// SQL Server implementation for the QLHV-owned assignment domain. Source
/// masters are queried read-only; every assignment mutation is a locked,
/// close-and-insert full-snapshot transaction.
/// </summary>
public sealed partial class SqlAssignmentRepository : IAssignmentRepository
{
    private const string Ready = "READY";
    private const string NoChange = "NO_CHANGE";
    private const string Conflict = "CONFLICT";
    private const string Invalid = "INVALID";
    private readonly IConnectionSettingsProvider _connections;
    private readonly AppSyncOptions _options;

    public SqlAssignmentRepository(
        IConnectionSettingsProvider connections,
        IOptions<AppSyncOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task<PagedResult<SourceTeacherItem>> SearchTeachersAsync(
        CatalogSearchRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = request.Normalize();
        await using var connection = await OpenAsync(cancellationToken);
        const string where = """
            g.IsDeleted = 0
            AND (@Keyword IS NULL OR g.MaGV LIKE N'%' + @Keyword + N'%'
                 OR g.HoTen LIKE N'%' + @Keyword + N'%')
            AND (@SourceProfileCode IS NULL OR g.SourceProfileCode = @SourceProfileCode)
            AND (@TrangThai IS NULL
                 OR (@TrangThai=N'ACTIVE' AND COALESCE(g.TrangThaiNguon,1)=1
                     AND UPPER(COALESCE(g.LastSyncStatus,N'')) NOT IN (N'MANUAL_REVIEW',N'CONFLICT'))
                 OR (@TrangThai=N'INACTIVE' AND COALESCE(g.TrangThaiNguon,1)=0)
                 OR (@TrangThai=N'MANUAL_REVIEW'
                     AND UPPER(COALESCE(g.LastSyncStatus,N'')) IN (N'MANUAL_REVIEW',N'CONFLICT'))
                 OR (@TrangThai NOT IN (N'ACTIVE',N'INACTIVE',N'MANUAL_REVIEW')
                     AND UPPER(COALESCE(g.TrangThai,N''))=@TrangThai))
            """;
        var args = new
        {
            normalized.Keyword,
            normalized.SourceProfileCode,
            normalized.TrangThai,
            Offset = (normalized.Page - 1) * normalized.PageSize,
            normalized.PageSize,
        };
        var total = await connection.ExecuteScalarAsync<int>(Command(
            $"SELECT COUNT_BIG(1) FROM dbo.App_GiaoVien g WHERE {where};",
            args,
            cancellationToken));
        if (total == 0)
        {
            return PagedResult<SourceTeacherItem>.Empty(normalized.Page, normalized.PageSize);
        }

        var assignmentReadSchemaAvailable = await HasAssignmentReadSchemaAsync(
            connection,
            cancellationToken);
        var rows = await connection.QueryAsync<TeacherRow>(Command(assignmentReadSchemaAvailable
            ? $"""
            SELECT g.GiaoVienId,
                   COALESCE(g.SourceProfileCode, N'') AS SourceProfileCode,
                   g.MaGV,
                   g.HoTen,
                   g.NgaySinh,
                   g.SoCCCD,
                   g.CacHangGPLXDuocDaoTao AS HangDaoTao,
                   COALESCE(g.TrangThai, CASE WHEN COALESCE(g.TrangThaiNguon,1)=1 THEN N'ACTIVE' ELSE N'INACTIVE' END) AS TrangThai,
                   CONVERT(bit, CASE WHEN COALESCE(g.TrangThaiNguon,1)=1 THEN 1 ELSE 0 END) AS IsActive,
                   (SELECT COUNT_BIG(1) FROM dbo.App_KhoaHoc_GiaoVien kg
                     WHERE kg.IsDeleted=0 AND kg.MaGV=g.MaGV
                       AND (kg.SourceProfileCode=g.SourceProfileCode OR kg.SourceProfileCode IS NULL)) AS CourseUsageCount,
                   (SELECT COUNT_BIG(1) FROM dbo.App_HocVien_PhanCong pc
                     WHERE pc.IsCurrent=1 AND pc.GiaoVienDungLopId=g.GiaoVienId) AS StudentUsageCount,
                   CONVERT(bit, CASE WHEN COALESCE(g.LastSyncStatus,N'') IN (N'MANUAL_REVIEW',N'CONFLICT')
                                          OR COALESCE(g.TrangThaiNguon,1)=0 THEN 1 ELSE 0 END) AS IsManualReview
            FROM dbo.App_GiaoVien g
            WHERE {where}
            ORDER BY g.HoTen, g.MaGV, g.GiaoVienId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """
            : $"""
            SELECT g.GiaoVienId,
                   COALESCE(g.SourceProfileCode, N'') AS SourceProfileCode,
                   g.MaGV,
                   g.HoTen,
                   g.NgaySinh,
                   g.SoCCCD,
                   g.CacHangGPLXDuocDaoTao AS HangDaoTao,
                   COALESCE(g.TrangThai, CASE WHEN COALESCE(g.TrangThaiNguon,1)=1 THEN N'ACTIVE' ELSE N'INACTIVE' END) AS TrangThai,
                   CONVERT(bit, CASE WHEN COALESCE(g.TrangThaiNguon,1)=1 THEN 1 ELSE 0 END) AS IsActive,
                   (SELECT COUNT_BIG(1) FROM dbo.App_KhoaHoc_GiaoVien kg
                     WHERE kg.IsDeleted=0 AND kg.MaGV=g.MaGV
                       AND (kg.SourceProfileCode=g.SourceProfileCode OR kg.SourceProfileCode IS NULL)) AS CourseUsageCount,
                   CONVERT(int,0) AS StudentUsageCount,
                   CONVERT(bit, CASE WHEN COALESCE(g.LastSyncStatus,N'') IN (N'MANUAL_REVIEW',N'CONFLICT')
                                          OR COALESCE(g.TrangThaiNguon,1)=0 THEN 1 ELSE 0 END) AS IsManualReview
            FROM dbo.App_GiaoVien g
            WHERE {where}
            ORDER BY g.HoTen, g.MaGV, g.GiaoVienId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, args, cancellationToken));
        return new PagedResult<SourceTeacherItem>
        {
            Items = rows.Select(ToTeacher).ToArray(),
            Page = normalized.Page,
            PageSize = normalized.PageSize,
            TotalItems = total,
        };
    }

    public async Task<PagedResult<SourceVehicleItem>> SearchVehiclesAsync(
        CatalogSearchRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = request.Normalize();
        await using var connection = await OpenAsync(cancellationToken);
        const string where = """
            x.IsDeleted = 0
            AND (@Keyword IS NULL OR x.BienSoXe LIKE N'%' + @Keyword + N'%'
                 OR x.SourceBienSoXe LIKE N'%' + @Keyword + N'%'
                 OR x.SoKhung LIKE N'%' + @Keyword + N'%')
            AND (@SourceProfileCode IS NULL OR x.SourceProfileCode = @SourceProfileCode)
            AND (@TrangThai IS NULL OR UPPER(COALESCE(x.TrangThai, x.SourceLifecycle, N'')) = @TrangThai)
            """;
        var args = new
        {
            normalized.Keyword,
            normalized.SourceProfileCode,
            normalized.TrangThai,
            Offset = (normalized.Page - 1) * normalized.PageSize,
            normalized.PageSize,
        };
        var total = await connection.ExecuteScalarAsync<int>(Command(
            $"SELECT COUNT_BIG(1) FROM dbo.App_XeTap x WHERE {where};",
            args,
            cancellationToken));
        if (total == 0)
        {
            return PagedResult<SourceVehicleItem>.Empty(normalized.Page, normalized.PageSize);
        }

        var assignmentReadSchemaAvailable = await HasAssignmentReadSchemaAsync(
            connection,
            cancellationToken);
        var rows = await connection.QueryAsync<VehicleRow>(Command(assignmentReadSchemaAvailable
            ? $"""
            SELECT x.XeTapId,
                   COALESCE(x.SourceProfileCode,N'') AS SourceProfileCode,
                   COALESCE(x.SourceBienSoXe,x.BienSoXe) AS MaXe,
                   x.BienSoXe,
                   x.SoKhung,
                   x.SoDongCo AS SoMay,
                   COALESCE(x.NhanHieu,x.HangXe) AS HangXe,
                   x.LoaiXe,
                   x.HangGPLXXe AS HangDaoTao,
                   COALESCE(x.SourceLifecycle,x.TrangThai,N'INACTIVE') AS TrangThai,
                   CONVERT(bit, CASE WHEN x.SourceLifecycle=N'ACTIVE' AND COALESCE(x.SourceTrangThai,1)=1 THEN 1 ELSE 0 END) AS IsActive,
                   (SELECT COUNT_BIG(1) FROM dbo.App_KhoaHoc_XeTap kx
                     JOIN dbo.App_KhoaHoc kxCourse ON kxCourse.IsDeleted=0
                       AND kxCourse.MaKhoa=kx.MaKhoa
                       AND kxCourse.SourceProfileCode=x.SourceProfileCode
                     WHERE kx.IsDeleted=0 AND kx.BienSoXe=x.BienSoXe) AS CourseUsageCount,
                   (SELECT COUNT_BIG(1) FROM dbo.App_KhoaHoc_NhomDaoTao n
                     WHERE n.TrangThai=N'ACTIVE' AND (n.XeTapId=x.XeTapId OR n.XeBaiSo10Id=x.XeTapId)) AS GroupUsageCount,
                   (SELECT COUNT_BIG(1) FROM dbo.App_HocVien_PhanCong pc
                     WHERE pc.IsCurrent=1 AND (pc.XeTapId=x.XeTapId OR pc.XeBaiSo10Id=x.XeTapId)) AS StudentUsageCount,
                   CONVERT(bit, CASE WHEN x.SourceLifecycle=N'MANUAL_REVIEW'
                                          OR x.ManualReviewCode IS NOT NULL THEN 1 ELSE 0 END) AS IsManualReview
            FROM dbo.App_XeTap x
            WHERE {where}
            ORDER BY x.BienSoXe, x.SourceProfileCode, x.XeTapId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """
            : $"""
            SELECT x.XeTapId,
                   COALESCE(x.SourceProfileCode,N'') AS SourceProfileCode,
                   COALESCE(x.SourceBienSoXe,x.BienSoXe) AS MaXe,
                   x.BienSoXe,x.SoKhung,x.SoDongCo AS SoMay,
                   COALESCE(x.NhanHieu,x.HangXe) AS HangXe,x.LoaiXe,
                   x.HangGPLXXe AS HangDaoTao,
                   COALESCE(x.SourceLifecycle,x.TrangThai,N'INACTIVE') AS TrangThai,
                   CONVERT(bit,CASE WHEN x.SourceLifecycle=N'ACTIVE' AND COALESCE(x.SourceTrangThai,1)=1 THEN 1 ELSE 0 END) AS IsActive,
                   (SELECT COUNT_BIG(1) FROM dbo.App_KhoaHoc_XeTap kx
                     JOIN dbo.App_KhoaHoc k ON k.IsDeleted=0 AND k.MaKhoa=kx.MaKhoa AND k.SourceProfileCode=x.SourceProfileCode
                     WHERE kx.IsDeleted=0 AND kx.BienSoXe=x.BienSoXe) AS CourseUsageCount,
                   CONVERT(int,0) AS GroupUsageCount,
                   CONVERT(int,0) AS StudentUsageCount,
                   CONVERT(bit,CASE WHEN x.SourceLifecycle=N'MANUAL_REVIEW' OR x.ManualReviewCode IS NOT NULL THEN 1 ELSE 0 END) AS IsManualReview
            FROM dbo.App_XeTap x WHERE {where}
            ORDER BY x.BienSoXe,x.SourceProfileCode,x.XeTapId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, args, cancellationToken));
        return new PagedResult<SourceVehicleItem>
        {
            Items = rows.Select(ToVehicle).ToArray(),
            Page = normalized.Page,
            PageSize = normalized.PageSize,
            TotalItems = total,
        };
    }

    public async Task<PagedResult<CourseItem>> SearchCoursesAsync(
        CourseSearchRequest request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 25 : request.PageSize, 1, 200);
        var args = new
        {
            MaKhoa = AssignmentRules.NormalizeOptional(request.MaKhoa, 50),
            TenKhoa = AssignmentRules.NormalizeOptional(request.TenKhoa, 255),
            HangDaoTao = AssignmentRules.NormalizeOptional(request.HangDaoTao, 20),
            LoaiDaoTao = AssignmentRules.NormalizeOptional(request.LoaiDaoTao, 50),
            TrangThai = AssignmentRules.NormalizeOptional(request.TrangThai, 50)?.ToUpperInvariant(),
            SourceProfileCode = AssignmentRules.NormalizeProfile(request.SourceProfileCode, required: false),
            TuNgay = request.TuNgay?.ToDateTime(TimeOnly.MinValue),
            DenNgay = request.DenNgay?.ToDateTime(TimeOnly.MinValue),
            Offset = (page - 1) * pageSize,
            PageSize = pageSize,
        };
        const string where = """
            k.IsDeleted=0
            AND (@MaKhoa IS NULL OR k.MaKhoa LIKE N'%' + @MaKhoa + N'%')
            AND (@TenKhoa IS NULL OR k.TenKhoa LIKE N'%' + @TenKhoa + N'%')
            AND (@HangDaoTao IS NULL OR COALESCE(k.HangDaoTao,k.HangGPLX)=@HangDaoTao)
            AND (@LoaiDaoTao IS NULL OR CONVERT(nvarchar(50),k.HinhThucDaoTao)=@LoaiDaoTao)
            AND (@TrangThai IS NULL
                 OR (@TrangThai=N'ACTIVE' AND COALESCE(k.TrangThaiNguon,1)=1
                     AND UPPER(COALESCE(k.LastSyncStatus,N'')) NOT IN (N'MANUAL_REVIEW',N'CONFLICT'))
                 OR (@TrangThai=N'INACTIVE' AND COALESCE(k.TrangThaiNguon,1)=0)
                 OR (@TrangThai=N'MANUAL_REVIEW'
                     AND UPPER(COALESCE(k.LastSyncStatus,N'')) IN (N'MANUAL_REVIEW',N'CONFLICT'))
                 OR (@TrangThai NOT IN (N'ACTIVE',N'INACTIVE',N'MANUAL_REVIEW')
                     AND UPPER(COALESCE(k.TrangThai,N''))=@TrangThai))
            AND (@SourceProfileCode IS NULL OR k.SourceProfileCode=@SourceProfileCode)
            AND (@TuNgay IS NULL OR k.NgayKhaiGiang>=@TuNgay)
            AND (@DenNgay IS NULL OR k.NgayKhaiGiang<=@DenNgay)
            """;
        await using var connection = await OpenAsync(cancellationToken);
        var total = await connection.ExecuteScalarAsync<int>(Command(
            $"SELECT COUNT_BIG(1) FROM dbo.App_KhoaHoc k WHERE {where};",
            args,
            cancellationToken));
        if (total == 0)
        {
            return PagedResult<CourseItem>.Empty(page, pageSize);
        }

        var assignmentReadSchemaAvailable = await HasAssignmentReadSchemaAsync(
            connection,
            cancellationToken);
        var rows = await connection.QueryAsync<CourseRow>(Command(assignmentReadSchemaAvailable
            ? $"""
            SELECT k.KhoaHocId, COALESCE(k.SourceProfileCode,N'') AS SourceProfileCode,
                   k.MaKhoa, k.TenKhoa, COALESCE(k.HangDaoTao,k.HangGPLX) AS HangDaoTao,
                   CONVERT(nvarchar(50),k.HinhThucDaoTao) AS LoaiDaoTao,
                   k.NgayKhaiGiang, k.NgayBeGiang,
                   k.SoQuyetDinhKhaiGiang AS SoQuyetDinh,
                   COALESCE(k.TrangThai,N'') AS TrangThai,
                   CONVERT(bit,CASE WHEN COALESCE(k.TrangThaiNguon,1)=1 THEN 1 ELSE 0 END) AS IsActive,
                   CONVERT(int,COUNT_BIG(DISTINCT h.HocVienId)) AS LearnerCount,
                   CONVERT(int,COUNT_BIG(DISTINCT CASE WHEN pc.PhanCongId IS NULL THEN h.HocVienId END)) AS UnassignedCount,
                   CONVERT(int,COUNT_BIG(DISTINCT CASE WHEN
                       (pc.GiaoVienHoSoId IS NOT NULL AND (gh.TrangThai<>N'ACTIVE' OR gh.IsDeleted=1)) OR
                       (pc.GiaoVienDungLopId IS NOT NULL AND (gv.GiaoVienId IS NULL OR COALESCE(gv.TrangThaiNguon,1)=0)) OR
                       (pc.XeTapId IS NOT NULL AND (xt.XeTapId IS NULL OR xt.SourceLifecycle<>N'ACTIVE'
                           OR COALESCE(xt.SourceTrangThai,1)<>1)) OR
                       (pc.XeBaiSo10Id IS NOT NULL AND (x10.XeTapId IS NULL OR x10.SourceLifecycle<>N'ACTIVE'
                           OR COALESCE(x10.SourceTrangThai,1)<>1))
                       THEN h.HocVienId END)) AS ManualReviewCount,
                   k.RowVersion
            FROM dbo.App_KhoaHoc k
            LEFT JOIN dbo.App_HocVien h ON h.IsDeleted=0 AND h.MaKhoa=k.MaKhoa
                 AND h.SourceProfileCode=k.SourceProfileCode
            LEFT JOIN dbo.App_HocVien_PhanCong pc ON pc.HocVienId=h.HocVienId AND pc.IsCurrent=1
            LEFT JOIN dbo.App_GiaoVien_hs gh ON gh.GiaoVienHsId=pc.GiaoVienHoSoId
            LEFT JOIN dbo.App_GiaoVien gv ON gv.GiaoVienId=pc.GiaoVienDungLopId
            LEFT JOIN dbo.App_XeTap xt ON xt.XeTapId=pc.XeTapId
            LEFT JOIN dbo.App_XeTap x10 ON x10.XeTapId=pc.XeBaiSo10Id
            WHERE {where}
            GROUP BY k.KhoaHocId,k.SourceProfileCode,k.MaKhoa,k.TenKhoa,k.HangDaoTao,k.HangGPLX,
                     k.HinhThucDaoTao,k.NgayKhaiGiang,k.NgayBeGiang,k.SoQuyetDinhKhaiGiang,
                     k.TrangThai,k.TrangThaiNguon,k.RowVersion
            ORDER BY k.NgayKhaiGiang DESC,k.MaKhoa,k.KhoaHocId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """
            : $"""
            SELECT k.KhoaHocId, COALESCE(k.SourceProfileCode,N'') AS SourceProfileCode,
                   k.MaKhoa, k.TenKhoa, COALESCE(k.HangDaoTao,k.HangGPLX) AS HangDaoTao,
                   CONVERT(nvarchar(50),k.HinhThucDaoTao) AS LoaiDaoTao,
                   k.NgayKhaiGiang, k.NgayBeGiang,
                   k.SoQuyetDinhKhaiGiang AS SoQuyetDinh,
                   COALESCE(k.TrangThai,N'') AS TrangThai,
                   CONVERT(bit,CASE WHEN COALESCE(k.TrangThaiNguon,1)=1 THEN 1 ELSE 0 END) AS IsActive,
                   CONVERT(int,COUNT_BIG(DISTINCT h.HocVienId)) AS LearnerCount,
                   CONVERT(int,COUNT_BIG(DISTINCT h.HocVienId)) AS UnassignedCount,
                   CONVERT(int,0) AS ManualReviewCount,
                   k.RowVersion
            FROM dbo.App_KhoaHoc k
            LEFT JOIN dbo.App_HocVien h ON h.IsDeleted=0 AND h.MaKhoa=k.MaKhoa
                 AND h.SourceProfileCode=k.SourceProfileCode
            WHERE {where}
            GROUP BY k.KhoaHocId,k.SourceProfileCode,k.MaKhoa,k.TenKhoa,k.HangDaoTao,k.HangGPLX,
                     k.HinhThucDaoTao,k.NgayKhaiGiang,k.NgayBeGiang,k.SoQuyetDinhKhaiGiang,
                     k.TrangThai,k.TrangThaiNguon,k.RowVersion
            ORDER BY k.NgayKhaiGiang DESC,k.MaKhoa,k.KhoaHocId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, args, cancellationToken));
        return new PagedResult<CourseItem>
        {
            Items = rows.Select(ToCourse).ToArray(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
        };
    }

    public async Task<PagedResult<DossierReceiverItem>> SearchDossierReceiversAsync(
        CatalogSearchRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = request.Normalize();
        var args = new
        {
            normalized.Keyword,
            normalized.TrangThai,
            Offset = (normalized.Page - 1) * normalized.PageSize,
            normalized.PageSize,
        };
        const string where = """
            (@Keyword IS NULL OR gh.MaGiaoVienHs LIKE N'%' + @Keyword + N'%'
                 OR gh.HoTen LIKE N'%' + @Keyword + N'%'
                 OR gh.HoTenSearch LIKE N'%' + @Keyword + N'%')
            AND (@TrangThai IS NULL OR UPPER(gh.TrangThai)=@TrangThai)
            """;
        await using var connection = await OpenAsync(cancellationToken);
        if (!await HasDossierProjectionSchemaAsync(connection, cancellationToken))
        {
            return new PagedResult<DossierReceiverItem>
            {
                Items = [],
                Page = normalized.Page,
                PageSize = normalized.PageSize,
                TotalItems = 0,
            };
        }
        var total = await connection.ExecuteScalarAsync<int>(Command(
            $"SELECT COUNT_BIG(1) FROM dbo.App_GiaoVien_hs gh WHERE {where};",
            args,
            cancellationToken));
        var rows = total == 0
            ? []
            : (await connection.QueryAsync<DossierReceiverRow>(Command($"""
                SELECT gh.GiaoVienHsId,gh.MaGiaoVienHs,gh.HoTen,gh.NgaySinh,gh.SoCCCD,
                       gh.TrangThai,gh.IsDeleted,
                       CONVERT(int,(SELECT COUNT_BIG(1) FROM dbo.App_HocVien_PhanCong pc
                                    WHERE pc.GiaoVienHoSoId=gh.GiaoVienHsId)) AS ReferenceCount,
                       gh.RowVersion,COALESCE(gh.UpdatedAt,gh.CreatedAt) AS UpdatedAtUtc,
                       COALESCE(gh.UpdatedBy,gh.CreatedBy) AS UpdatedBy,gh.GhiChu
                FROM dbo.App_GiaoVien_hs gh WHERE {where}
                ORDER BY gh.IsDeleted,gh.HoTen,gh.MaGiaoVienHs
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """, args, cancellationToken))).ToArray();
        return new PagedResult<DossierReceiverItem>
        {
            Items = rows.Select(ToDossierReceiver).ToArray(),
            Page = normalized.Page,
            PageSize = normalized.PageSize,
            TotalItems = total,
        };
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new InvalidOperationException("QLHV_APP chưa có cấu hình kết nối dùng được.");
        }

        var connection = new SqlConnection(target.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task<bool> HasAssignmentReadSchemaAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        return await connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CONVERT(bit,CASE WHEN
                OBJECT_ID(N'dbo.App_HocVien_PhanCong',N'U') IS NOT NULL AND
                OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao',N'U') IS NOT NULL AND
                OBJECT_ID(N'dbo.App_GiaoVien_hs',N'U') IS NOT NULL
                THEN 1 ELSE 0 END);
            """, null, cancellationToken));
    }

    private async Task<bool> HasDossierProjectionSchemaAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
        => await connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CONVERT(bit,CASE WHEN
                OBJECT_ID(N'dbo.App_GiaoVien_hs',N'U') IS NOT NULL AND
                COL_LENGTH(N'dbo.App_GiaoVien_hs',N'SourceProfileCode') IS NOT NULL AND
                COL_LENGTH(N'dbo.App_GiaoVien_hs',N'SourceRelationshipKey') IS NOT NULL
                THEN 1 ELSE 0 END);
            """, null, cancellationToken));

    private CommandDefinition Command(
        string sql,
        object? parameters,
        CancellationToken cancellationToken,
        IDbTransaction? transaction = null) =>
        new(sql, parameters, transaction, _options.TimeoutSeconds, cancellationToken: cancellationToken);

    private async Task<DateTime> ReadDatabaseUtcNowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<DateTime>(Command(
            "SELECT CONVERT(datetime2(7),SYSUTCDATETIME());",
            null,
            cancellationToken,
            transaction));

    private static SourceTeacherItem ToTeacher(TeacherRow row) => new(
        row.GiaoVienId,row.SourceProfileCode,row.MaGV,row.HoTen,
        AssignmentRules.ToDateOnly(row.NgaySinh),row.SoCCCD,row.HangDaoTao,row.TrangThai,
        row.IsActive,row.CourseUsageCount,row.StudentUsageCount,row.IsManualReview);

    private static SourceVehicleItem ToVehicle(VehicleRow row) => new(
        row.XeTapId,row.SourceProfileCode,row.MaXe,row.BienSoXe,row.SoKhung,row.SoMay,
        row.HangXe,row.LoaiXe,row.HangDaoTao,row.TrangThai,row.IsActive,row.CourseUsageCount,
        row.GroupUsageCount,row.StudentUsageCount,row.IsManualReview);

    private static CourseItem ToCourse(CourseRow row) => new(
        row.KhoaHocId,row.SourceProfileCode,row.MaKhoa,row.TenKhoa,row.HangDaoTao,row.LoaiDaoTao,
        AssignmentRules.ToDateOnly(row.NgayKhaiGiang),AssignmentRules.ToDateOnly(row.NgayBeGiang),
        row.SoQuyetDinh,row.TrangThai,row.IsActive,row.LearnerCount,row.UnassignedCount,
        row.ManualReviewCount,AssignmentRules.RowVersionToString(row.RowVersion));

    private static DossierReceiverItem ToDossierReceiver(DossierReceiverRow row) => new(
        row.GiaoVienHsId,row.MaGiaoVienHs,row.HoTen,AssignmentRules.ToDateOnly(row.NgaySinh),
        row.SoCCCD,row.TrangThai,row.IsDeleted,row.ReferenceCount,
        AssignmentRules.RowVersionToString(row.RowVersion),row.UpdatedAtUtc,row.UpdatedBy,row.GhiChu);

    private sealed class TeacherRow
    {
        public long GiaoVienId { get; init; }
        public string SourceProfileCode { get; init; } = string.Empty;
        public string MaGV { get; init; } = string.Empty;
        public string HoTen { get; init; } = string.Empty;
        public DateTime? NgaySinh { get; init; }
        public string? SoCCCD { get; init; }
        public string? HangDaoTao { get; init; }
        public string TrangThai { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public int CourseUsageCount { get; init; }
        public int StudentUsageCount { get; init; }
        public bool IsManualReview { get; init; }
    }

    private sealed class VehicleRow
    {
        public long XeTapId { get; init; }
        public string SourceProfileCode { get; init; } = string.Empty;
        public string MaXe { get; init; } = string.Empty;
        public string BienSoXe { get; init; } = string.Empty;
        public string? SoKhung { get; init; }
        public string? SoMay { get; init; }
        public string? HangXe { get; init; }
        public string? LoaiXe { get; init; }
        public string? HangDaoTao { get; init; }
        public string TrangThai { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public int CourseUsageCount { get; init; }
        public int GroupUsageCount { get; init; }
        public int StudentUsageCount { get; init; }
        public bool IsManualReview { get; init; }
    }

    private sealed class CourseRow
    {
        public long KhoaHocId { get; init; }
        public string SourceProfileCode { get; init; } = string.Empty;
        public string MaKhoa { get; init; } = string.Empty;
        public string? TenKhoa { get; init; }
        public string? HangDaoTao { get; init; }
        public string? LoaiDaoTao { get; init; }
        public DateTime? NgayKhaiGiang { get; init; }
        public DateTime? NgayBeGiang { get; init; }
        public string? SoQuyetDinh { get; init; }
        public string TrangThai { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public int LearnerCount { get; init; }
        public int UnassignedCount { get; init; }
        public int ManualReviewCount { get; init; }
        public byte[] RowVersion { get; init; } = [];
    }

    private sealed class DossierReceiverRow
    {
        public long GiaoVienHsId { get; init; }
        public string MaGiaoVienHs { get; init; } = string.Empty;
        public string HoTen { get; init; } = string.Empty;
        public DateTime? NgaySinh { get; init; }
        public string? SoCCCD { get; init; }
        public string TrangThai { get; init; } = string.Empty;
        public bool IsDeleted { get; init; }
        public int ReferenceCount { get; init; }
        public byte[] RowVersion { get; init; } = [];
        public DateTime? UpdatedAtUtc { get; init; }
        public string? UpdatedBy { get; init; }
        public string? GhiChu { get; init; }
    }
}

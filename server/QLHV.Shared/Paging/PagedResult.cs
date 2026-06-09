namespace QLHV.Shared.Paging;

/// <summary>
/// Kết quả phân trang dùng chung cho các API danh sách.
/// </summary>
/// <typeparam name="T">Kiểu phần tử trong danh sách.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>Danh sách phần tử của trang hiện tại.</summary>
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>Trang hiện tại (bắt đầu từ 1).</summary>
    public int Page { get; init; }

    /// <summary>Số phần tử mỗi trang.</summary>
    public int PageSize { get; init; }

    /// <summary>Tổng số phần tử thỏa điều kiện.</summary>
    public int TotalItems { get; init; }

    /// <summary>Tổng số trang.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalItems / PageSize);

    /// <summary>Tạo một kết quả rỗng an toàn cho trang/kích thước trang cho trước.</summary>
    public static PagedResult<T> Empty(int page, int pageSize) => new()
    {
        Items = Array.Empty<T>(),
        Page = page < 1 ? 1 : page,
        PageSize = pageSize < 1 ? 0 : pageSize,
        TotalItems = 0,
    };
}

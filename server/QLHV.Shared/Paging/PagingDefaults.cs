namespace QLHV.Shared.Paging;

/// <summary>Giá trị mặc định và giới hạn cho phân trang.</summary>
public static class PagingDefaults
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;

    /// <summary>Chuẩn hóa số trang về giá trị hợp lệ tối thiểu là 1.</summary>
    public static int NormalizePage(int page) => page < 1 ? DefaultPage : page;

    /// <summary>Chuẩn hóa kích thước trang về khoảng hợp lệ.</summary>
    public static int NormalizePageSize(int pageSize)
    {
        if (pageSize < 1)
        {
            return DefaultPageSize;
        }

        return pageSize > MaxPageSize ? MaxPageSize : pageSize;
    }
}

using QLHV.Application.HocVien.Dtos;
using QLHV.Shared.Paging;

namespace QLHV.Application.HocVien;

/// <summary>
/// Cài đặt nghiệp vụ tìm kiếm học viên. Chuẩn hóa tham số rồi ủy quyền cho repository.
/// </summary>
public sealed class HocVienService : IHocVienService
{
    private readonly IHocVienRepository _repository;

    public HocVienService(IHocVienRepository repository)
    {
        _repository = repository;
    }

    public Task<PagedResult<HocVienListItemDto>> SearchAsync(
        HocVienSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = request.Normalized();
        return _repository.SearchAsync(normalized, cancellationToken);
    }
}

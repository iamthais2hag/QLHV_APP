namespace QLHV.Application.HocVien.Dtos;

public sealed class HocVienPhotoAuditResultDto
{
    public int TotalItems { get; init; }

    public int TotalHasPhoto { get; init; }

    public int TotalMissingPhoto { get; init; }

    public int TotalInvalidPhoto { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalItems / PageSize);

    public IReadOnlyList<HocVienPhotoAuditItemDto> Items { get; init; } = [];
}

namespace QLHV.Application.Sync.Dtos;

/// <summary>Counts returned by the target upsert repository. Contains no raw CCCD/GPLX data.</summary>
public sealed class HocVienUpsertResultDto
{
    public int TotalRead { get; init; }
    public int Inserted { get; init; }
    public int Updated { get; init; }
    public int Skipped { get; init; }
}

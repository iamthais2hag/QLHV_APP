namespace QLHV.Application.Sync.Dtos;

/// <summary>Số liệu kết quả upsert một lô vào App_HocVien.</summary>
public sealed record UpsertCounts(int Inserted, int Updated, int Skipped)
{
    public static UpsertCounts Empty => new(0, 0, 0);
}

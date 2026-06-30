using System.Data;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

public static class MotoSyncUpdatePlanner
{
    public static MotoSyncUpdateDetectionResult Build(
        string tableName,
        DataTable sourceRows,
        DataTable targetRows,
        IReadOnlyList<string> compareColumns,
        int sampleLimit = 20)
    {
        var targetByMaDk = targetRows.Rows
            .Cast<DataRow>()
            .Select(row => new
            {
                MaDK = Convert.ToString(row["MaDK"])?.Trim() ?? string.Empty,
                Row = row,
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.MaDK))
            .GroupBy(item => item.MaDK, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Row, StringComparer.OrdinalIgnoreCase);

        var updatedMaDks = new List<string>();
        var samples = new List<MotoSyncUpdateSampleDto>();

        foreach (DataRow sourceRow in sourceRows.Rows)
        {
            var maDk = Convert.ToString(sourceRow["MaDK"])?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(maDk) || !targetByMaDk.TryGetValue(maDk, out var targetRow))
            {
                continue;
            }

            var changedColumns = compareColumns
                .Where(column => !ValuesEqual(sourceRow[column], targetRow[column]))
                .ToArray();
            if (changedColumns.Length == 0)
            {
                continue;
            }

            updatedMaDks.Add(maDk);
            if (samples.Count < sampleLimit)
            {
                samples.Add(new MotoSyncUpdateSampleDto
                {
                    MaDK = maDk,
                    TableName = tableName,
                    ChangedColumnNames = changedColumns,
                });
            }
        }

        return new MotoSyncUpdateDetectionResult(updatedMaDks, samples);
    }

    public static bool ValuesEqual(object? sourceValue, object? targetValue)
    {
        var source = NormalizeValue(sourceValue);
        var target = NormalizeValue(targetValue);
        if (source is null || target is null)
        {
            return source is null && target is null;
        }

        if (source is byte[] sourceBytes && target is byte[] targetBytes)
        {
            return sourceBytes.SequenceEqual(targetBytes);
        }

        return source.Equals(target);
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is null || value == DBNull.Value)
        {
            return null;
        }

        return value;
    }
}

public sealed class MotoSyncUpdateDetectionResult
{
    public MotoSyncUpdateDetectionResult(
        IReadOnlyList<string> updatedMaDks,
        IReadOnlyList<MotoSyncUpdateSampleDto> samples)
    {
        UpdatedMaDks = updatedMaDks;
        Samples = samples;
    }

    public IReadOnlyList<string> UpdatedMaDks { get; }

    public IReadOnlyList<MotoSyncUpdateSampleDto> Samples { get; }

    public long UpdatedRowCount => UpdatedMaDks.Count;
}

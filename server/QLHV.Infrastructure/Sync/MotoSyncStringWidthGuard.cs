namespace QLHV.Infrastructure.Sync;

public static class MotoSyncStringWidthGuard
{
    public static MotoSyncStringWidthGuardResult Evaluate(
        string tableName,
        string columnName,
        string sourceDataType,
        short sourceMaxLengthBytes,
        string targetDataType,
        short targetMaxLengthBytes,
        long actualMaxLength)
    {
        var sourceLimit = ToCharacterLimit(sourceDataType, sourceMaxLengthBytes);
        var targetLimit = ToCharacterLimit(targetDataType, targetMaxLengthBytes);
        if (targetLimit is null)
        {
            return MotoSyncStringWidthGuardResult.None;
        }

        var sourceIsWider = sourceLimit is null || sourceLimit > targetLimit;
        if (!sourceIsWider)
        {
            return MotoSyncStringWidthGuardResult.None;
        }

        var sourceLimitText = sourceLimit?.ToString() ?? "max";
        if (actualMaxLength > targetLimit.Value)
        {
            return MotoSyncStringWidthGuardResult.Blocker(
                $"dbo.{tableName}.{columnName} co du lieu planned insert dai nhat {actualMaxLength} ky tu, vuot gioi han target {targetLimit.Value} ky tu (schema source {sourceLimitText}, target {targetLimit.Value}).");
        }

        return MotoSyncStringWidthGuardResult.Warning(
            $"dbo.{tableName}.{columnName} schema source rong hon target ({sourceLimitText}>{targetLimit.Value} ky tu) nhung du lieu planned insert dai nhat {actualMaxLength} ky tu van fit target.");
    }

    public static int? ToCharacterLimit(string dataType, short maxLengthBytes)
    {
        if (maxLengthBytes < 0)
        {
            return null;
        }

        return dataType.ToLowerInvariant() switch
        {
            "nvarchar" or "nchar" => maxLengthBytes / 2,
            "varchar" or "char" => maxLengthBytes,
            _ => maxLengthBytes,
        };
    }
}

public sealed class MotoSyncStringWidthGuardResult
{
    public static readonly MotoSyncStringWidthGuardResult None = new(false, false, string.Empty);

    private MotoSyncStringWidthGuardResult(bool isBlocker, bool isWarning, string message)
    {
        IsBlocker = isBlocker;
        IsWarning = isWarning;
        Message = message;
    }

    public bool IsBlocker { get; }

    public bool IsWarning { get; }

    public string Message { get; }

    public static MotoSyncStringWidthGuardResult Blocker(string message) => new(true, false, message);

    public static MotoSyncStringWidthGuardResult Warning(string message) => new(false, true, message);
}

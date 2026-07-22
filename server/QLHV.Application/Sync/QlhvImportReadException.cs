namespace QLHV.Application.Sync;

public sealed class QlhvImportReadException : Exception
{
    public QlhvImportReadException(string safeMessage)
        : base(safeMessage)
    {
    }
}

namespace QLHV.Application.Sync;

public sealed class QlhvOperationsStoreUnavailableException : Exception
{
    public QlhvOperationsStoreUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

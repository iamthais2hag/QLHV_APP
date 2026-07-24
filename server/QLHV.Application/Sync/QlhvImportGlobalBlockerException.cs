namespace QLHV.Application.Sync;

/// <summary>
/// Signals target readiness failures that make every write domain unsafe.
/// Optional-domain schema gaps must not use this exception.
/// </summary>
public sealed class QlhvImportGlobalBlockerException : Exception
{
    public QlhvImportGlobalBlockerException(string message)
        : base(message)
    {
    }
}

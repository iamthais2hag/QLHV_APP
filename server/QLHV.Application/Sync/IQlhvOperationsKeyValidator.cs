namespace QLHV.Application.Sync;

public interface IQlhvOperationsKeyValidator
{
    bool IsConfigured { get; }

    bool IsValid(string? providedKey);
}

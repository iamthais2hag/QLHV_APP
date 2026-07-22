using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace QLHV.Application.Sync;

public sealed class QlhvOperationsKeyValidator : IQlhvOperationsKeyValidator
{
    private readonly byte[]? _configuredKeyHash;

    public QlhvOperationsKeyValidator(IOptions<QlhvOperationsOptions> options)
    {
        var configured = options.Value.AdminKey;
        _configuredKeyHash = string.IsNullOrWhiteSpace(configured)
            ? null
            : SHA256.HashData(Encoding.UTF8.GetBytes(configured));
    }

    public bool IsConfigured => _configuredKeyHash is not null;

    public bool IsValid(string? providedKey)
    {
        if (_configuredKeyHash is null || string.IsNullOrEmpty(providedKey))
        {
            return false;
        }

        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
        return CryptographicOperations.FixedTimeEquals(_configuredKeyHash, providedHash);
    }
}

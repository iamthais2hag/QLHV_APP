using QLHV.Application.CsdtConnections;

namespace QLHV.Infrastructure.CsdtConnections;

public sealed class UnavailableConnectionPasswordProtector : IConnectionPasswordProtector
{
    public bool IsAvailable => false;

    public byte[] Protect(string plainText)
        => throw new InvalidOperationException(
            "Connection password encryption is not configured. Refusing to store plaintext.");

    public string Unprotect(byte[] cipherText)
        => throw new InvalidOperationException(
            "Connection password encryption is not configured. Refusing to decrypt.");
}

namespace QLHV.Application.CsdtConnections;

public interface IConnectionPasswordProtector
{
    bool IsAvailable { get; }

    byte[] Protect(string plainText);

    string Unprotect(byte[] cipherText);
}

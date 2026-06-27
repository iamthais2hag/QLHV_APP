namespace QLHV.Application.CsdtConnections;

public sealed class CsdtConnectionProfileException : Exception
{
    public CsdtConnectionProfileException(string code, string message, int statusCode = 400)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }

    public int StatusCode { get; }
}

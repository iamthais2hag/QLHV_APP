namespace QLHV.Application.CsdtConnections.Dtos;

public sealed class CsdtConnectionProfileRecord
{
    public Guid Id { get; init; }

    public string ProfileCode { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string ProfileGroup { get; init; } = string.Empty;

    public string? ServerName { get; init; }

    public string? DatabaseName { get; init; }

    public string AuthMode { get; init; } = "Windows";

    public string? UserName { get; init; }

    public byte[]? PasswordCipherText { get; init; }

    public DateTime? PasswordUpdatedAt { get; init; }

    public bool IsPasswordConfigured { get; init; }

    public bool IsActive { get; init; }

    public DateTime? LastTestedAt { get; init; }

    public string? LastTestStatus { get; init; }

    public string? LastTestMessage { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }
}

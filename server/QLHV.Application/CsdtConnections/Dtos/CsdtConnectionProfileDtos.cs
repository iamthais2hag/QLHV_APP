namespace QLHV.Application.CsdtConnections.Dtos;

public sealed class CsdtConnectionProfileListItemDto
{
    public string ProfileCode { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string ProfileGroup { get; init; } = string.Empty;

    public string AuthMode { get; init; } = string.Empty;

    public bool IsConfigured { get; init; }

    public bool IsPasswordConfigured { get; init; }

    public bool IsActive { get; init; }

    public DateTime? LastTestedAt { get; init; }

    public string? LastTestStatus { get; init; }

    public string? LastTestMessage { get; init; }
}

public sealed class CsdtConnectionProfileDetailDto
{
    public string ProfileCode { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string ProfileGroup { get; init; } = string.Empty;

    public string? ServerName { get; init; }

    public string? DatabaseName { get; init; }

    public string AuthMode { get; init; } = string.Empty;

    public string? UserName { get; init; }

    public bool IsConfigured { get; init; }

    public bool IsPasswordConfigured { get; init; }

    public bool IsActive { get; init; }

    public DateTime? LastTestedAt { get; init; }

    public string? LastTestStatus { get; init; }

    public string? LastTestMessage { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }
}

public sealed class SaveCsdtConnectionProfileRequest
{
    public string? DisplayName { get; init; }

    public string? ServerName { get; init; }

    public string? DatabaseName { get; init; }

    public string AuthMode { get; init; } = "Windows";

    public string? UserName { get; init; }

    public string? PasswordPlainText { get; init; }

    public bool IsActive { get; init; }
}

public sealed class TestCsdtConnectionProfileRequest
{
    public string? ServerName { get; init; }

    public string? DatabaseName { get; init; }

    public string? AuthMode { get; init; }

    public string? UserName { get; init; }

    public string? PasswordPlainText { get; init; }
}

public sealed class TestCsdtConnectionProfileResultDto
{
    public string ProfileCode { get; init; } = string.Empty;

    public bool IsReadOnly => true;

    public bool CanTest { get; init; }

    public bool Succeeded { get; init; }

    public string Status { get; init; } = "Unknown";

    public string Message { get; init; } = string.Empty;

    public DateTime TestedAt { get; init; }
}

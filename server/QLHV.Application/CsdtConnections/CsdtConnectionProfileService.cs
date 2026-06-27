using QLHV.Application.CsdtConnections.Dtos;
using QLHV.Application.Sync.Connections;

namespace QLHV.Application.CsdtConnections;

public sealed class CsdtConnectionProfileService : ICsdtConnectionProfileService
{
    private const string AuthModeWindows = "Windows";
    private const string AuthModeSqlLogin = "SqlLogin";
    private readonly ICsdtConnectionProfileRepository _repository;
    private readonly IConnectionPasswordProtector _passwordProtector;
    private readonly ICsdtConnectionTester _connectionTester;
    private readonly IConnectionSettingsProvider _connectionSettings;

    public CsdtConnectionProfileService(
        ICsdtConnectionProfileRepository repository,
        IConnectionPasswordProtector passwordProtector,
        ICsdtConnectionTester connectionTester,
        IConnectionSettingsProvider connectionSettings)
    {
        _repository = repository;
        _passwordProtector = passwordProtector;
        _connectionTester = connectionTester;
        _connectionSettings = connectionSettings;
    }

    public async Task<IReadOnlyList<CsdtConnectionProfileListItemDto>> GetProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        var records = await _repository.GetAllAsync(cancellationToken);
        var rows = records
            .Where(r => CsdtConnectionProfileCodes.IsFixedProfile(r.ProfileCode))
            .OrderBy(r => SortOrder(r.ProfileCode))
            .Select(ToListItem)
            .ToList();

        return rows;
    }

    public async Task<CsdtConnectionProfileDetailDto?> GetProfileAsync(
        string profileCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = EnsureFixedProfile(profileCode);
        var record = await _repository.GetByCodeAsync(normalized, cancellationToken);
        return record is null ? null : ToDetail(record);
    }

    public async Task<CsdtConnectionProfileDetailDto> SaveProfileAsync(
        string profileCode,
        SaveCsdtConnectionProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalized = EnsureFixedProfile(profileCode);

        if (string.Equals(normalized, CsdtConnectionProfileCodes.QlhvApp, StringComparison.OrdinalIgnoreCase))
        {
            throw new CsdtConnectionProfileException(
                "BOOTSTRAP_PROFILE_READONLY",
                "QLHV_APP la ket noi bootstrap. Giai doan nay chi ho tro xem/test trang thai, chua sua cau hinh trong menu.");
        }

        var authMode = NormalizeAuthMode(request.AuthMode);
        if (authMode == AuthModeSqlLogin && string.IsNullOrWhiteSpace(request.UserName))
        {
            throw new CsdtConnectionProfileException(
                "SQL_LOGIN_USERNAME_REQUIRED",
                "SQL Login yeu cau UserName.");
        }

        byte[]? cipherText = null;
        var updatePassword = !string.IsNullOrEmpty(request.PasswordPlainText);
        if (updatePassword)
        {
            if (!_passwordProtector.IsAvailable)
            {
                throw new CsdtConnectionProfileException(
                    "PASSWORD_PROTECTOR_NOT_CONFIGURED",
                    "Chua cau hinh co che ma hoa mat khau. Backend tu choi luu password de tranh plaintext.");
            }

            cipherText = _passwordProtector.Protect(request.PasswordPlainText!);
        }

        var sanitizedRequest = new SaveCsdtConnectionProfileRequest
        {
            DisplayName = request.DisplayName,
            ServerName = request.ServerName,
            DatabaseName = request.DatabaseName,
            AuthMode = authMode,
            UserName = request.UserName,
            PasswordPlainText = null,
            IsActive = request.IsActive,
        };

        var saved = await _repository.SaveAsync(
            normalized,
            sanitizedRequest,
            cipherText,
            updatePassword,
            cancellationToken);
        if (saved is null)
        {
            throw new CsdtConnectionProfileException(
                "PROFILE_NOT_FOUND",
                "Profile khong ton tai trong bang cau hinh.",
                404);
        }

        await _repository.AddAuditAsync(
            normalized,
            updatePassword ? "PasswordChange" : "Update",
            "Success",
            "Updated CSDT connection profile metadata without returning password.",
            cancellationToken);

        return ToDetail(saved);
    }

    public async Task<TestCsdtConnectionProfileResultDto> TestProfileAsync(
        string profileCode,
        TestCsdtConnectionProfileRequest? request,
        CancellationToken cancellationToken = default)
    {
        var normalized = EnsureFixedProfile(profileCode);
        var testedAt = DateTime.UtcNow;
        ConnectionProfileTestOutcome outcome;

        if (string.Equals(normalized, CsdtConnectionProfileCodes.QlhvApp, StringComparison.OrdinalIgnoreCase))
        {
            var bootstrap = await _connectionSettings.GetQlhvAppConnectionAsync(cancellationToken);
            outcome = await _connectionTester.TestConnectionStringAsync(
                normalized,
                bootstrap.ConnectionString,
                cancellationToken);
        }
        else
        {
            var record = await _repository.GetByCodeAsync(normalized, cancellationToken);
            if (record is null)
            {
                throw new CsdtConnectionProfileException(
                    "PROFILE_NOT_FOUND",
                    "Profile khong ton tai trong bang cau hinh.",
                    404);
            }

            var settings = BuildTestSettings(record, request);
            outcome = await TestStoredOrRequestedSettingsAsync(record, settings, request, cancellationToken);
        }

        var status = outcome.Succeeded ? "Success" : "Failed";
        var safeMessage = SanitizeMessage(outcome.SafeMessage);
        await _repository.UpdateTestResultAsync(normalized, testedAt, status, safeMessage, cancellationToken);
        await _repository.AddAuditAsync(normalized, "Test", status, safeMessage, cancellationToken);

        return new TestCsdtConnectionProfileResultDto
        {
            ProfileCode = normalized,
            CanTest = outcome.CanTest,
            Succeeded = outcome.Succeeded,
            Status = outcome.Status,
            Message = safeMessage,
            TestedAt = testedAt,
        };
    }

    private async Task<ConnectionProfileTestOutcome> TestStoredOrRequestedSettingsAsync(
        CsdtConnectionProfileRecord record,
        ConnectionProfileTestSettings settings,
        TestCsdtConnectionProfileRequest? request,
        CancellationToken cancellationToken)
    {
        var authMode = NormalizeAuthMode(settings.AuthMode);
        var password = request?.PasswordPlainText;
        if (authMode == AuthModeSqlLogin && string.IsNullOrEmpty(password))
        {
            if (record.PasswordCipherText is null || !record.IsPasswordConfigured)
            {
                return BlockedTest("Profile SQL Login chua co password da ma hoa.");
            }

            if (!_passwordProtector.IsAvailable)
            {
                return BlockedTest("Chua cau hinh co che giai ma password de test ket noi.");
            }

            password = _passwordProtector.Unprotect(record.PasswordCipherText);
        }

        if (string.IsNullOrWhiteSpace(settings.ServerName) || string.IsNullOrWhiteSpace(settings.DatabaseName))
        {
            return new ConnectionProfileTestOutcome
            {
                CanTest = false,
                Succeeded = false,
                Status = "NotConfigured",
                SafeMessage = "Profile chua co ServerName hoac DatabaseName.",
            };
        }

        if (authMode == AuthModeSqlLogin &&
            (string.IsNullOrWhiteSpace(settings.UserName) || string.IsNullOrEmpty(password)))
        {
            return BlockedTest("Profile SQL Login chua du UserName/password.");
        }

        return await _connectionTester.TestSettingsAsync(
            new ConnectionProfileTestSettings
            {
                ProfileCode = settings.ProfileCode,
                ServerName = settings.ServerName,
                DatabaseName = settings.DatabaseName,
                AuthMode = authMode,
                UserName = settings.UserName,
                PasswordPlainText = password,
            },
            cancellationToken);
    }

    private static ConnectionProfileTestSettings BuildTestSettings(
        CsdtConnectionProfileRecord record,
        TestCsdtConnectionProfileRequest? request) => new()
    {
        ProfileCode = record.ProfileCode,
        ServerName = FirstNonEmpty(request?.ServerName, record.ServerName),
        DatabaseName = FirstNonEmpty(request?.DatabaseName, record.DatabaseName),
        AuthMode = FirstNonEmpty(request?.AuthMode, record.AuthMode) ?? AuthModeWindows,
        UserName = FirstNonEmpty(request?.UserName, record.UserName),
        PasswordPlainText = request?.PasswordPlainText,
    };

    private static CsdtConnectionProfileListItemDto ToListItem(CsdtConnectionProfileRecord record) => new()
    {
        ProfileCode = record.ProfileCode,
        DisplayName = record.DisplayName,
        ProfileGroup = record.ProfileGroup,
        AuthMode = record.AuthMode,
        IsConfigured = IsConfigured(record),
        IsPasswordConfigured = record.IsPasswordConfigured,
        IsActive = record.IsActive,
        LastTestedAt = record.LastTestedAt,
        LastTestStatus = record.LastTestStatus,
        LastTestMessage = record.LastTestMessage,
    };

    private static CsdtConnectionProfileDetailDto ToDetail(CsdtConnectionProfileRecord record) => new()
    {
        ProfileCode = record.ProfileCode,
        DisplayName = record.DisplayName,
        ProfileGroup = record.ProfileGroup,
        ServerName = record.ServerName,
        DatabaseName = record.DatabaseName,
        AuthMode = record.AuthMode,
        UserName = record.UserName,
        IsConfigured = IsConfigured(record),
        IsPasswordConfigured = record.IsPasswordConfigured,
        IsActive = record.IsActive,
        LastTestedAt = record.LastTestedAt,
        LastTestStatus = record.LastTestStatus,
        LastTestMessage = record.LastTestMessage,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
    };

    private static bool IsConfigured(CsdtConnectionProfileRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.ServerName) || string.IsNullOrWhiteSpace(record.DatabaseName))
        {
            return false;
        }

        return !string.Equals(record.AuthMode, AuthModeSqlLogin, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(record.UserName) && record.IsPasswordConfigured);
    }

    private static string EnsureFixedProfile(string profileCode)
    {
        if (!CsdtConnectionProfileCodes.IsFixedProfile(profileCode))
        {
            throw new CsdtConnectionProfileException(
                "PROFILE_NOT_ALLOWED",
                "Chi chap nhan 7 profile ket noi co dinh trong giai doan nay.",
                404);
        }

        return CsdtConnectionProfileCodes.Normalize(profileCode);
    }

    private static string NormalizeAuthMode(string? authMode)
    {
        if (string.Equals(authMode, AuthModeWindows, StringComparison.OrdinalIgnoreCase))
        {
            return AuthModeWindows;
        }

        if (string.Equals(authMode, AuthModeSqlLogin, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authMode, "SQL Login", StringComparison.OrdinalIgnoreCase))
        {
            return AuthModeSqlLogin;
        }

        throw new CsdtConnectionProfileException(
            "AUTH_MODE_INVALID",
            "AuthMode chi chap nhan Windows hoac SqlLogin.");
    }

    private static ConnectionProfileTestOutcome BlockedTest(string message) => new()
    {
        CanTest = false,
        Succeeded = false,
        Status = "Blocked",
        SafeMessage = message,
    };

    private static string? FirstNonEmpty(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) ? first.Trim() : string.IsNullOrWhiteSpace(second) ? null : second.Trim();

    private static int SortOrder(string profileCode)
    {
        var normalized = CsdtConnectionProfileCodes.Normalize(profileCode);
        return normalized switch
        {
            CsdtConnectionProfileCodes.CsdtMoto => 1,
            CsdtConnectionProfileCodes.CsdtOto => 2,
            CsdtConnectionProfileCodes.CsdtMotoGplx => 3,
            CsdtConnectionProfileCodes.CsdtOtoGplx => 4,
            CsdtConnectionProfileCodes.DataV1 => 5,
            CsdtConnectionProfileCodes.DataV2 => 6,
            CsdtConnectionProfileCodes.QlhvApp => 7,
            _ => 99,
        };
    }

    private static string SanitizeMessage(string? message)
        => string.IsNullOrWhiteSpace(message)
            ? "Khong co thong tin chi tiet."
            : message.Length <= 1000
                ? message
                : message[..1000];
}

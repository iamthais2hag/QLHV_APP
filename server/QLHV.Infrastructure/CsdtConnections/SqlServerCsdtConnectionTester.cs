using Microsoft.Data.SqlClient;
using QLHV.Application.CsdtConnections;

namespace QLHV.Infrastructure.CsdtConnections;

public sealed class SqlServerCsdtConnectionTester : ICsdtConnectionTester
{
    public async Task<ConnectionProfileTestOutcome> TestSettingsAsync(
        ConnectionProfileTestSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerName) || string.IsNullOrWhiteSpace(settings.DatabaseName))
        {
            return NotConfigured("Profile chua co ServerName hoac DatabaseName.");
        }

        try
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = settings.ServerName,
                InitialCatalog = settings.DatabaseName,
                ConnectTimeout = 5,
                TrustServerCertificate = true,
                MultipleActiveResultSets = false,
            };

            if (string.Equals(settings.AuthMode, "SqlLogin", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(settings.UserName) || string.IsNullOrEmpty(settings.PasswordPlainText))
                {
                    return NotConfigured("Profile SQL Login chua du UserName/password.");
                }

                builder.IntegratedSecurity = false;
                builder.UserID = settings.UserName;
                builder.Password = settings.PasswordPlainText;
            }
            else
            {
                builder.IntegratedSecurity = true;
            }

            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(ex);
        }
    }

    public async Task<ConnectionProfileTestOutcome> TestConnectionStringAsync(
        string profileCode,
        string? connectionString,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return NotConfigured("Ket noi bootstrap chua duoc cau hinh.");
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(ex);
        }
    }

    private static ConnectionProfileTestOutcome Success() => new()
    {
        CanTest = true,
        Succeeded = true,
        Status = "Success",
        SafeMessage = "Test ket noi thanh cong.",
    };

    private static ConnectionProfileTestOutcome NotConfigured(string message) => new()
    {
        CanTest = false,
        Succeeded = false,
        Status = "NotConfigured",
        SafeMessage = message,
    };

    private static ConnectionProfileTestOutcome Failed(Exception ex) => new()
    {
        CanTest = true,
        Succeeded = false,
        Status = "Failed",
        SafeMessage = $"Test ket noi that bai: {ex.GetType().Name}.",
    };
}

using Microsoft.Extensions.DependencyInjection;
using QLHV.Application.Auth;

namespace QLHV.Api.Auth;

public static class FirstAdminSeedCommand
{
    public const string ArgumentName = "--seed-admin";
    public const string UsernameEnvironmentVariable = "QLHV_SEED_ADMIN_USERNAME";
    public const string DisplayNameEnvironmentVariable = "QLHV_SEED_ADMIN_DISPLAY_NAME";
    public const string PasswordEnvironmentVariable = "QLHV_SEED_ADMIN_PASSWORD";

    public static bool IsRequested(IEnumerable<string> args) =>
        args.Any(arg => string.Equals(arg, ArgumentName, StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunAsync(
        IServiceProvider services,
        TextWriter? output = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        output ??= TextWriter.Null;
        var username = Environment.GetEnvironmentVariable(UsernameEnvironmentVariable);
        var displayName = Environment.GetEnvironmentVariable(DisplayNameEnvironmentVariable);
        var password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrEmpty(password))
        {
            await output.WriteLineAsync(
                $"Seed requires {UsernameEnvironmentVariable}, {DisplayNameEnvironmentVariable}, " +
                $"and {PasswordEnvironmentVariable} for this process only.");
            return 2;
        }

        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IFirstAdminSeeder>();
        var result = await seeder.SeedAsync(
            new FirstAdminSeedRequest
            {
                Username = username,
                DisplayName = displayName,
                Password = password,
            },
            cancellationToken);

        await output.WriteLineAsync(result.Message);
        return result.Status switch
        {
            FirstAdminSeedStatus.Created => 0,
            FirstAdminSeedStatus.InvalidInput => 2,
            FirstAdminSeedStatus.AdminAlreadyExists => 3,
            FirstAdminSeedStatus.UsernameAlreadyExists => 3,
            _ => 1,
        };
    }
}

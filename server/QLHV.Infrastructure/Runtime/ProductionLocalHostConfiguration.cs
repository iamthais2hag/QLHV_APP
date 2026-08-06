using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace QLHV.Infrastructure.Runtime;

/// <summary>
/// Loads the machine-local production configuration for non-web hosts.
/// The file remains outside the repository and environment/command-line values
/// keep their normal higher precedence.
/// </summary>
public static class ProductionLocalHostConfiguration
{
    public const string DefaultRuntimeRoot = @"D:\QLHV_APP_RUNTIME";
    public const string FileName = "appsettings.Production.Local.json";

    public static string? Load(
        ConfigurationManager configuration,
        IHostEnvironment environment,
        string[] commandLineArguments)
    {
        if (!environment.IsProduction())
        {
            return null;
        }

        var runtimeRoot = configuration["QlhvRuntime:Root"];
        if (string.IsNullOrWhiteSpace(runtimeRoot))
        {
            runtimeRoot = DefaultRuntimeRoot;
        }

        var configuredPath = configuration["QlhvRuntime:ProductionLocalConfigPath"];
        var rawPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(runtimeRoot, "config", FileName)
            : configuredPath;
        var path = Path.GetFullPath(rawPath);

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                "Production Local configuration is required for the realtime worker.");
        }

        try
        {
            using var stream = File.OpenRead(path);
            _ = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (Exception exception) when (exception is
               JsonException or
               IOException or
               InvalidDataException or
               FormatException or
               UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Production Local configuration is missing, unreadable, or invalid.",
                exception);
        }

        configuration.AddJsonFile(path, optional: false, reloadOnChange: false);
        configuration.AddEnvironmentVariables();
        if (commandLineArguments.Length > 0)
        {
            configuration.AddCommandLine(commandLineArguments);
        }

        return path;
    }
}

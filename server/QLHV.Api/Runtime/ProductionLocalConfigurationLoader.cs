using System.Text.Json;
using QLHV.Application.Runtime;

namespace QLHV.Api.Runtime;

internal static class ProductionLocalConfigurationLoader
{
    internal const string DefaultRuntimeRoot = @"D:\QLHV_APP_RUNTIME";
    internal const string FileName = "appsettings.Production.Local.json";

    public static RuntimeConfigurationState Load(
        ConfigurationManager configuration,
        IWebHostEnvironment environment,
        string[] commandLineArguments)
    {
        var runtimeRoot = configuration["QlhvRuntime:Root"];
        if (string.IsNullOrWhiteSpace(runtimeRoot))
        {
            runtimeRoot = DefaultRuntimeRoot;
        }

        var configuredPath = configuration["QlhvRuntime:ProductionLocalConfigPath"];
        var rawPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(runtimeRoot, "config", FileName)
            : configuredPath.Trim();
        string path;
        try
        {
            path = Path.GetFullPath(rawPath);
        }
        catch (Exception exception) when (exception is
               ArgumentException or
               NotSupportedException or
               PathTooLongException or
               System.Security.SecurityException)
        {
            return new RuntimeConfigurationState(
                "[invalid Production.Local configuration path]",
                environment.IsProduction(),
                false,
                false);
        }

        if (!environment.IsProduction())
        {
            return new RuntimeConfigurationState(path, false, File.Exists(path), true);
        }

        if (!File.Exists(path))
        {
            return new RuntimeConfigurationState(path, true, false, false);
        }

        try
        {
            using (var stream = File.OpenRead(path))
            {
                _ = JsonDocument.Parse(stream, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
            }

            configuration.AddJsonFile(path, optional: false, reloadOnChange: false);
            // Production.Local fills machine-local defaults. Environment variables and explicit
            // command-line values must retain the standard ASP.NET Core highest precedence.
            configuration.AddEnvironmentVariables();
            if (commandLineArguments.Length > 0)
            {
                configuration.AddCommandLine(commandLineArguments);
            }

            return new RuntimeConfigurationState(path, true, true, true);
        }
        catch (JsonException)
        {
            return new RuntimeConfigurationState(path, true, true, false);
        }
        catch (IOException)
        {
            return new RuntimeConfigurationState(path, true, true, false);
        }
        catch (InvalidDataException)
        {
            return new RuntimeConfigurationState(path, true, true, false);
        }
        catch (FormatException)
        {
            return new RuntimeConfigurationState(path, true, true, false);
        }
        catch (UnauthorizedAccessException)
        {
            return new RuntimeConfigurationState(path, true, true, false);
        }
        catch (Exception exception) when (exception is
               ArgumentException or
               NotSupportedException or
               System.Security.SecurityException)
        {
            return new RuntimeConfigurationState(path, true, true, false);
        }
    }
}

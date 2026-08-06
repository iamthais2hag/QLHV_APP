using System.Text.Json;
using Microsoft.Data.SqlClient;
using QLHV.Application.Runtime;

return await TimeHealthPreflightProgram.RunAsync(args);

internal static class TimeHealthPreflightProgram
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> RunAsync(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException exception)
        {
            return Write(null, new TimeHealthContractValidationResult(
                TimeHealthPreflightExitCode.ContractSchemaInvalid,
                TimeHealthPreflightClassifications.ContractSchemaInvalid,
                exception.Message));
        }

        if (options.Help)
        {
            Console.WriteLine(Options.HelpText);
            return 0;
        }

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            var result = options.Mode switch
            {
                "api" => await ValidateApiAsync(options, timeout.Token),
                "standalone" => await ValidateStandaloneAsync(options, timeout.Token),
                "both" => await ValidateBothAsync(options, timeout.Token),
                _ => throw new ArgumentException("--mode must be api, standalone, or both."),
            };
            return Write(options.OutputPath, result);
        }
        catch (OperationCanceledException)
        {
            return Write(options.OutputPath,
                TimeHealthContractValidator.FromApiFailure(null, timedOut: true));
        }
        catch
        {
            return Write(options.OutputPath,
                TimeHealthContractValidator.FromApiFailure(null));
        }
    }

    private static async Task<TimeHealthContractValidationResult> ValidateApiAsync(
        Options options,
        CancellationToken cancellationToken)
    {
        string json;
        if (options.ContractPath is not null)
        {
            json = await File.ReadAllTextAsync(options.ContractPath, cancellationToken);
        }
        else
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
            };
            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(options.ApiUri, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return TimeHealthContractValidator.FromApiFailure(null, timedOut: true);
            }
            catch (HttpRequestException)
            {
                return TimeHealthContractValidator.FromApiFailure(null);
            }
            if (!response.IsSuccessStatusCode)
            {
                return TimeHealthContractValidator.FromApiFailure(
                    (int)response.StatusCode);
            }
            json = await response.Content.ReadAsStringAsync(cancellationToken);
        }

        return TimeHealthContractValidator.ValidateRuntimeStatusJson(
            json,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(options.MaximumAgeSeconds));
    }

    private static async Task<TimeHealthContractValidationResult> ValidateStandaloneAsync(
        Options options,
        CancellationToken cancellationToken)
    {
        if (options.ObservationPath is not null)
        {
            var fixture = await File.ReadAllTextAsync(
                options.ObservationPath, cancellationToken);
            return TimeHealthContractValidator.ValidateRuntimeStatusJson(
                fixture,
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(options.MaximumAgeSeconds));
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new TimeHealthContractValidationResult(
                TimeHealthPreflightExitCode.ContractSchemaInvalid,
                TimeHealthPreflightClassifications.ContractSchemaInvalid,
                "--connection-string is required for live standalone mode.");
        }

        DateTime databaseUtc;
        try
        {
            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT SYSUTCDATETIME();";
            command.CommandTimeout = DatabaseTimeAuthorityContract.QueryTimeoutSeconds;
            databaseUtc = (DateTime)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new TimeHealthContractValidationResult(
                TimeHealthPreflightExitCode.TimeHealthBlocked,
                TimeHealthPreflightClassifications.TimeHealthBlocked,
                TimeHealthReasonCodes.DatabaseUtcUnavailable);
        }

        var databaseUtcOffset = new DateTimeOffset(
            DateTime.SpecifyKind(databaseUtc, DateTimeKind.Utc));
        var contract = new TimeHealthContractDto
        {
            Time = new TimeHealthDto
            {
                TimeHealth = TimeHealthStatuses.Healthy,
                ReasonCode = TimeHealthReasonCodes.None,
                WritesAllowed = true,
                DatabaseClockAvailable = true,
                DatabaseUtcNow = databaseUtcOffset,
                ServerUtcNow = databaseUtcOffset,
                EvaluatedAtUtc = databaseUtcOffset,
            },
        };
        return TimeHealthContractValidator.ValidateRuntimeStatusJson(
            JsonSerializer.Serialize(contract, JsonOptions),
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(options.MaximumAgeSeconds));
    }

    private static async Task<TimeHealthContractValidationResult> ValidateBothAsync(
        Options options,
        CancellationToken cancellationToken)
    {
        var standalone = await ValidateStandaloneAsync(options, cancellationToken);
        if (!standalone.IsHealthy)
        {
            return standalone;
        }
        var api = await ValidateApiAsync(options, cancellationToken);
        return api.IsHealthy
            ? api
            : TimeHealthContractValidator.FromPolicyDivergence();
    }

    private static int Write(
        string? outputPath,
        TimeHealthContractValidationResult result)
    {
        var json = JsonSerializer.Serialize(new
        {
            timeContractVersion = TimeHealthContract.Version,
            exitCode = (int)result.ExitCode,
            result.Classification,
            result.Reason,
        }, JsonOptions);
        Console.WriteLine(json);
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, json);
        }
        return (int)result.ExitCode;
    }

    private sealed record Options
    {
        public string Mode { get; init; } = "api";
        public Uri ApiUri { get; init; } = new("http://127.0.0.1:5000/api/system/time-health");
        public string? ConnectionString { get; init; }
        public string? ContractPath { get; init; }
        public string? ObservationPath { get; init; }
        public string? OutputPath { get; init; }
        public int TimeoutSeconds { get; init; } = 5;
        public int MaximumAgeSeconds { get; init; } = 30;
        public bool Help { get; init; }

        public const string HelpText = """
            QLHV SQL UTC preflight
              --mode api|standalone|both
              --api-uri <absolute-uri>
              --connection-string <SQL connection string> (live standalone/both)
              --contract-file <JSON contract fixture>
              --observation-file <JSON contract fixture>
              --timeout-seconds <1..60>
              --maximum-age-seconds <1..300>
              --output <path>
            """;

        public static Options Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < args.Length; index++)
            {
                if (args[index] is "--help" or "-h")
                {
                    return new Options { Help = true };
                }
                if (!args[index].StartsWith("--", StringComparison.Ordinal) ||
                    index + 1 >= args.Length)
                {
                    throw new ArgumentException($"Invalid argument: {args[index]}");
                }
                values[args[index]] = args[++index];
            }

            var mode = values.GetValueOrDefault("--mode") ?? "api";
            if (mode is not ("api" or "standalone" or "both"))
            {
                throw new ArgumentException("--mode must be api, standalone, or both.");
            }
            var apiText = values.GetValueOrDefault("--api-uri") ??
                "http://127.0.0.1:5000/api/system/time-health";
            if (!Uri.TryCreate(apiText, UriKind.Absolute, out var apiUri))
            {
                throw new ArgumentException("--api-uri must be absolute.");
            }
            var timeout = ParseRange(values, "--timeout-seconds", 5, 1, 60);
            var maximumAge = ParseRange(values, "--maximum-age-seconds", 30, 1, 300);
            return new Options
            {
                Mode = mode,
                ApiUri = apiUri,
                ConnectionString = values.GetValueOrDefault("--connection-string"),
                ContractPath = values.GetValueOrDefault("--contract-file"),
                ObservationPath = values.GetValueOrDefault("--observation-file"),
                OutputPath = values.GetValueOrDefault("--output"),
                TimeoutSeconds = timeout,
                MaximumAgeSeconds = maximumAge,
            };
        }

        private static int ParseRange(
            IReadOnlyDictionary<string, string> values,
            string key,
            int fallback,
            int minimum,
            int maximum)
        {
            if (!values.TryGetValue(key, out var text))
            {
                return fallback;
            }
            if (!int.TryParse(text, out var value) || value < minimum || value > maximum)
            {
                throw new ArgumentException($"{key} must be {minimum}..{maximum}.");
            }
            return value;
        }
    }
}

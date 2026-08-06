using System.Globalization;
using System.Text.Json;

namespace QLHV.Application.Runtime;

public static class TimeHealthPreflightClassifications
{
    public const string TimeHealthy = "DATABASE_CLOCK_AVAILABLE";
    public const string ApiUnavailable = "API_UNAVAILABLE";
    public const string TimeObjectMissing = "TIME_OBJECT_MISSING";
    public const string TimeHealthBlocked = "DATABASE_CLOCK_UNAVAILABLE";
    public const string ContractVersionMismatch = "CONTRACT_VERSION_MISMATCH";
    public const string ContractSchemaInvalid = "CONTRACT_SCHEMA_INVALID";
    public const string ContractStale = "DATABASE_CLOCK_EVIDENCE_STALE";
    public const string InvalidJson = "INVALID_JSON";
    public const string Unauthorized = "API_UNAUTHORIZED";
    public const string HttpNotFound = "API_NOT_FOUND";
    public const string Timeout = "API_TIMEOUT";
    public const string TimePolicyDivergence = "DATABASE_CLOCK_POLICY_DIVERGENCE";
}

public enum TimeHealthPreflightExitCode
{
    Healthy = 0,
    ApiUnavailable = 10,
    TimeObjectMissing = 11,
    TimeHealthBlocked = 12,
    ContractVersionMismatch = 14,
    ContractSchemaInvalid = 15,
    ContractStale = 16,
    InvalidJson = 21,
    Unauthorized = 22,
    HttpNotFound = 23,
    Timeout = 24,
    TimePolicyDivergence = 25,
}

public sealed record TimeHealthContractValidationResult(
    TimeHealthPreflightExitCode ExitCode,
    string Classification,
    string Reason)
{
    public bool IsHealthy => ExitCode == TimeHealthPreflightExitCode.Healthy;
    public bool IsApproved => IsHealthy;
}

/// <summary>
/// StrictMode for contract 2.0 validates one fact: a fresh SQL
/// SYSUTCDATETIME() probe succeeded. W32Time/NTP and historical timestamps are
/// intentionally outside the authorization decision.
/// </summary>
public static class TimeHealthContractValidator
{
    public static readonly TimeSpan DefaultMaximumAge = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan AllowedFutureTolerance = TimeSpan.FromSeconds(5);

    public static TimeHealthContractValidationResult FromApiFailure(
        int? statusCode,
        bool timedOut = false)
    {
        if (timedOut)
        {
            return Invalid(TimeHealthPreflightExitCode.Timeout,
                TimeHealthPreflightClassifications.Timeout,
                "Database-clock API request timed out.");
        }
        if (statusCode is 401 or 403)
        {
            return Invalid(TimeHealthPreflightExitCode.Unauthorized,
                TimeHealthPreflightClassifications.Unauthorized,
                "Database-clock API authorization failed.");
        }
        if (statusCode == 404)
        {
            return Invalid(TimeHealthPreflightExitCode.HttpNotFound,
                TimeHealthPreflightClassifications.HttpNotFound,
                "Database-clock API endpoint was not found.");
        }
        return Invalid(TimeHealthPreflightExitCode.ApiUnavailable,
            TimeHealthPreflightClassifications.ApiUnavailable,
            "Database-clock API is unavailable.");
    }

    public static TimeHealthContractValidationResult FromPolicyDivergence() =>
        Invalid(TimeHealthPreflightExitCode.TimePolicyDivergence,
            TimeHealthPreflightClassifications.TimePolicyDivergence,
            "API and standalone SQL-clock decisions differ.");

    public static TimeHealthContractValidationResult ValidateRuntimeStatusJson(
        string json,
        DateTimeOffset observedAtUtc,
        TimeSpan? maximumAge = null)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Invalid(TimeHealthPreflightExitCode.InvalidJson,
                TimeHealthPreflightClassifications.InvalidJson,
                "Response is not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid(TimeHealthPreflightExitCode.ContractSchemaInvalid,
                    TimeHealthPreflightClassifications.ContractSchemaInvalid,
                    "Contract root must be an object.");
            }

            if (!root.TryGetProperty("timeContractVersion", out var version) ||
                version.ValueKind != JsonValueKind.String)
            {
                return Invalid(TimeHealthPreflightExitCode.ContractVersionMismatch,
                    TimeHealthPreflightClassifications.ContractVersionMismatch,
                    "timeContractVersion is missing.");
            }
            if (!string.Equals(version.GetString(), TimeHealthContract.Version,
                    StringComparison.Ordinal))
            {
                return Invalid(TimeHealthPreflightExitCode.ContractVersionMismatch,
                    TimeHealthPreflightClassifications.ContractVersionMismatch,
                    $"Expected time contract {TimeHealthContract.Version}.");
            }

            if (!root.TryGetProperty("time", out var time) ||
                time.ValueKind != JsonValueKind.Object)
            {
                return Invalid(TimeHealthPreflightExitCode.TimeObjectMissing,
                    TimeHealthPreflightClassifications.TimeObjectMissing,
                    "time object is missing.");
            }

            if (!TryBoolean(time, "databaseClockAvailable", out var available) ||
                !TryBoolean(time, "writesAllowed", out var writesAllowed) ||
                !TryNullableUtc(time, "databaseUtcNow", out var databaseUtc) ||
                !TryUtc(time, "evaluatedAtUtc", out var evaluatedAt))
            {
                return Invalid(TimeHealthPreflightExitCode.ContractSchemaInvalid,
                    TimeHealthPreflightClassifications.ContractSchemaInvalid,
                    "SQL-clock fields are missing or invalid.");
            }

            var maxAge = maximumAge ?? DefaultMaximumAge;
            var age = observedAtUtc - evaluatedAt;
            if (age > maxAge || age < -AllowedFutureTolerance)
            {
                return Invalid(TimeHealthPreflightExitCode.ContractStale,
                    TimeHealthPreflightClassifications.ContractStale,
                    "SQL-clock evidence is not fresh.");
            }

            if (!available || !writesAllowed || databaseUtc is null)
            {
                return Invalid(TimeHealthPreflightExitCode.TimeHealthBlocked,
                    TimeHealthPreflightClassifications.TimeHealthBlocked,
                    TimeHealthReasonCodes.DatabaseUtcUnavailable);
            }

            return new TimeHealthContractValidationResult(
                TimeHealthPreflightExitCode.Healthy,
                TimeHealthPreflightClassifications.TimeHealthy,
                "SQL SYSUTCDATETIME() is available.");
        }
    }

    private static TimeHealthContractValidationResult Invalid(
        TimeHealthPreflightExitCode exitCode,
        string classification,
        string reason) => new(exitCode, classification, reason);

    private static bool TryBoolean(
        JsonElement parent,
        string name,
        out bool value)
    {
        value = false;
        return parent.TryGetProperty(name, out var property) &&
               (property.ValueKind == JsonValueKind.True ||
                property.ValueKind == JsonValueKind.False) &&
               (value = property.GetBoolean()) == value;
    }

    private static bool TryNullableUtc(
        JsonElement parent,
        string name,
        out DateTimeOffset? value)
    {
        value = null;
        if (!parent.TryGetProperty(name, out var property))
        {
            return false;
        }
        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (property.ValueKind != JsonValueKind.String ||
            !ParseUtc(property.GetString(), out var parsed))
        {
            return false;
        }
        value = parsed;
        return true;
    }

    private static bool TryUtc(
        JsonElement parent,
        string name,
        out DateTimeOffset value)
    {
        value = default;
        return parent.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               ParseUtc(property.GetString(), out value);
    }

    private static bool ParseUtc(string? text, out DateTimeOffset value)
    {
        value = default;
        return !string.IsNullOrWhiteSpace(text) &&
               DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
                   out value) &&
               value.Offset == TimeSpan.Zero;
    }
}

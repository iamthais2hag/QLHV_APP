using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using QLHV.Application.Runtime;
using QLHV.Application.Sync;

namespace QLHV.Infrastructure.Runtime;

public sealed class RuntimeBuildIdentity : IRuntimeBuildIdentity
{
    public RuntimeBuildIdentity(IHostEnvironment environment)
    {
        Current = Create(environment);
    }

    public RuntimeBuildIdentityDto Current { get; }

    private static RuntimeBuildIdentityDto Create(IHostEnvironment environment)
    {
        var entryAssembly = Assembly.GetEntryAssembly() ?? typeof(RuntimeBuildIdentity).Assembly;
        var assemblies = new[]
            {
                entryAssembly,
                typeof(RuntimeBuildIdentity).Assembly,
                typeof(QlhvAutoSyncService).Assembly,
            }
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .DistinctBy(assembly => assembly.Location, StringComparer.OrdinalIgnoreCase)
            .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();
        var apiBuildId = ComputeBuildId(assemblies);
        var informationalVersion = entryAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var frontend = ReadFrontendIdentity(environment.ContentRootPath);
        var instanceId = Guid.NewGuid().ToString("N");
        var processStartedAtUtc = GetProcessStartedAtUtc();
        var hostProcess = entryAssembly.GetName().Name ?? "unknown";

        return new RuntimeBuildIdentityDto
        {
            ApplicationVersion = informationalVersion?.Split('+', 2)[0]
                ?? entryAssembly.GetName().Version?.ToString()
                ?? "unknown",
            CommitSha = ParseCommitSha(informationalVersion),
            ApiBuildId = apiBuildId,
            WorkerBuildId = apiBuildId,
            ApiBuiltAtUtc = assemblies
                .Select(assembly => File.GetLastWriteTimeUtc(assembly.Location))
                .DefaultIfEmpty()
                .Max(),
            ProcessStartedAtUtc = processStartedAtUtc,
            InstanceId = instanceId,
            HostProcess = hostProcess,
            Environment = environment.EnvironmentName,
            WorkerInstanceId = $"{hostProcess}:{instanceId}",
            FrontendBuildId = frontend.BuildId,
            FrontendBuiltAtUtc = frontend.BuiltAtUtc,
        };
    }

    private static string ComputeBuildId(IEnumerable<Assembly> assemblies)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var assembly in assemblies)
        {
            var name = Encoding.UTF8.GetBytes(assembly.GetName().Name ?? string.Empty);
            hash.AppendData(name);
            using var stream = File.OpenRead(assembly.Location);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static DateTime GetProcessStartedAtUtc()
    {
        try
        {
            return Process.GetCurrentProcess().StartTime.ToUniversalTime();
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }

    private static string? ParseCommitSha(string? informationalVersion)
    {
        var metadata = informationalVersion?.Split('+', 2).ElementAtOrDefault(1);
        return metadata is not null &&
               metadata.Length is >= 7 and <= 64 &&
               metadata.All(Uri.IsHexDigit)
            ? metadata.ToLowerInvariant()
            : null;
    }

    private static FrontendIdentity ReadFrontendIdentity(string contentRootPath)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "build-info.json"),
            Path.Combine(contentRootPath, "wwwroot", "build-info.json"),
        };
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                var buildId = root.TryGetProperty("frontendBuildId", out var id)
                    ? id.GetString()
                    : null;
                var builtAt = root.TryGetProperty("frontendBuiltAtUtc", out var timestamp) &&
                              timestamp.TryGetDateTime(out var parsed)
                    ? parsed.ToUniversalTime()
                    : (DateTime?)null;
                if (!string.IsNullOrWhiteSpace(buildId))
                {
                    return new FrontendIdentity(buildId, builtAt);
                }
            }
            catch
            {
                // Build identity must never make the host unavailable.
            }
        }

        return new FrontendIdentity("unknown", null);
    }

    private sealed record FrontendIdentity(string BuildId, DateTime? BuiltAtUtc);
}

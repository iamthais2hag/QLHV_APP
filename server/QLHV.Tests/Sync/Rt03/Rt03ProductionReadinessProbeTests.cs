using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QLHV.Application;
using QLHV.Application.CsdtConnections;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Rt01;
using QLHV.Infrastructure;
using QLHV.Infrastructure.Sync;
using QLHV.Infrastructure.Sync.Rt01;
using Xunit.Abstractions;

namespace QLHV.Tests.Sync.Rt03;

public sealed class Rt03ProductionReadinessProbeTests
{
    private readonly ITestOutputHelper _output;

    public Rt03ProductionReadinessProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "ProductionReadOnlyOptIn")]
    public async Task Current_oto_then_moto_drift_is_privacy_safe_stable_and_select_only()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("QLHV_RUN_RT03_READ_ONLY_PROBE"),
                "1",
                StringComparison.Ordinal))
        {
            _output.WriteLine(
                "RT-03 production readiness probe not requested; no production connection opened.");
            return;
        }

        var runtimeConfig =
            Environment.GetEnvironmentVariable("QLHV_RT03_RUNTIME_CONFIG") ??
            @"D:\QLHV_APP_RUNTIME\config\appsettings.Production.Local.json";
        Assert.True(File.Exists(runtimeConfig), $"Missing runtime config: {runtimeConfig}");

        var configuration = new ConfigurationManager();
        configuration.AddJsonFile(runtimeConfig, optional: false, reloadOnChange: false);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddApplication();
        services.AddInfrastructureCore(configuration, @"D:\QLHV_APP");

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var reader = new Rt01aOtoDriftEvidenceReader(
            scope.ServiceProvider.GetRequiredService<QlhvImportReadRepository>(),
            scope.ServiceProvider.GetRequiredService<ICsdtConnectionProfileRepository>(),
            scope.ServiceProvider.GetRequiredService<IConnectionPasswordProtector>(),
            scope.ServiceProvider.GetRequiredService<IConnectionSettingsProvider>(),
            scope.ServiceProvider.GetRequiredService<IOptions<SyncOptions>>());
        var hmacKey = RandomNumberGenerator.GetBytes(32);
        var results = new List<object>();

        foreach (var route in Rt01ShadowRouteCatalog.Ordered)
        {
            var samples = new List<Rt01aProbeEvidence>();
            for (var sample = 0; sample < 3; sample++)
            {
                samples.Add(Rt01aDriftClassifier.Classify(
                    await reader.ReadAsync(route),
                    hmacKey,
                    route.SourceProfileCode));
            }

            var first = samples[0];
            Assert.All(samples, evidence =>
            {
                Assert.Equal(Rt01aProofContract.MappingContractStatus,
                    evidence.MappingContractStatus);
                Assert.False(string.IsNullOrWhiteSpace(evidence.SourceSchemaFingerprint));
                Assert.False(string.IsNullOrWhiteSpace(evidence.TargetSchemaFingerprint));
                Assert.Equal(first.SourceActiveRows, evidence.SourceActiveRows);
                Assert.Equal(first.TargetActiveRows, evidence.TargetActiveRows);
                Assert.Equal(first.TargetSoftDeletedRows, evidence.TargetSoftDeletedRows);
                Assert.Equal(first.SourceKeySetHash, evidence.SourceKeySetHash);
                Assert.Equal(first.TargetKeySetHash, evidence.TargetKeySetHash);
                Assert.Equal(first.StageHash, evidence.StageHash);
                Assert.Equal(first.TargetComparisonHash, evidence.TargetComparisonHash);
                Assert.Equal(0, evidence.BusinessDataWrites);
                Assert.False(evidence.ApplyCheckpointPublished);
                Assert.False(evidence.ExistingAutoSyncTouched);
            });

            results.Add(new
            {
                route.SourceType,
                route.SourceProfileCode,
                route.SourceDatabaseName,
                route.MaCsdt,
                SampleCount = samples.Count,
                Samples = samples,
            });
        }

        _output.WriteLine(JsonSerializer.Serialize(new
        {
            Evidence = "RT03_CURRENT_PRODUCTION_DRIFT",
            RouteOrder = new[] { "OTO", "MOTO" },
            Consistency = Rt01aProofContract.ConsistencyLevel,
            BusinessDataWrites = 0,
            ApplyCheckpointPublished = false,
            ExistingAutoSyncTouched = false,
            Profiles = results,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

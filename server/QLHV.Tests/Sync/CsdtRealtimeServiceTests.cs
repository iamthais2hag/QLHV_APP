using Microsoft.Extensions.Options;
using QLHV.Application.Auth;
using QLHV.Application.Sync.Realtime;

namespace QLHV.Tests.Sync;

public sealed class CsdtRealtimeServiceTests
{
    private static readonly CsdtRealtimeUserContext Admin =
        new("admin", AppRoles.Admin, true);

    private static readonly CsdtRealtimeUserContext Viewer =
        new("viewer", AppRoles.Viewer, false);

    [Fact]
    public async Task Status_exposes_fixed_routes_and_never_authorizes_viewer_writes()
    {
        var fixture = Fixture.Live();

        var viewerResult = await fixture.Service.GetStreamsAsync(Viewer);
        var adminResult = await fixture.Service.GetStreamsAsync(Admin);

        Assert.Collection(
            viewerResult.Streams,
            oto =>
            {
                Assert.Equal(CsdtRealtimeStreamCodes.OtoV2ToV1, oto.StreamCode);
                Assert.Equal(CsdtRealtimeDatabaseNames.OtoV2, oto.SourceDatabaseName);
                Assert.Equal(CsdtRealtimeDatabaseNames.OtoV1, oto.TargetDatabaseName);
                Assert.Equal(AppRoles.Viewer, oto.CurrentUserRole);
                Assert.False(oto.WriteAuthorized);
            },
            moto =>
            {
                Assert.Equal(CsdtRealtimeStreamCodes.MotoV2ToV1, moto.StreamCode);
                Assert.Equal(CsdtRealtimeDatabaseNames.MotoV2, moto.SourceDatabaseName);
                Assert.Equal(CsdtRealtimeDatabaseNames.MotoV1, moto.TargetDatabaseName);
                Assert.False(moto.WriteAuthorized);
            });
        Assert.All(adminResult.Streams, stream => Assert.True(stream.WriteAuthorized));
    }

    [Fact]
    public async Task Backup_status_exposes_configured_backup_route_and_allows_admin_commands()
    {
        var fixture = Fixture.Backup();

        var result = await fixture.Service.GetStreamsAsync(Admin);

        Assert.Collection(
            result.Streams,
            oto =>
            {
                Assert.Equal(CsdtRealtimeVehicleTypes.Oto, oto.VehicleType);
                Assert.Equal(CsdtRealtimeProfileCodes.OtoV2Bak, oto.SourceProfileCode);
                Assert.Equal(CsdtRealtimeProfileCodes.OtoV1Bak, oto.TargetProfileCode);
                Assert.Equal(CsdtRealtimeDatabaseNames.OtoV2Bak, oto.SourceDatabaseName);
                Assert.Equal(CsdtRealtimeDatabaseNames.OtoV1Bak, oto.TargetDatabaseName);
                Assert.True(oto.WriteAuthorized);
            },
            moto =>
            {
                Assert.Equal(CsdtRealtimeVehicleTypes.Moto, moto.VehicleType);
                Assert.Equal(CsdtRealtimeProfileCodes.MotoV2Bak, moto.SourceProfileCode);
                Assert.Equal(CsdtRealtimeProfileCodes.MotoV1Bak, moto.TargetProfileCode);
                Assert.Equal(CsdtRealtimeDatabaseNames.MotoV2Bak, moto.SourceDatabaseName);
                Assert.Equal(CsdtRealtimeDatabaseNames.MotoV1Bak, moto.TargetDatabaseName);
                Assert.True(moto.WriteAuthorized);
            });
    }
    [Fact]
    public async Task Unknown_database_in_state_is_fail_closed_and_not_exposed()
    {
        var fixture = Fixture.Live();
        fixture.State.Streams[0] = fixture.State.Streams[0] with
        {
            SourceDatabaseName = "ARBITRARY_DATABASE",
        };

        var result = await fixture.Service.GetStreamsAsync(Admin);

        var oto = result.Streams[0];
        Assert.Equal(CsdtRealtimeDatabaseNames.OtoV2, oto.SourceDatabaseName);
        Assert.False(oto.WriteAuthorized);
        Assert.Contains(
            oto.ActionBlockers,
            blocker => blocker.Contains("allowlist", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enabled_command_preserves_opaque_token_and_fixed_route()
    {
        var fixture = Fixture.Live();

        var result = await fixture.Service.SetEnabledAsync(
            CsdtRealtimeStreamCodes.OtoV2ToV1,
            new CsdtRealtimeEnableRequest
            {
                Enabled = false,
                ExpectedStateToken = "state:OTO:0001",
            },
            Admin);

        Assert.True(result.Accepted);
        var command = Assert.Single(fixture.Commands.Commands);
        Assert.Equal(CsdtRealtimeCommandTypes.SetEnabled, command.CommandType);
        Assert.Equal("state:OTO:0001", command.ExpectedStateToken);
        Assert.Equal(false, command.Enabled);
        Assert.Equal(CsdtRealtimeProfileCodes.OtoV2, command.SourceProfileCode);
        Assert.Equal(CsdtRealtimeProfileCodes.OtoV1, command.TargetProfileCode);
        Assert.Equal(CsdtRealtimeDatabaseNames.OtoV2, command.SourceDatabaseName);
        Assert.Equal(CsdtRealtimeDatabaseNames.OtoV1, command.TargetDatabaseName);
        Assert.Equal("66029", command.MaCSDT);
        Assert.Equal("admin", command.RequestedBy);
    }

    [Fact]
    public async Task Viewer_cannot_delegate_any_write_even_when_service_is_called_directly()
    {
        var fixture = Fixture.Live();

        await Assert.ThrowsAsync<CsdtRealtimeAuthorizationException>(() =>
            fixture.Service.QueueBaselineAsync(
                CsdtRealtimeStreamCodes.OtoV2ToV1,
                new CsdtRealtimeBaselineRequest { ExpectedStateToken = "state-token" },
                Viewer));

        Assert.Empty(fixture.Commands.Commands);
    }

    [Theory]
    [InlineData(AppRoles.Employee)]
    [InlineData(AppRoles.Viewer)]
    public async Task Non_admin_cannot_execute_reverse(string role)
    {
        var fixture = Fixture.Live();
        var user = new CsdtRealtimeUserContext(role.ToLowerInvariant(), role, false);

        await Assert.ThrowsAsync<CsdtRealtimeAuthorizationException>(() =>
            fixture.Service.ExecuteReverseAsync(
                new CsdtReverseExecuteRequest
                {
                    VehicleType = CsdtRealtimeVehicleTypes.Oto,
                    ExpectedPlanToken = "approved-plan",
                },
                user));

        Assert.Empty(fixture.Commands.Commands);
    }

    [Fact]
    public async Task State_token_is_not_trimmed_before_enqueue()
    {
        var fixture = Fixture.Live();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.QueueRetryAsync(
                CsdtRealtimeStreamCodes.OtoV2ToV1,
                new CsdtRealtimeRetryRequest { ExpectedStateToken = " state-token" },
                Admin));

        Assert.Empty(fixture.Commands.Commands);
    }

    [Fact]
    public async Task Backup_mode_delegates_only_fixed_backup_route()
    {
        var fixture = Fixture.Backup();

        await fixture.Service.QueueBaselineAsync(
            CsdtRealtimeStreamCodes.MotoV2ToV1,
            new CsdtRealtimeBaselineRequest { ExpectedStateToken = "moto-state" },
            Admin);

        var command = Assert.Single(fixture.Commands.Commands);
        Assert.Equal(CsdtRealtimeProfileCodes.MotoV2Bak, command.SourceProfileCode);
        Assert.Equal(CsdtRealtimeProfileCodes.MotoV1Bak, command.TargetProfileCode);
        Assert.Equal(CsdtRealtimeDatabaseNames.MotoV2Bak, command.SourceDatabaseName);
        Assert.Equal(CsdtRealtimeDatabaseNames.MotoV1Bak, command.TargetDatabaseName);
    }

    [Theory]
    [InlineData("enabled")]
    [InlineData("baseline")]
    [InlineData("retry")]
    public async Task Backup_mode_write_commands_use_configured_backup_route(string action)
    {
        var fixture = Fixture.Backup();

        if (action == "enabled")
        {
            await fixture.Service.SetEnabledAsync(
                CsdtRealtimeStreamCodes.OtoV2ToV1,
                new CsdtRealtimeEnableRequest
                {
                    Enabled = false,
                    ExpectedStateToken = "state-token",
                },
                Admin);
        }
        else if (action == "baseline")
        {
            await fixture.Service.QueueBaselineAsync(
                CsdtRealtimeStreamCodes.OtoV2ToV1,
                new CsdtRealtimeBaselineRequest { ExpectedStateToken = "state-token" },
                Admin);
        }
        else
        {
            await fixture.Service.QueueRetryAsync(
                CsdtRealtimeStreamCodes.OtoV2ToV1,
                new CsdtRealtimeRetryRequest { ExpectedStateToken = "state-token" },
                Admin);
        }

        var command = Assert.Single(fixture.Commands.Commands);
        Assert.Equal(CsdtRealtimeProfileCodes.OtoV2Bak, command.SourceProfileCode);
        Assert.Equal(CsdtRealtimeProfileCodes.OtoV1Bak, command.TargetProfileCode);
        Assert.Equal(CsdtRealtimeDatabaseNames.OtoV2Bak, command.SourceDatabaseName);
        Assert.Equal(CsdtRealtimeDatabaseNames.OtoV1Bak, command.TargetDatabaseName);
    }
    [Fact]
    public async Task Reverse_plan_uses_server_selected_v1_to_v2_route_and_raw_course()
    {
        var fixture = Fixture.Live();
        fixture.Reverse.Plan = ExecutablePlan("plan-001");

        var plan = await fixture.Service.GetReversePlanAsync(
            CsdtRealtimeVehicleTypes.Oto,
            "66029K260001");

        Assert.True(plan.IsReadOnly);
        Assert.Equal(CsdtRealtimeDirections.V1ToV2, plan.Direction);
        Assert.Equal(CsdtRealtimeDatabaseNames.OtoV1, plan.SourceDatabaseName);
        Assert.Equal(CsdtRealtimeDatabaseNames.OtoV2, plan.TargetDatabaseName);
        Assert.Equal("66029K260001", plan.MaKhoaHoc);
        Assert.Equal("66029K260001", fixture.Reverse.LastMaKhoaHoc);
        Assert.NotNull(fixture.Reverse.LastRoute);
        Assert.Equal(CsdtRealtimeProfileCodes.OtoV1, fixture.Reverse.LastRoute!.SourceProfileCode);
        Assert.Equal(CsdtRealtimeProfileCodes.OtoV2, fixture.Reverse.LastRoute.TargetProfileCode);
    }

    [Fact]
    public async Task Reverse_plan_rejects_whitespace_instead_of_normalizing_identity()
    {
        var fixture = Fixture.Live();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.GetReversePlanAsync(
                CsdtRealtimeVehicleTypes.Oto,
                " 66029K260001"));

        Assert.Equal(0, fixture.Reverse.Calls);
    }

    [Fact]
    public async Task Stale_reverse_plan_is_not_enqueued_and_returns_current_plan()
    {
        var fixture = Fixture.Live();
        fixture.Reverse.Plan = ExecutablePlan("current-plan");

        var result = await fixture.Service.ExecuteReverseAsync(
            new CsdtReverseExecuteRequest
            {
                VehicleType = CsdtRealtimeVehicleTypes.Oto,
                MaKhoaHoc = "66029K260001",
                ExpectedPlanToken = "stale-plan",
            },
            Admin);

        Assert.False(result.Accepted);
        Assert.Equal(CsdtRealtimeActionStatuses.Conflict, result.Status);
        Assert.Equal("current-plan", result.Plan?.PlanToken);
        Assert.Empty(fixture.Commands.Commands);
    }

    [Fact]
    public async Task Stale_plan_token_is_reused_only_for_a_durable_retryable_reverse_run()
    {
        var fixture = Fixture.Live();
        fixture.Reverse.Plan = ExecutablePlan("current-plan");
        fixture.Commands.HasRetryableReverse = true;

        var result = await fixture.Service.ExecuteReverseAsync(
            new CsdtReverseExecuteRequest
            {
                VehicleType = CsdtRealtimeVehicleTypes.Oto,
                ExpectedPlanToken = "approved-plan",
            },
            Admin);

        Assert.True(result.Accepted);
        var command = Assert.Single(fixture.Commands.Commands);
        Assert.Equal("approved-plan", command.ExpectedPlanToken);
        Assert.Equal(CsdtRealtimeCommandTypes.ReverseExecute, command.CommandType);
    }

    [Theory]
    [InlineData(1, 0, 0, "V1_ONLY_REQUIRES_REVIEW")]
    [InlineData(0, 1, 0, "IDENTITY_CHANGED")]
    [InlineData(0, 0, 1, "CONFLICT_REQUIRES_REVIEW")]
    public async Task Reverse_review_counts_fail_closed_even_if_repository_marks_plan_executable(
        long v1Only,
        long identityChanged,
        long conflict,
        string blockerCode)
    {
        var fixture = Fixture.Live();
        fixture.Reverse.Plan = ExecutablePlan("plan") with
        {
            V1OnlyRequiresReview = v1Only,
            IdentityChanged = identityChanged,
            ConflictRequiresReview = conflict,
        };

        var plan = await fixture.Service.GetReversePlanAsync(
            CsdtRealtimeVehicleTypes.Oto,
            "66029K260001");

        Assert.False(plan.Executable);
        Assert.Contains(
            plan.Blockers,
            blocker => blocker.Contains(blockerCode, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Executable_reverse_plan_delegates_command_without_rewriting_identity()
    {
        var fixture = Fixture.Live();
        fixture.Reverse.Plan = ExecutablePlan("exact-plan-token");

        var result = await fixture.Service.ExecuteReverseAsync(
            new CsdtReverseExecuteRequest
            {
                VehicleType = CsdtRealtimeVehicleTypes.Moto,
                MaKhoaHoc = "66030K260001",
                ExpectedPlanToken = "exact-plan-token",
            },
            Admin);

        Assert.True(result.Accepted);
        var command = Assert.Single(fixture.Commands.Commands);
        Assert.Equal(CsdtRealtimeCommandTypes.ReverseExecute, command.CommandType);
        Assert.Equal(CsdtRealtimeProfileCodes.MotoV1, command.SourceProfileCode);
        Assert.Equal(CsdtRealtimeProfileCodes.MotoV2, command.TargetProfileCode);
        Assert.Equal("66030K260001", command.MaKhoaHoc);
        Assert.Equal("exact-plan-token", command.ExpectedPlanToken);
        Assert.Null(command.ExpectedStateToken);
    }

    [Fact]
    public async Task Missing_state_is_reported_read_only_instead_of_inventing_a_token()
    {
        var fixture = Fixture.Live();
        fixture.State.Streams.RemoveAll(stream =>
            string.Equals(
                stream.StreamCode,
                CsdtRealtimeStreamCodes.MotoV2ToV1,
                StringComparison.Ordinal));

        var result = await fixture.Service.GetStreamsAsync(Admin);

        var moto = Assert.Single(result.Streams.Where(stream =>
            stream.StreamCode == CsdtRealtimeStreamCodes.MotoV2ToV1));
        Assert.Equal("NOT_CONFIGURED", moto.State);
        Assert.Equal(string.Empty, moto.StateToken);
        Assert.False(moto.WriteAuthorized);
        Assert.NotEmpty(moto.ActionBlockers);
    }

    private static CsdtReversePlanDto ExecutablePlan(string token)
        => new()
        {
            PlanToken = token,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Executable = true,
            Blockers = [],
            Warnings = [],
        };

    private sealed class Fixture
    {
        private Fixture(CsdtRealtimeSyncOptions options)
        {
            var routes = CsdtRealtimeStreamCatalog.GetConfiguredRoutes(options);
            State.Streams.AddRange(routes.Select(StatusFor));
            Service = new CsdtRealtimeService(
                State,
                Commands,
                Reverse,
                Options.Create(options));
        }

        public FakeStateRepository State { get; } = new();

        public FakeCommandRepository Commands { get; } = new();

        public FakeReversePlanRepository Reverse { get; } = new();

        public CsdtRealtimeService Service { get; }

        public static Fixture Live()
            => new(new CsdtRealtimeSyncOptions { Enabled = true });

        public static Fixture Backup()
            => new(new CsdtRealtimeSyncOptions
            {
                Enabled = true,
                UseBackupProfiles = true,
                Streams = new CsdtRealtimeStreamsOptions
                {
                    Oto = new CsdtRealtimeStreamOptions
                    {
                        Enabled = true,
                        StreamCode = CsdtRealtimeStreamCodes.OtoV2ToV1,
                        SourceProfile = CsdtRealtimeProfileCodes.OtoV2Bak,
                        TargetProfile = CsdtRealtimeProfileCodes.OtoV1Bak,
                        MaCSDT = "66029",
                    },
                    Moto = new CsdtRealtimeStreamOptions
                    {
                        Enabled = true,
                        StreamCode = CsdtRealtimeStreamCodes.MotoV2ToV1,
                        SourceProfile = CsdtRealtimeProfileCodes.MotoV2Bak,
                        TargetProfile = CsdtRealtimeProfileCodes.MotoV1Bak,
                        MaCSDT = "66030",
                    },
                },
            });

        private static CsdtRealtimeStreamStatusDto StatusFor(
            CsdtRealtimeRouteDefinition route)
            => new()
            {
                StreamCode = route.StreamCode,
                VehicleType = route.VehicleType,
                SourceProfileCode = route.SourceProfileCode,
                TargetProfileCode = route.TargetProfileCode,
                SourceDatabaseName = route.SourceDatabaseName,
                TargetDatabaseName = route.TargetDatabaseName,
                MaCSDT = route.MaCSDT,
                Enabled = true,
                State = "RUNNING",
                BaselineStatus = "COMPLETED",
                StateToken = "state:" + route.StreamCode,
            };
    }

    private sealed class FakeStateRepository : ICsdtRealtimeStateRepository
    {
        public List<CsdtRealtimeStreamStatusDto> Streams { get; } = [];

        public Task<IReadOnlyList<CsdtRealtimeStreamStatusDto>> GetStreamsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CsdtRealtimeStreamStatusDto>>(Streams);

        public Task<IReadOnlyList<CsdtRealtimeHistoryItemDto>> GetHistoryAsync(
            string streamCode,
            int take,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CsdtRealtimeHistoryItemDto>>([]);

        public Task<IReadOnlyList<CsdtRealtimeTombstoneDto>> GetTombstonesAsync(
            string streamCode,
            int take,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CsdtRealtimeTombstoneDto>>([]);
    }

    private sealed class FakeCommandRepository : ICsdtRealtimeCommandRepository
    {
        public List<CsdtRealtimeCommand> Commands { get; } = [];

        public bool HasRetryableReverse { get; set; }

        public CsdtRealtimeActionResultDto Result { get; set; } = new()
        {
            Accepted = true,
            RunId = Guid.NewGuid(),
            Status = CsdtRealtimeActionStatuses.Queued,
            Message = "queued",
        };

        public Task<CsdtRealtimeActionResultDto> EnqueueAsync(
            CsdtRealtimeCommand command,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(Result);
        }

        public Task<bool> HasRetryableReverseAsync(
            string streamCode,
            string? maKhoaHoc,
            string expectedPlanToken,
            CancellationToken cancellationToken = default)
            => Task.FromResult(HasRetryableReverse);
    }

    private sealed class FakeReversePlanRepository : ICsdtReversePlanRepository
    {
        public int Calls { get; private set; }

        public CsdtRealtimeRouteDefinition? LastRoute { get; private set; }

        public string? LastMaKhoaHoc { get; private set; }

        public CsdtReversePlanDto Plan { get; set; } = ExecutablePlan("plan");

        public Task<CsdtReversePlanDto> BuildPlanAsync(
            CsdtRealtimeRouteDefinition route,
            string? maKhoaHoc,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRoute = route;
            LastMaKhoaHoc = maKhoaHoc;
            return Task.FromResult(Plan);
        }
    }
}

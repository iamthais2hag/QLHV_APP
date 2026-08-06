using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using QLHV.Api.Controllers;
using QLHV.Application.Auth;
using QLHV.Application.Sync.Realtime;

namespace QLHV.Tests.Sync;

public sealed class CsdtRealtimeControllerTests
{
    [Fact]
    public void Controller_allows_business_data_roles_to_read_but_requires_admin_policy_for_writes()
    {
        var controllerAuthorization = Assert.Single(
            typeof(CsdtRealtimeController).GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(AuthPolicies.CanViewBusinessData, controllerAuthorization.Policy);

        foreach (var methodName in new[]
                 {
                     nameof(CsdtRealtimeController.SetEnabled),
                     nameof(CsdtRealtimeController.Baseline),
                     nameof(CsdtRealtimeController.Retry),
                     nameof(CsdtRealtimeController.ReverseExecute),
                 })
        {
            var method = Assert.Single(typeof(CsdtRealtimeController)
                .GetMethods()
                .Where(candidate => candidate.Name == methodName));
            var authorization = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>());
            Assert.Equal(AuthPolicies.CanSynchronizeCSDT, authorization.Policy);
        }

        foreach (var methodName in new[]
                 {
                     nameof(CsdtRealtimeController.Streams),
                     nameof(CsdtRealtimeController.History),
                     nameof(CsdtRealtimeController.Tombstones),
                     nameof(CsdtRealtimeController.ReversePlan),
                 })
        {
            var method = Assert.Single(typeof(CsdtRealtimeController)
                .GetMethods()
                .Where(candidate => candidate.Name == methodName));
            Assert.Empty(method.GetCustomAttributes<AuthorizeAttribute>());
        }
    }

    [Fact]
    public async Task Admin_action_passes_authenticated_actor_and_returns_accepted()
    {
        var service = new FakeService();
        var controller = CreateController(service, AppRoles.Admin, "admin");

        var response = await controller.SetEnabled(
            CsdtRealtimeStreamCodes.OtoV2ToV1,
            new CsdtRealtimeEnableRequest
            {
                Enabled = true,
                ExpectedStateToken = "state",
            },
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(response.Result);
        Assert.IsType<CsdtRealtimeActionResultDto>(accepted.Value);
        Assert.NotNull(service.LastUser);
        Assert.Equal("admin", service.LastUser!.Actor);
        Assert.Equal(AppRoles.Admin, service.LastUser.Role);
        Assert.True(service.LastUser.WriteAuthorized);
    }

    [Fact]
    public async Task Viewer_status_is_forwarded_as_read_only_context()
    {
        var service = new FakeService();
        var controller = CreateController(service, AppRoles.Viewer, "viewer");

        var response = await controller.Streams(CancellationToken.None);

        Assert.IsType<OkObjectResult>(response.Result);
        Assert.NotNull(service.LastUser);
        Assert.Equal(AppRoles.Viewer, service.LastUser!.Role);
        Assert.False(service.LastUser.WriteAuthorized);
    }

    [Fact]
    public async Task Stale_reverse_execute_maps_to_http_conflict()
    {
        var service = new FakeService
        {
            ReverseExecuteResult = new CsdtReverseExecuteResultDto
            {
                Accepted = false,
                Status = CsdtRealtimeActionStatuses.Conflict,
                Message = "stale",
            },
        };
        var controller = CreateController(service, AppRoles.Admin, "admin");

        var response = await controller.ReverseExecute(
            new CsdtReverseExecuteRequest
            {
                VehicleType = CsdtRealtimeVehicleTypes.Oto,
                ExpectedPlanToken = "old",
            },
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        Assert.Same(service.ReverseExecuteResult, conflict.Value);
    }

    [Fact]
    public void Routes_match_the_client_contract()
    {
        var controllerRoute = Assert.Single(
            typeof(CsdtRealtimeController).GetCustomAttributes<RouteAttribute>());
        Assert.Equal("api/dong-bo-v2/csdt-realtime", controllerRoute.Template);

        AssertHttpTemplate(nameof(CsdtRealtimeController.Streams), "streams");
        AssertHttpTemplate(
            nameof(CsdtRealtimeController.History),
            "streams/{streamCode}/history");
        AssertHttpTemplate(
            nameof(CsdtRealtimeController.Tombstones),
            "streams/{streamCode}/tombstones");
        AssertHttpTemplate(
            nameof(CsdtRealtimeController.SetEnabled),
            "streams/{streamCode}/enabled");
        AssertHttpTemplate(
            nameof(CsdtRealtimeController.Baseline),
            "streams/{streamCode}/baseline");
        AssertHttpTemplate(
            nameof(CsdtRealtimeController.Retry),
            "streams/{streamCode}/retry");
        AssertHttpTemplate(nameof(CsdtRealtimeController.ReversePlan), "reverse-plan");
        AssertHttpTemplate(nameof(CsdtRealtimeController.ReverseExecute), "reverse-execute");
    }

    private static CsdtRealtimeController CreateController(
        FakeService service,
        string role,
        string username)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role),
            ],
            "TestCookie");
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
        };
        return new CsdtRealtimeController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    private static void AssertHttpTemplate(string methodName, string expected)
    {
        var method = Assert.Single(typeof(CsdtRealtimeController)
            .GetMethods()
            .Where(candidate => candidate.Name == methodName));
        var template = method
            .GetCustomAttributes(inherit: true)
            .OfType<HttpMethodAttribute>()
            .Select(attribute => attribute.Template)
            .Single();
        Assert.Equal(expected, template);
    }

    private sealed class FakeService : ICsdtRealtimeService
    {
        public CsdtRealtimeUserContext? LastUser { get; private set; }

        public CsdtReverseExecuteResultDto ReverseExecuteResult { get; set; } = new()
        {
            Accepted = true,
            Status = CsdtRealtimeActionStatuses.Queued,
            Message = "queued",
        };

        public Task<CsdtRealtimeStreamsResponseDto> GetStreamsAsync(
            CsdtRealtimeUserContext user,
            CancellationToken cancellationToken = default)
        {
            LastUser = user;
            return Task.FromResult(new CsdtRealtimeStreamsResponseDto());
        }

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

        public Task<CsdtRealtimeActionResultDto> SetEnabledAsync(
            string streamCode,
            CsdtRealtimeEnableRequest request,
            CsdtRealtimeUserContext user,
            CancellationToken cancellationToken = default)
        {
            LastUser = user;
            return Task.FromResult(Accepted());
        }

        public Task<CsdtRealtimeActionResultDto> QueueBaselineAsync(
            string streamCode,
            CsdtRealtimeBaselineRequest request,
            CsdtRealtimeUserContext user,
            CancellationToken cancellationToken = default)
        {
            LastUser = user;
            return Task.FromResult(Accepted());
        }

        public Task<CsdtRealtimeActionResultDto> QueueRetryAsync(
            string streamCode,
            CsdtRealtimeRetryRequest request,
            CsdtRealtimeUserContext user,
            CancellationToken cancellationToken = default)
        {
            LastUser = user;
            return Task.FromResult(Accepted());
        }

        public Task<CsdtReversePlanDto> GetReversePlanAsync(
            string vehicleType,
            string? maKhoaHoc,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new CsdtReversePlanDto
            {
                PlanToken = "plan",
                GeneratedAtUtc = DateTimeOffset.UtcNow,
            });

        public Task<CsdtReverseExecuteResultDto> ExecuteReverseAsync(
            CsdtReverseExecuteRequest request,
            CsdtRealtimeUserContext user,
            CancellationToken cancellationToken = default)
        {
            LastUser = user;
            return Task.FromResult(ReverseExecuteResult);
        }

        private static CsdtRealtimeActionResultDto Accepted()
            => new()
            {
                Accepted = true,
                RunId = Guid.NewGuid(),
                Status = CsdtRealtimeActionStatuses.Queued,
                Message = "queued",
            };
    }
}

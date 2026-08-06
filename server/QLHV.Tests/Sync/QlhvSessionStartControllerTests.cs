using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QLHV.Api.Controllers;
using QLHV.Application.Auth;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Tests.Sync;

public sealed class QlhvSessionStartControllerTests
{
    [Fact]
    public async Task Authorized_loopback_launcher_marker_can_start_system_session()
    {
        var service = new FakeAutoSyncService();
        var controller = CreateController(
            service,
            IPAddress.Loopback,
            includeLauncherMarker: true);

        var response = await controller.Start(
            new QlhvSessionStartSyncRequest { ServerStartedByLauncher = true },
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(response.Result);
        var result = Assert.IsType<QlhvAutoSyncQueueResultDto>(accepted.Value);
        Assert.True(result.Accepted);
        Assert.True(service.LastServerStartedByLauncher);
        Assert.Equal(1, service.SessionStartCalls);
        var authorization = Assert.Single(typeof(QlhvSessionStartController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal(AuthPolicies.CanSynchronizeCSDT, authorization.Policy);
        Assert.Empty(typeof(QlhvSessionStartController)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Fact]
    public async Task Loopback_launcher_can_read_need_sync_status_without_browser_cookie()
    {
        var service = new FakeAutoSyncService
        {
            StatusResult = new QlhvSessionStartStatusDto
            {
                NeedSync = true,
                CanStart = true,
                State = "idle",
            },
        };
        var controller = CreateController(
            service,
            IPAddress.Loopback,
            includeLauncherMarker: true);

        var response = await controller.Status(
            serverStartedByLauncher: false,
            runId: null,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var result = Assert.IsType<QlhvSessionStartStatusDto>(ok.Value);
        Assert.True(result.NeedSync);
        Assert.Equal(1, service.StatusCalls);
        Assert.False(service.LastServerStartedByLauncher);
    }

    [Theory]
    [InlineData("192.168.100.10", true)]
    [InlineData("127.0.0.1", false)]
    public async Task Lan_caller_or_request_without_launcher_marker_is_not_exposed(
        string remoteAddress,
        bool includeLauncherMarker)
    {
        var service = new FakeAutoSyncService();
        var controller = CreateController(
            service,
            IPAddress.Parse(remoteAddress),
            includeLauncherMarker);

        var response = await controller.Start(
            new QlhvSessionStartSyncRequest(),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(response.Result);
        Assert.Equal(0, service.SessionStartCalls);
    }

    [Theory]
    [InlineData("192.168.100.10", true)]
    [InlineData("127.0.0.1", false)]
    public async Task Status_is_not_exposed_to_lan_or_unmarked_callers(
        string remoteAddress,
        bool includeLauncherMarker)
    {
        var service = new FakeAutoSyncService();
        var controller = CreateController(
            service,
            IPAddress.Parse(remoteAddress),
            includeLauncherMarker);

        var response = await controller.Status(
            serverStartedByLauncher: false,
            runId: null,
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(response.Result);
        Assert.Equal(0, service.StatusCalls);
    }

    [Fact]
    public async Task Concurrent_local_start_returns_conflict_for_launcher_reconciliation()
    {
        var service = new FakeAutoSyncService
        {
            QueueResult = new QlhvAutoSyncQueueResultDto
            {
                IsConflict = true,
                Status = "CONFLICT",
                Message = "operation active",
            },
        };
        var controller = CreateController(
            service,
            IPAddress.Loopback,
            includeLauncherMarker: true);

        var response = await controller.Start(
            new QlhvSessionStartSyncRequest(),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        Assert.Same(service.QueueResult, conflict.Value);
        Assert.Equal(1, service.SessionStartCalls);
    }

    private static QlhvSessionStartController CreateController(
        FakeAutoSyncService service,
        IPAddress remoteAddress,
        bool includeLauncherMarker)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = remoteAddress;
        if (includeLauncherMarker)
        {
            httpContext.Request.Headers["X-QLHV-Local-Launcher"] = "session-start-v1";
        }

        return new QlhvSessionStartController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private sealed class FakeAutoSyncService : IQlhvAutoSyncService
    {
        public int SessionStartCalls { get; private set; }

        public int StatusCalls { get; private set; }

        public bool LastServerStartedByLauncher { get; private set; }

        public QlhvSessionStartStatusDto StatusResult { get; init; } = new();

        public QlhvAutoSyncQueueResultDto QueueResult { get; init; } = new()
        {
            Accepted = true,
            RunId = Guid.NewGuid(),
            Status = QlhvAutoSyncConstants.Queued,
        };

        public Task<QlhvAutoSyncQueueResultDto> QueueSessionStartAsync(
            bool serverStartedByLauncher,
            CancellationToken cancellationToken = default)
        {
            SessionStartCalls++;
            LastServerStartedByLauncher = serverStartedByLauncher;
            return Task.FromResult(QueueResult);
        }

        public Task<QlhvAutoSyncQueueResultDto> QueueEnsureFreshAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<QlhvAutoSyncQueueResultDto> QueueAsync(
            string triggerType,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<QlhvSessionStartStatusDto> GetSessionStartStatusAsync(
            bool serverStartedByLauncher,
            Guid? runId = null,
            CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            LastServerStartedByLauncher = serverStartedByLauncher;
            return Task.FromResult(StatusResult);
        }

        public Task<QlhvAutoSyncStatusDto> GetStatusAsync(
            Guid? runId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<QlhvSyncFreshnessResult> GetDiagnosticsAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

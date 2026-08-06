using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class QlhvOperationsStateProbeTests
{
    [Fact]
    public void Running_scm_service_with_nonzero_pid_is_a_running_worker_process()
    {
        Assert.Equal(
            "RUNNING",
            QlhvOperationsStateProbe.ClassifyProcessState(
                serviceState: 4,
                processId: 14104));
    }

    [Theory]
    [InlineData(4u, 0u)]
    [InlineData(1u, 14104u)]
    [InlineData(2u, 14104u)]
    [InlineData(3u, 14104u)]
    [InlineData(7u, 14104u)]
    public void Nonrunning_or_pidless_scm_state_is_not_a_running_worker_process(
        uint serviceState,
        uint processId)
    {
        Assert.Equal(
            "NOT_RUNNING",
            QlhvOperationsStateProbe.ClassifyProcessState(
                serviceState,
                processId));
    }
}

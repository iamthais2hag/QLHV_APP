using Dapper;
using System.Runtime.InteropServices;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

/// <summary>One read-only production snapshot for the operations screen.</summary>
public sealed class QlhvOperationsStateProbe : IQlhvOperationsStateProbe
{
    private readonly IConnectionSettingsProvider _connections;
    private readonly SyncOptions _options;

    public QlhvOperationsStateProbe(
        IConnectionSettingsProvider connections,
        IOptions<SyncOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task<QlhvOperationsStateSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new QlhvOperationsStoreUnavailableException(
                "Khong doc duoc trang thai realtime tu QLHV_APP.");
        }

        await using var connection = new SqlConnection(target.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(new CommandDefinition(
            SnapshotSql,
            new { Resource = QlhvSqlAutoSyncGlobalLock.LockResource },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        var header = await result.ReadSingleAsync<HeaderRow>();
        var service = ReadWindowsServiceState();
        var profiles = (await result.ReadAsync<ProfileRow>())
            .Select(row => new QlhvRealtimeProfileStateDto
            {
                ProfileCode = row.SourceProfileCode,
                Enabled = row.Enabled,
                Health = row.LastStatus ?? "UNKNOWN",
                CheckpointVersion = row.LastCheckpointVersion,
                LastCycleCompletedAtUtc = row.LastCycleCompletedAtUtc,
            }).ToArray();
        return new QlhvOperationsStateSnapshot(
            header.EnableProductionRealtime,
            header.EnableProductionWrites,
            service.ServiceState,
            service.ProcessState,
            header.WorkerStatus ?? "UNKNOWN",
            header.WorkerInstanceId,
            header.CurrentProfile,
            header.CycleActive,
            header.LastHeartbeatUtc,
            header.LastErrorCode,
            header.MutexHeld,
            header.RawAutoSyncSlots,
            header.ActiveOperations,
            profiles);
    }

    internal const string SnapshotSql = """
        SELECT f.EnableProductionRealtime, f.EnableProductionWrites,
               w.Status AS WorkerStatus, w.InstanceId AS WorkerInstanceId,
               w.CurrentProfile, w.CycleActive, w.LastHeartbeatUtc,
               w.LastErrorCode,
               CONVERT(bit, CASE WHEN APPLOCK_TEST(N'public', @Resource,
                   N'Exclusive', N'Session') = 0 THEN 1 ELSE 0 END) AS MutexHeld,
               (SELECT COUNT(1) FROM dbo.App_QlhvAutoSyncRun
                WHERE ActiveSlot=1) AS RawAutoSyncSlots,
               (SELECT COUNT(1) FROM dbo.App_QlhvSyncOperationHistory
                WHERE Status IN (N'QUEUED', N'RUNNING')) AS ActiveOperations
        FROM dbo.App_QlhvDirectRealtimeFeatureState f
        CROSS JOIN dbo.App_QlhvDirectRealtimeWorkerState w
        WHERE f.FeatureStateId=1 AND w.WorkerStateId=1;

        SELECT SourceProfileCode, Enabled, LastStatus,
               LastCheckpointVersion, LastCycleCompletedAtUtc
        FROM dbo.App_QlhvDirectRealtimeProfileState
        ORDER BY SequenceOrder;
        """;

    private sealed class HeaderRow
    {
        public bool EnableProductionRealtime { get; init; }
        public bool EnableProductionWrites { get; init; }
        public string? WorkerStatus { get; init; }
        public string? WorkerInstanceId { get; init; }
        public string? CurrentProfile { get; init; }
        public bool CycleActive { get; init; }
        public DateTime? LastHeartbeatUtc { get; init; }
        public string? LastErrorCode { get; init; }
        public bool MutexHeld { get; init; }
        public int RawAutoSyncSlots { get; init; }
        public int ActiveOperations { get; init; }
    }

    private sealed class ProfileRow
    {
        public string SourceProfileCode { get; init; } = string.Empty;
        public bool Enabled { get; init; }
        public string? LastStatus { get; init; }
        public long LastCheckpointVersion { get; init; }
        public DateTime? LastCycleCompletedAtUtc { get; init; }
    }

    private static WindowsServiceState ReadWindowsServiceState()
    {
        if (!OperatingSystem.IsWindows()) return new("NOT_APPLICABLE", "NOT_APPLICABLE");
        var manager = OpenSCManager(null, null, 0x0001);
        if (manager == IntPtr.Zero) return new("UNKNOWN", "UNKNOWN");
        try
        {
            var service = OpenService(manager, "QLHV_APP_RealtimeWorker", 0x0004);
            if (service == IntPtr.Zero) return new("NOT_INSTALLED", "NOT_RUNNING");
            try
            {
                var size = Marshal.SizeOf<ServiceStatusProcess>();
                if (!QueryServiceStatusEx(service, 0, out var status, size, out _))
                {
                    return new("UNKNOWN", "UNKNOWN");
                }
                var serviceState = status.CurrentState switch
                {
                    1 => "STOPPED", 2 => "START_PENDING", 3 => "STOP_PENDING",
                    4 => "RUNNING", 5 => "CONTINUE_PENDING", 6 => "PAUSE_PENDING",
                    7 => "PAUSED", _ => "UNKNOWN",
                };
                var processState = ClassifyProcessState(
                    status.CurrentState,
                    status.ProcessId);
                return new(serviceState, processState);
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    internal static string ClassifyProcessState(
        uint serviceState,
        uint processId) =>
        serviceState == 4 && processId != 0
            ? "RUNNING"
            : "NOT_RUNNING";

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    private sealed record WindowsServiceState(string ServiceState, string ProcessState);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr manager, string serviceName, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(
        IntPtr service, int infoLevel, out ServiceStatusProcess buffer,
        int bufferSize, out int bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}

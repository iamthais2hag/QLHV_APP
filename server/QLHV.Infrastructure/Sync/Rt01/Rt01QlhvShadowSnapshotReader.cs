using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Rt01;

namespace QLHV.Infrastructure.Sync.Rt01;

/// <summary>
/// SELECT-only adapter for RT-01. It reads the fixed live CSDT profile and the
/// matching QLHV_APP partition through the existing import read repository.
/// It never reads BAK and has no reference to a target writer.
/// </summary>
public sealed class Rt01QlhvShadowSnapshotReader : IRt01ShadowSnapshotReader
{
    private readonly QlhvImportReadRepository _reads;
    private readonly TimeProvider _timeProvider;

    public Rt01QlhvShadowSnapshotReader(
        QlhvImportReadRepository reads,
        TimeProvider? timeProvider = null)
    {
        _reads = reads;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Rt01ShadowSnapshots> ReadAsync(
        Rt01ShadowRoute route,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!Rt01ShadowRouteCatalog.Ordered.Contains(route))
        {
            throw new ArgumentException(
                "RT-01 route khong nam trong allowlist OTO/MOTO live.",
                nameof(route));
        }

        var startedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var request = new QlhvImportRequest
        {
            SourceProfileCode = route.SourceProfileCode,
            MaCSDT = route.MaCsdt,
        };
        var liveSource = await _reads.ReadLiveSourceAsync(request, cancellationToken);
        var sourceMaDks = liveSource.HocVienRows
            .Where(row => !string.IsNullOrWhiteSpace(row.MaDK))
            .Select(row => row.MaDK.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var target = await _reads.ReadTargetAsync(
            request,
            sourceMaDks,
            cancellationToken);

        return new Rt01ShadowSnapshots(
            liveSource,
            target,
            startedAtUtc,
            _timeProvider.GetUtcNow().UtcDateTime);
    }
}

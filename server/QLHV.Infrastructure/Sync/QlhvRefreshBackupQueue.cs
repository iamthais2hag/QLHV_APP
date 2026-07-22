using System.Threading.Channels;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

public sealed class QlhvRefreshBackupQueue : IQlhvRefreshBackupQueue
{
    private readonly Channel<QlhvRefreshBackupWorkItem> _channel;

    public QlhvRefreshBackupQueue(IOptions<QlhvOperationsOptions> options)
    {
        _channel = Channel.CreateBounded<QlhvRefreshBackupWorkItem>(new BoundedChannelOptions(
            Math.Clamp(options.Value.QueueCapacity, 2, 100))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    public ValueTask EnqueueAsync(
        QlhvRefreshBackupWorkItem item,
        CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(item, cancellationToken);

    internal IAsyncEnumerable<QlhvRefreshBackupWorkItem> ReadAllAsync(
        CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}

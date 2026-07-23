using System.Threading.Channels;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

public sealed class QlhvAutoSyncQueue : IQlhvAutoSyncQueue
{
    private readonly Channel<QlhvAutoSyncWorkItem> _channel;

    public QlhvAutoSyncQueue(IOptions<QlhvAutoSyncOptions> options)
    {
        _channel = Channel.CreateBounded<QlhvAutoSyncWorkItem>(
            new BoundedChannelOptions(Math.Clamp(options.Value.QueueCapacity, 1, 20))
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
    }

    public ValueTask EnqueueAsync(
        QlhvAutoSyncWorkItem item,
        CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(item, cancellationToken);

    internal IAsyncEnumerable<QlhvAutoSyncWorkItem> ReadAllAsync(
        CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}

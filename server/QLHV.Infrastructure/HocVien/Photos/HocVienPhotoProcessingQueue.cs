using System.Threading.Channels;
using Microsoft.Extensions.Options;
using QLHV.Application.HocVien.Photos;

namespace QLHV.Infrastructure.HocVien.Photos;

public sealed class HocVienPhotoProcessingQueue : IHocVienPhotoProcessingQueue
{
    private readonly Channel<HocVienPhotoProcessingWorkItem> _channel;
    private int _pendingCount;

    public HocVienPhotoProcessingQueue(IOptions<HocVienPhotoProcessingOptions> options)
    {
        var capacity = Math.Clamp(options.Value.QueueCapacity, 1, 10_000);
        _channel = Channel.CreateBounded<HocVienPhotoProcessingWorkItem>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
    }

    public int PendingCount => Math.Max(0, Volatile.Read(ref _pendingCount));

    public bool TryEnqueue(HocVienPhotoProcessingWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_channel.Writer.TryWrite(item))
        {
            return false;
        }

        Interlocked.Increment(ref _pendingCount);
        return true;
    }

    public async ValueTask<HocVienPhotoProcessingWorkItem> DequeueAsync(
        CancellationToken cancellationToken = default)
    {
        var item = await _channel.Reader.ReadAsync(cancellationToken);
        Interlocked.Decrement(ref _pendingCount);
        return item;
    }
}

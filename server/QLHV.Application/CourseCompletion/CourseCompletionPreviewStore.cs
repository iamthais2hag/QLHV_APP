using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace QLHV.Application.CourseCompletion;

public sealed class CourseCompletionPreviewStore
{
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, Envelope> _previews = new(StringComparer.Ordinal);

    public CourseCompletionPreviewStore(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public (string Token, DateTime ExpiresAtUtc) Put(SealedCourseCompletionPreview preview)
    {
        Purge();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = _clock.GetUtcNow().UtcDateTime.Add(CourseCompletionContract.PreviewTtl);
        if (!_previews.TryAdd(token, new Envelope(preview, _clock.GetTimestamp(), CourseCompletionContract.PreviewTtl)))
            throw new InvalidOperationException("Could not create a unique completion preview token.");
        return (token, expires);
    }

    public SealedCourseCompletionPreview Get(string token, string actor, long courseId)
    {
        Purge();
        var normalized = token?.Trim() ?? string.Empty;
        if (!_previews.TryGetValue(normalized, out var envelope) ||
            _clock.GetElapsedTime(envelope.CreatedAt) >= envelope.Ttl)
        {
            _previews.TryRemove(normalized, out _);
            throw new CourseCompletionDomainException(CourseCompletionCodes.Conflict, "Preview không tồn tại hoặc đã hết hạn.", 409);
        }
        if (!string.Equals(envelope.Preview.Actor, actor, StringComparison.OrdinalIgnoreCase) ||
            envelope.Preview.KhoaHocId != courseId)
            throw new CourseCompletionDomainException(CourseCompletionCodes.Conflict, "Preview không thuộc người dùng hoặc khóa học hiện tại.", 409);
        return envelope.Preview;
    }

    private void Purge()
    {
        foreach (var item in _previews.Where(x => _clock.GetElapsedTime(x.Value.CreatedAt) >= x.Value.Ttl))
            _previews.TryRemove(item.Key, out _);
    }

    private sealed record Envelope(SealedCourseCompletionPreview Preview, long CreatedAt, TimeSpan Ttl);
}

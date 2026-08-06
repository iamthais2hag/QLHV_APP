using System.Collections.Concurrent;

namespace QLHV.Application.Assignments;

public sealed class AssignmentPreviewStore
{
    private static readonly TimeSpan CompletedTtl = TimeSpan.FromHours(24);
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, PreviewEnvelope> _previews = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CompletedEnvelope> _completed = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _confirmLocks = new(StringComparer.Ordinal);

    public AssignmentPreviewStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public (string Token, DateTime ExpiresAtUtc) Put(string kind, string actor, object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentNullException.ThrowIfNull(payload);

        PurgeExpired();
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var expires = _timeProvider.GetUtcNow().UtcDateTime.Add(AssignmentRules.PreviewTtl);
        if (!_previews.TryAdd(
                token,
                new PreviewEnvelope(
                    kind,
                    actor,
                    expires,
                    _timeProvider.GetTimestamp(),
                    AssignmentRules.PreviewTtl,
                    payload)))
        {
            throw new InvalidOperationException("Không thể tạo preview token duy nhất.");
        }

        return (token, expires);
    }

    public T Get<T>(string token, string kind, string actor)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            !_previews.TryGetValue(token.Trim(), out var envelope) ||
            IsExpired(envelope.CreatedTimestamp, envelope.TimeToLive))
        {
            _previews.TryRemove(token?.Trim() ?? string.Empty, out _);
            throw new AssignmentDomainException("CONFLICT", "Preview không tồn tại hoặc đã hết hạn.", 409);
        }

        if (!string.Equals(envelope.Kind, kind, StringComparison.Ordinal) ||
            !string.Equals(envelope.Actor, actor, StringComparison.OrdinalIgnoreCase) ||
            envelope.Payload is not T typed)
        {
            throw new AssignmentDomainException("CONFLICT", "Preview không thuộc thao tác hoặc người dùng hiện tại.", 409);
        }

        return typed;
    }

    public bool TryGetCompleted<T>(string actor, string idempotencyKey, out T? result)
    {
        PurgeExpired();
        var key = CompletedKey(actor, idempotencyKey);
        if (_completed.TryGetValue(key, out var value) && value.Result is T typed)
        {
            result = typed;
            return true;
        }

        result = default;
        return false;
    }

    public void Complete(string token, string actor, string idempotencyKey, object result)
    {
        _completed.TryAdd(
            CompletedKey(actor, idempotencyKey),
            new CompletedEnvelope(
                token,
                token,
                token,
                result,
                _timeProvider.GetTimestamp(),
                CompletedTtl));
        _previews.TryRemove(token, out _);
    }

    public async Task<T> RunIdempotentAsync<T>(
        string token,
        string actor,
        string idempotencyKey,
        Func<Task<T>> action)
        => await RunIdempotentAsync(token,actor,idempotencyKey,token,token,action);

    public async Task<T> RunIdempotentAsync<T>(
        string token,
        string actor,
        string idempotencyKey,
        string planIdentity,
        string scopeIdentity,
        Func<Task<T>> action)
    {
        PurgeExpired();
        planIdentity=AssignmentRules.NormalizeRequired(planIdentity,200,"PlanIdentity");
        scopeIdentity=AssignmentRules.NormalizeRequired(scopeIdentity,200,"ScopeIdentity");
        var key=CompletedKey(actor,idempotencyKey);
        var gate=_confirmLocks.GetOrAdd(key,_=>new SemaphoreSlim(1,1));
        await gate.WaitAsync();
        try
        {
            if(_completed.TryGetValue(key,out var completed))
            {
                if(!string.Equals(completed.PlanIdentity,planIdentity,StringComparison.Ordinal) ||
                   !string.Equals(completed.ScopeIdentity,scopeIdentity,StringComparison.Ordinal) ||
                   completed.Result is not T typed)
                    throw new AssignmentDomainException("CONFLICT","IdempotencyKey đã dùng cho preview khác.",409);
                return typed;
            }
            var result=await action();
            _completed[key]=new CompletedEnvelope(
                token,
                planIdentity,
                scopeIdentity,
                result!,
                _timeProvider.GetTimestamp(),
                CompletedTtl);
            _previews.TryRemove(token,out _);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string CompletedKey(string actor, string key)
    {
        var normalizedKey = AssignmentRules.NormalizeRequired(key, 100, "IdempotencyKey");
        return $"{actor.Trim().ToUpperInvariant()}\n{normalizedKey}";
    }

    private void PurgeExpired()
    {
        foreach (var item in _previews.Where(
                     item => IsExpired(
                         item.Value.CreatedTimestamp,
                         item.Value.TimeToLive)))
        {
            _previews.TryRemove(item.Key, out _);
        }
        foreach (var item in _completed.Where(
                     item => IsExpired(
                         item.Value.CreatedTimestamp,
                         item.Value.TimeToLive)))
        {
            if(_completed.TryRemove(item.Key,out _))
                _confirmLocks.TryRemove(item.Key,out _);
        }
    }

    private sealed record PreviewEnvelope(
        string Kind,
        string Actor,
        DateTime ExpiresAtUtc,
        long CreatedTimestamp,
        TimeSpan TimeToLive,
        object Payload);

    private bool IsExpired(long createdTimestamp, TimeSpan timeToLive) =>
        _timeProvider.GetElapsedTime(createdTimestamp) >= timeToLive;

    public bool TryGetCompletedForToken<T>(
        string token,
        string actor,
        string idempotencyKey,
        string scopeIdentity,
        out T? result)
    {
        PurgeExpired();
        var key=CompletedKey(actor,idempotencyKey);
        if(_completed.TryGetValue(key,out var completed) &&
           string.Equals(completed.OriginalToken,token,StringComparison.Ordinal) &&
           string.Equals(completed.ScopeIdentity,scopeIdentity,StringComparison.Ordinal) &&
           completed.Result is T typed)
        {
            result=typed;
            return true;
        }
        result=default;
        return false;
    }

    private sealed record CompletedEnvelope(
        string OriginalToken,
        string PlanIdentity,
        string ScopeIdentity,
        object Result,
        long CreatedTimestamp,
        TimeSpan TimeToLive);
}

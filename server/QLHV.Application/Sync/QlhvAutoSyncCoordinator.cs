using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public sealed class QlhvAutoSyncCoordinator
{
    private readonly IQlhvAutoSyncRunRepository _runs;
    private readonly IQlhvAutoSyncSourceRunner _sourceRunner;

    public QlhvAutoSyncCoordinator(
        IQlhvAutoSyncRunRepository runs,
        IQlhvAutoSyncSourceRunner sourceRunner)
    {
        _runs = runs;
        _sourceRunner = sourceRunner;
    }

    public async Task<QlhvAutoSyncOutcome> ExecuteAsync(
        QlhvAutoSyncRunRecord run,
        CancellationToken cancellationToken = default)
    {
        var sourceOrder = QlhvAutoSyncConstants.NormalizeSourceOrder(run.SourceOrder);
        var succeeded = 0;
        var partialSucceeded = 0;
        var failedSources = 0;
        string? latestError = null;

        foreach (var sourceType in sourceOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previousResult = string.Equals(sourceType, "OTO", StringComparison.Ordinal)
                ? run.Oto
                : run.Moto;
            if (previousResult is not null &&
                (string.Equals(
                     previousResult.Status,
                     QlhvAutoSyncConstants.Succeeded,
                     StringComparison.Ordinal) ||
                 string.Equals(
                     previousResult.Status,
                     QlhvAutoSyncConstants.PartialSuccess,
                     StringComparison.Ordinal) ||
                 string.Equals(
                     previousResult.Status,
                     QlhvAutoSyncConstants.PartialFailed,
                     StringComparison.Ordinal)))
            {
                if (string.Equals(
                        previousResult.Status,
                        QlhvAutoSyncConstants.PartialSuccess,
                        StringComparison.Ordinal))
                {
                    partialSucceeded++;
                }
                else if (string.Equals(
                             previousResult.Status,
                             QlhvAutoSyncConstants.PartialFailed,
                             StringComparison.Ordinal))
                {
                    failedSources++;
                    latestError = previousResult.Message;
                }
                else
                {
                    succeeded++;
                }
                continue;
            }

            await _runs.SetCurrentSourceAsync(run.RunId, sourceType, cancellationToken);

            QlhvAutoSyncSourceResultDto result;
            try
            {
                result = await _sourceRunner.RunAsync(
                    run.RunId,
                    sourceType,
                    run.Actor,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var now = DateTime.UtcNow;
                result = new QlhvAutoSyncSourceResultDto
                {
                    SourceType = sourceType,
                    Status = QlhvAutoSyncConstants.Failed,
                    StartedAtUtc = now,
                    CompletedAtUtc = now,
                    Message = $"Auto Sync source failed: {ex.GetType().Name}.",
                };
            }

            // A source result may represent a transaction that already committed.
            // Persist that terminal fact even if the host stop token was cancelled
            // immediately after the runner returned.
            await _runs.SetSourceResultAsync(
                run.RunId,
                result,
                CancellationToken.None);
            if (string.Equals(
                    result.Status,
                    QlhvAutoSyncConstants.Succeeded,
                    StringComparison.Ordinal))
            {
                succeeded++;
            }
            else if (string.Equals(
                         result.Status,
                         QlhvAutoSyncConstants.PartialSuccess,
                         StringComparison.Ordinal))
            {
                partialSucceeded++;
                latestError = result.Message;
            }
            else if (string.Equals(
                         result.Status,
                         QlhvAutoSyncConstants.PartialFailed,
                         StringComparison.Ordinal))
            {
                failedSources++;
                latestError = result.Message;
            }
            else
            {
                failedSources++;
                latestError = result.Message ?? $"Auto Sync {sourceType} that bai.";
            }
        }

        var completed = succeeded + partialSucceeded;
        var status = (completed, partialSucceeded, failedSources) switch
        {
            (2, 0, 0) => QlhvAutoSyncConstants.Succeeded,
            (2, > 0, 0) => QlhvAutoSyncConstants.PartialSuccess,
            (> 0, _, > 0) => QlhvAutoSyncConstants.PartialFailed,
            _ => QlhvAutoSyncConstants.Failed,
        };
        await _runs.SetCurrentStageAsync(
            run.RunId,
            QlhvAutoSyncConstants.LoadingDataStage,
            CancellationToken.None);
        return new QlhvAutoSyncOutcome(status, latestError, DateTime.UtcNow);
    }
}

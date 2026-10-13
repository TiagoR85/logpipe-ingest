using LogPipe.Contracts.V1;
using LogPipe.Ingest.Pipeline;

namespace LogPipe.Ingest.Forwarding;

/// <summary>
/// Drains the ingress channel in batches, retries transient gRPC failures, and
/// counts entries it must drop after the last attempt (see ADR-0003).
/// </summary>
public sealed class BatchForwarder : BackgroundService
{
    private readonly LogEntryChannel _channel;
    private readonly ILogBatchClient _client;
    private readonly ILogger<BatchForwarder> _logger;
    private readonly int _maxSize;
    private readonly int _maxDelayMs;
    private readonly int _maxAttempts;

    public BatchForwarder(
        LogEntryChannel channel,
        ILogBatchClient client,
        IConfiguration config,
        ILogger<BatchForwarder> logger)
    {
        _channel = channel;
        _client = client;
        _logger = logger;
        _maxSize = config.GetValue("Batch:MaxSize", 100);
        _maxDelayMs = config.GetValue("Batch:MaxDelayMs", 500);
        _maxAttempts = config.GetValue("Batch:MaxAttempts", 3);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<LogEntry>(_maxSize);
        var reader = _channel.Reader;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await reader.WaitToReadAsync(stoppingToken)) break;

                while (batch.Count < _maxSize && reader.TryRead(out var item)) batch.Add(item);
                if (batch.Count == 0) continue;

                if (batch.Count < _maxSize)
                {
                    await Task.Delay(_maxDelayMs, stoppingToken);
                    while (reader.TryRead(out var item) && batch.Count < _maxSize) batch.Add(item);
                }

                await SendWithRetryAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected failure in batch forwarder");
            }
            finally
            {
                batch.Clear();
            }
        }
    }

    private async Task SendWithRetryAsync(List<LogEntry> batch, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                var ack = await _client.SendAsync(batch, ct);
                IngestMetrics.Forwarded.Add(batch.Count);
                if (ack.Rejected > 0)
                {
                    _logger.LogWarning("Search rejected {Rejected} of {Count} entries",
                        ack.Rejected, batch.Count);
                }
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < _maxAttempts)
            {
                var delayMs = 200 * (int)Math.Pow(2, attempt - 1);
                _logger.LogWarning(ex, "Forward attempt {Attempt}/{Max} failed; retrying in {Delay} ms",
                    attempt, _maxAttempts, delayMs);
                await Task.Delay(delayMs, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dropping batch of {Count} entries after {Max} attempts",
                    batch.Count, _maxAttempts);
                IngestMetrics.Dropped.Add(batch.Count);
            }
        }
    }
}

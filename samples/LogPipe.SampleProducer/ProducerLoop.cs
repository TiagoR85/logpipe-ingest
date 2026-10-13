using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LogPipe.SampleProducer;

public sealed class ProducerLoop(
    BatchLogSender sender, LogGenerator generator, ProducerOptions options, ILogger<ProducerLoop> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = generator.NextBatch(options.BatchSize);
                var accepted = await sender.SendAsync(batch, stoppingToken);
                logger.LogInformation("Sent {Count} entries, accepted {Accepted}", batch.Count, accepted);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Producer loop failure");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

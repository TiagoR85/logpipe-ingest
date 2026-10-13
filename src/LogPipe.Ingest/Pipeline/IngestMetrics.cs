using System.Diagnostics.Metrics;

namespace LogPipe.Ingest.Pipeline;

public static class IngestMetrics
{
    public static readonly Meter Meter = new("LogPipe.Ingest");
    public static readonly Counter<long> Accepted =
        Meter.CreateCounter<long>("logpipe_ingest_entries_accepted_total");
    public static readonly Counter<long> Rejected =
        Meter.CreateCounter<long>("logpipe_ingest_entries_rejected_total");
    public static readonly Counter<long> RateLimited =
        Meter.CreateCounter<long>("logpipe_ingest_rate_limited_total");
    public static readonly Counter<long> Forwarded =
        Meter.CreateCounter<long>("logpipe_ingest_entries_forwarded_total");
    public static readonly Counter<long> Dropped =
        Meter.CreateCounter<long>("logpipe_ingest_entries_dropped_total");
}

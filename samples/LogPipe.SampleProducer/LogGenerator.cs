namespace LogPipe.SampleProducer;

public sealed record ProducerLogEntry(
    string Id, string Timestamp, string Level, string Service, string Message,
    string TraceId, string SpanId, Dictionary<string, string> Attributes);

public sealed class LogGenerator(Random random)
{
    private static readonly string[] Services = ["checkout", "auth", "billing", "search-api"];

    private static readonly (string Level, int Weight)[] Levels =
    [
        ("Debug", 30), ("Information", 45), ("Warning", 15), ("Error", 8), ("Critical", 2)
    ];

    private static readonly string[] Messages =
    [
        "order created", "payment authorized", "payment timeout upstream",
        "cache miss for session", "cache warmed", "user login succeeded",
        "user login failed: bad password", "retrying inventory reservation",
        "inventory reserved", "invoice pdf queued", "pdf rendered",
        "rate limit nearing threshold",
        "upstream latency 850ms", "connection pool exhausted, queued",
        "queue lag 12 messages", "schema migration check ok",
        "webhook delivered 200", "webhook delivery failed 503",
        "nightly reindex started", "disk usage at 71%"
    ];

    public LogGenerator() : this(Random.Shared) { }

    public IReadOnlyList<ProducerLogEntry> NextBatch(int size)
    {
        var batch = new List<ProducerLogEntry>(size);
        while (batch.Count < size)
        {
            // ~30% of the time, start a burst that shares one trace id (trace-page demo).
            var traceId = random.NextDouble() < 0.3 ? Guid.NewGuid().ToString("N") : "";
            var burst = traceId.Length > 0 ? random.Next(3, 6) : 1;
            for (var i = 0; i < burst && batch.Count < size; i++)
            {
                batch.Add(NewEntry(traceId));
            }
        }
        return batch;
    }

    private ProducerLogEntry NewEntry(string traceId) => new(
        Id: Guid.NewGuid().ToString(),
        Timestamp: DateTimeOffset.UtcNow.ToString("O"),
        Level: PickLevel(),
        Service: Services[random.Next(Services.Length)],
        Message: Messages[random.Next(Messages.Length)],
        TraceId: traceId,
        SpanId: traceId.Length > 0 ? Guid.NewGuid().ToString("N")[..16] : "",
        Attributes: new Dictionary<string, string> { ["env"] = "demo", ["region"] = "eu" });

    private string PickLevel()
    {
        var total = Levels.Sum(l => l.Weight);
        var roll = random.Next(total);
        foreach (var (level, weight) in Levels)
        {
            if (roll < weight) return level;
            roll -= weight;
        }
        return "Information";
    }
}

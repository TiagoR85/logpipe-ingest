using LogPipe.SampleProducer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;

var settings = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var options = ProducerOptions.FromConfiguration(settings);

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddConfiguration(settings);
builder.Services.AddSingleton(options);
// TokenProvider and BatchLogSender both take a bare HttpClient. Registered here
// rather than as a typed client because they need the same instance shape the
// unit tests use (a plain client with no BaseAddress — the requests carry
// absolute URLs built from ProducerOptions). The timeout is raised so a slow
// ingest does not kill batches mid-send.
builder.Services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
builder.Services.AddSingleton<TokenProvider>();
builder.Services.AddSingleton<BatchLogSender>();
builder.Services.AddSingleton(new LogGenerator());
builder.Services.AddHostedService<ProducerLoop>();

LogManager.Setup().LoadConfigurationFromFile(Path.Combine(AppContext.BaseDirectory, "nlog.config"));

await builder.Build().RunAsync();

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
builder.Services.AddSingleton<TokenProvider>();
builder.Services.AddSingleton<BatchLogSender>();
builder.Services.AddSingleton(new LogGenerator());
builder.Services.AddHostedService<ProducerLoop>();

LogManager.Setup().LoadConfigurationFromFile(Path.Combine(AppContext.BaseDirectory, "nlog.config"));

await builder.Build().RunAsync();

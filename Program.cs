using ServiceWorker;
using NLog;
using NLog.Web;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<AuctionWorker>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
    })
    .UseNLog()
    .Build();

await host.RunAsync();

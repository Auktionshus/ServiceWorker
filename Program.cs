using ServiceWorker;
using NLog;
using NLog.Web;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<AuctionWorker>();
        services.AddHostedService<BidWorker>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
    })
    .UseNLog()
    .Build();

await host.RunAsync();

using System.IO;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using MongoDB.Driver;

namespace ServiceWorker;

public class AuctionWorker : BackgroundService
{
    private readonly ILogger<AuctionWorker> _logger;
    private readonly string _hostName;

    private readonly string _mongoDbConnectionString;

    public AuctionWorker(ILogger<AuctionWorker> logger, Environment secrets, IConfiguration config)
    {
        try
        {
            _hostName = config["HostnameRabbit"];
            _mongoDbConnectionString = secrets.dictionary["ConnectionString"];

            _logger = logger;
            _logger.LogInformation($"HostName: {_hostName}");
            _logger.LogInformation($"MongoDbConnectionString: {_mongoDbConnectionString}");
        }
        catch (Exception e)
        {
            _logger.LogError($"Error getting environment variables{e.Message}");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Create a new instance of the ConnectionFactory
        var factory = new ConnectionFactory { HostName = _hostName };

        // Create a new connection to rabbitMQ using the ConnectionFactory
        using var connection = factory.CreateConnection();
        // Create a new channel using the connection
        using var channel = connection.CreateModel();

        // Declare a topic exchange named "topic_fleet"
        channel.ExchangeDeclare(exchange: "topic_fleet", type: ExchangeType.Topic);

        var queueName = channel.QueueDeclare().QueueName;

        channel.QueueBind(queue: queueName, exchange: "topic_fleet", routingKey: "auctions.create");

        var consumer = new EventingBasicConsumer(channel);

        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var dbClient = new MongoClient(_mongoDbConnectionString);
            var auctionCollection = dbClient
                .GetDatabase("auction")
                .GetCollection<Auction>("auctions");
            var itemCollection = dbClient.GetDatabase("Items").GetCollection<Item>("Item");

            _logger.LogInformation($" [x] Received {message}");

            var auctionDTO = JsonSerializer.Deserialize<AuctionDTO>(message);

            Item item = null;
            try
            {
                item = itemCollection.Find(i => i.Id == auctionDTO.Item).FirstOrDefault();
                _logger.LogInformation($" [x] Received item with id: {item.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while querying the item collection: {ex}");
            }

            Auction auction = new Auction
            {
                Id = Guid.NewGuid(),
                Item = item,
                StartTime = auctionDTO.StartTime,
                EndTime = auctionDTO.EndTime,
                StartingPrice = auctionDTO.StartingPrice,
                CurrentPrice = auctionDTO.StartingPrice,
                Bids = new List<Bid>()
            };

            if (item != null)
            {
                try
                {
                    auctionCollection.InsertOneAsync(auction);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        $"An error occurred while performing database operations: {ex}"
                    );
                }
            }
        };

        channel.BasicConsume(queue: queueName, autoAck: true, consumer: consumer);

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            await Task.Delay(1000, stoppingToken);
        }
    }
}

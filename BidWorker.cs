using System.IO;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using MongoDB.Driver;

namespace ServiceWorker;

public class BidWorker : BackgroundService
{
    private readonly ILogger<BidWorker> _logger;
    private readonly string _hostName;

    private readonly string _mongoDbConnectionString;

    public BidWorker(ILogger<BidWorker> logger, IConfiguration config)
    {
        _logger = logger;
        _mongoDbConnectionString = config["MongoDbConnectionString"];
        _hostName = config["HostnameRabbit"];
        _logger.LogInformation($"Connection: {_hostName}");
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

        channel.QueueBind(queue: queueName, exchange: "topic_fleet", routingKey: "bids.create");

        var consumer = new EventingBasicConsumer(channel);

        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var dbClient = new MongoClient(_mongoDbConnectionString);
            var auctionCollection = dbClient
                .GetDatabase("auction")
                .GetCollection<Auction>("auctions");
            var userCollection = dbClient.GetDatabase("User").GetCollection<User>("Users");
            var bidCollection = dbClient.GetDatabase("Bid").GetCollection<Bid>("Bids");
            _logger.LogInformation($" [x] Received {message}");

            BidDTO? bidDTO = JsonSerializer.Deserialize<BidDTO>(message);
            _logger.LogInformation(
                $" [x] serialized message auction: {bidDTO.Auction}, bidder: {bidDTO.Bidder}, amount: {bidDTO.Amount}"
            );

            Auction auction = auctionCollection.Find(a => a.Id == bidDTO.Auction).FirstOrDefault();
            _logger.LogInformation($" [x] Received auction with id: {auction.Id}");

            User user = null;
            try
            {
                user = userCollection.Find(u => u.Id == bidDTO.Bidder).FirstOrDefault();
                _logger.LogInformation($" [x] Received user with id: {user.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while querying the user collection: {ex}");
            }
            _logger.LogInformation(
                $" [x] Bid amount: {bidDTO.Amount}, auction current price: {auction.CurrentPrice}"
            );
            if (auction != null && bidDTO.Amount > auction.CurrentPrice)
            {
                if (auction.BidHistory == null)
                {
                    auction.BidHistory = new List<Bid>();
                }
                Bid bid = new Bid
                {
                    Amount = bidDTO.Amount,
                    Bidder = user,
                    Id = Guid.NewGuid()
                };
                _logger.LogInformation(
                    $" [x] Received bid with id: {bid.Id}, amount: {bid.Amount}, bidder: {bid.Bidder}"
                );

                auction.BidHistory.Add(bid);
                auction.CurrentPrice = bid.Amount;

                var update = Builders<Auction>.Update
                    .Set(a => a.CurrentPrice, bid.Amount)
                    .Push(a => a.BidHistory, bid);

                auctionCollection.UpdateOne(a => a.Id == bidDTO.Auction, update);

                bidCollection.InsertOne(bid);
            }
            else
            {
                _logger.LogInformation($"error while adding auction");
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

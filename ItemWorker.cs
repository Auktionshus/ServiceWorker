using System.IO;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using MongoDB.Driver;

namespace ServiceWorker;

public class ItemWorker : BackgroundService
{
    private readonly ILogger<ItemWorker> _logger;
    private readonly string _hostName;

    private readonly string _mongoDbConnectionString;

    public ItemWorker(ILogger<ItemWorker> logger, IConfiguration config)
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

        channel.QueueBind(queue: queueName, exchange: "topic_fleet", routingKey: "items.create");

        var consumer = new EventingBasicConsumer(channel);

        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var dbClient = new MongoClient(_mongoDbConnectionString);
            var itemCollection = dbClient.GetDatabase("Items").GetCollection<Item>("Item");
            var userCollection = dbClient.GetDatabase("User").GetCollection<User>("Users");

            _logger.LogInformation($" [x] Received {message}");

            var itemDTO = JsonSerializer.Deserialize<ItemDTO>(message);

            User user = null;
            try
            {
                user = userCollection.Find(u => u.Id == itemDTO.Seller).FirstOrDefault();
                _logger.LogInformation($" [x] Received user with id: {user.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while querying the user collection: {ex}");
            }

            Category chairs = new Category
            {
                CategoryCode = "CH",
                CategoryName = "Chairs",
                CategoryDescription = "Something to sit on"
            };

            Category lamps = new Category
            {
                CategoryCode = "LA",
                CategoryName = "Lamps",
                CategoryDescription = "A Collection of lamps to brighten your life"
            };

            Category coins = new Category
            {
                CategoryCode = "CO",
                CategoryName = "Coins",
                CategoryDescription = "Moneyzz"
            };

            Category rings = new Category
            {
                CategoryCode = "RI",
                CategoryName = "Rings",
                CategoryDescription = "A collection of different types of rings"
            };

            Item item = new Item
            {
                Id = Guid.NewGuid(),
                Seller = user,
                Title = itemDTO.Title,
                Brand = itemDTO.Brand,
                Description = itemDTO.Description,
                Location = itemDTO.Location
            };

            if (itemDTO.CategoryCode == "CH")
            {
                item.Category = chairs;
            }
            else if (itemDTO.CategoryCode == "LA")
            {
                item.Category = lamps;
            }
            else if (itemDTO.CategoryCode == "CO")
            {
                item.Category = coins;
            }
            else if (itemDTO.CategoryCode == "RI")
            {
                item.Category = rings;
            }

            if (user != null)
            {
                try
                {
                    itemCollection.InsertOneAsync(item);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"An error occurred while querying the itemcollection: {ex}");
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

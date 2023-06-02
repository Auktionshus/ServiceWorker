using ServiceWorker;
using NLog;
using NLog.Web;
using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Token;
using VaultSharp.V1.Commons;

try
{
    var EndPoint = "https://vault_dev:8201/";
    var httpClientHandler = new HttpClientHandler();
    httpClientHandler.ServerCertificateCustomValidationCallback = (
        message,
        cert,
        chain,
        sslPolicyErrors
    ) =>
    {
        return true;
    };

    // Initialize one of the several auth methods.
    IAuthMethodInfo authMethod = new TokenAuthMethodInfo("00000000-0000-0000-0000-000000000000");

    // Initialize vault settings.
    var vaultClientSettings = new VaultClientSettings(EndPoint, authMethod)
    {
        Namespace = "",
        MyHttpClientProviderFunc = handler =>
            new HttpClient(httpClientHandler) { BaseAddress = new Uri(EndPoint) }
    };

    // Initialize vault client
    IVaultClient vaultClient = new VaultClient(vaultClientSettings);
    logger.Info($"vault client created: {vaultClient}");
    // Uses vault client to read key-value secrets.
    Secret<SecretData> MongoSecrets = await vaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(
        path: "mongoSecrets",
        mountPoint: "secret"
    );

    string? connectionString = MongoSecrets.Data.Data["ConnectionString"].ToString();
    logger.Info($"Connection String: {connectionString}");
    // Creates and EnviromentVariable object with a dictionary to contain the secrets
    EnvVariables vaultSecrets = new EnvVariables
    {
        dictionary = new Dictionary<string, string>
        {
            { "Secret", secret },
            { "Issuer", issuer },
            { "ConnectionURI", connectionURI }
        }
    };

    IHost host = Host.CreateDefaultBuilder(args)
        .ConfigureServices(services =>
        {
            services.AddHostedService<Worker>();
            // Adds the EnviromentVariable object to the project as a singleton.
            // It can now be accessed wihtin the entire projekt
            services.AddSingleton<EnvVariables>(vaultSecrets);
        })
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
        })
        .UseNLog()
        .Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    logger.Error(ex, "Stopped program because of exception");
}
finally
{
    NLog.LogManager.Shutdown();
}

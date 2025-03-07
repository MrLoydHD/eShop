using eShop.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

// Add observability infrastructure
var (prometheus, jaeger, grafana) = builder.AddObservabilityInfrastructure();

var redis = builder.AddRedis("redis");
var rabbitMq = builder.AddRabbitMQ("eventbus")
    .WithLifetime(ContainerLifetime.Persistent);
var postgres = builder.AddPostgres("postgres")
    .WithImage("ankane/pgvector")
    .WithImageTag("latest")
    .WithLifetime(ContainerLifetime.Persistent);

var catalogDb = postgres.AddDatabase("catalogdb");
var identityDb = postgres.AddDatabase("identitydb");
var orderDb = postgres.AddDatabase("orderingdb");
var webhooksDb = postgres.AddDatabase("webhooksdb");

var launchProfileName = ShouldUseHttpForEndpoints() ? "http" : "https";

// Define Jaeger connection for all services
var jaegerHostName = "localhost";
var jaegerOtlpEndpoint = $"http://{jaegerHostName}:4319";

// Services
var identityApi = builder.AddProject<Projects.Identity_API>("identity-api", launchProfileName)
    .WithExternalHttpEndpoints()
    .WithReference(identityDb)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", jaegerOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
    .WithEnvironment("OTEL_TRACES_SAMPLER", "always_on")
    .WithHttpEndpoint(port: 9701, name: "metrics") // Unique port for metrics
    .WithObservability("identity-api", jaeger);

var identityEndpoint = identityApi.GetEndpoint(launchProfileName);

var basketApi = builder.AddProject<Projects.Basket_API>("basket-api")
    .WithReference(redis)
    .WithReference(rabbitMq).WaitFor(rabbitMq)
    .WithEnvironment("Identity__Url", identityEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", jaegerOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
    .WithEnvironment("OTEL_TRACES_SAMPLER", "always_on")
    .WithHttpEndpoint(port: 9702, name: "metrics")
    .WithObservability("basket-api", jaeger);
redis.WithParentRelationship(basketApi);

var catalogApi = builder.AddProject<Projects.Catalog_API>("catalog-api")
    .WithReference(rabbitMq).WaitFor(rabbitMq)
    .WithReference(catalogDb)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", jaegerOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
    .WithEnvironment("OTEL_TRACES_SAMPLER", "always_on")
    .WithHttpEndpoint(port: 9703, name: "metrics")
    .WithObservability("catalog-api", jaeger);

var orderingApi = builder.AddProject<Projects.Ordering_API>("ordering-api")
    .WithReference(rabbitMq).WaitFor(rabbitMq)
    .WithReference(orderDb).WaitFor(orderDb)
    .WithHttpHealthCheck("/health")
    .WithEnvironment("Identity__Url", identityEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", jaegerOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
    .WithEnvironment("OTEL_TRACES_SAMPLER", "always_on")
    .WithHttpEndpoint(port: 9704, name: "metrics")
    .WithObservability("ordering-api", jaeger);

builder.AddProject<Projects.OrderProcessor>("order-processor")
    .WithReference(rabbitMq).WaitFor(rabbitMq)
    .WithReference(orderDb)
    .WaitFor(orderingApi) // wait for the orderingApi to be ready because that contains the EF migrations
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", jaegerOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
    .WithEnvironment("OTEL_TRACES_SAMPLER", "always_on")
    .WithObservability("order-processor", jaeger);

builder.AddProject<Projects.PaymentProcessor>("payment-processor")
    .WithReference(rabbitMq).WaitFor(rabbitMq)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", jaegerOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
    .WithEnvironment("OTEL_TRACES_SAMPLER", "always_on")
    .WithHttpEndpoint(port: 9705, name: "metrics")
    .WithObservability("payment-processor", jaeger);

var webHooksApi = builder.AddProject<Projects.Webhooks_API>("webhooks-api")
    .WithReference(rabbitMq).WaitFor(rabbitMq)
    .WithReference(webhooksDb)
    .WithEnvironment("Identity__Url", identityEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", jaegerOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
    .WithEnvironment("OTEL_TRACES_SAMPLER", "always_on")
    .WithObservability("webhooks-api", jaeger);

// Reverse proxies
builder.AddProject<Projects.Mobile_Bff_Shopping>("mobile-bff")
    .WithReference(catalogApi)
    .WithReference(orderingApi)
    .WithReference(basketApi)
    .WithReference(identityApi)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", jaegerOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
    .WithEnvironment("OTEL_TRACES_SAMPLER", "always_on")
    .WithObservability("mobile-bff", jaeger);

// Apps
var webhooksClient = builder.AddProject<Projects.WebhookClient>("webhooksclient", launchProfileName)
    .WithReference(webHooksApi)
    .WithEnvironment("IdentityUrl", identityEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", jaegerOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
    .WithEnvironment("OTEL_TRACES_SAMPLER", "always_on")
    .WithObservability("webhooks-client", jaeger);

var webApp = builder.AddProject<Projects.WebApp>("webapp", launchProfileName)
    .WithExternalHttpEndpoints()
    .WithReference(basketApi)
    .WithReference(catalogApi)
    .WithReference(orderingApi)
    .WithReference(rabbitMq).WaitFor(rabbitMq)
    .WithEnvironment("IdentityUrl", identityEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", jaegerOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
    .WithEnvironment("OTEL_TRACES_SAMPLER", "always_on")
    .WithObservability("webapp", jaeger);

// set to true if you want to use OpenAI
bool useOpenAI = false;
if (useOpenAI)
{
    builder.AddOpenAI(catalogApi, webApp);
}

bool useOllama = false;
if (useOllama)
{
    builder.AddOllama(catalogApi, webApp);
}

// Wire up the callback urls (self referencing)
webApp.WithEnvironment("CallBackUrl", webApp.GetEndpoint(launchProfileName));
webhooksClient.WithEnvironment("CallBackUrl", webhooksClient.GetEndpoint(launchProfileName));

// Identity has a reference to all of the apps for callback urls, this is a cyclic reference
identityApi.WithEnvironment("BasketApiClient", basketApi.GetEndpoint("http"))
           .WithEnvironment("OrderingApiClient", orderingApi.GetEndpoint("http"))
           .WithEnvironment("WebhooksApiClient", webHooksApi.GetEndpoint("http"))
           .WithEnvironment("WebhooksWebClient", webhooksClient.GetEndpoint(launchProfileName))
           .WithEnvironment("WebAppClient", webApp.GetEndpoint(launchProfileName));

builder.Build().Run();

// For test use only.
// Looks for an environment variable that forces the use of HTTP for all the endpoints. We
// are doing this for ease of running the Playwright tests in CI.
static bool ShouldUseHttpForEndpoints()
{
    const string EnvVarName = "ESHOP_USE_HTTP_ENDPOINTS";
    var envValue = Environment.GetEnvironmentVariable(EnvVarName);

    // Attempt to parse the environment variable value; return true if it's exactly "1".
    return int.TryParse(envValue, out int result) && result == 1;
}

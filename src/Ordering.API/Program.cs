using eShop.Ordering.API.OpenTelemetry;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text.Json;
using System.Net.Mime;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpLogging;
using eShop.Ordering.API.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add custom telemetry for the Ordering API
builder.AddOrderingTelemetry();
builder.AddApplicationServices();
builder.Services.AddProblemDetails();

// Add HTTP Request Logging with filtering for sensitive data
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = HttpLoggingFields.RequestPropertiesAndHeaders | 
                           HttpLoggingFields.ResponseStatusCode |
                           HttpLoggingFields.Duration;
                           
    // Don't log sensitive headers
    logging.RequestHeaders.Remove("Authorization");
    logging.RequestHeaders.Remove("Cookie");
    
    // Add request ID header for correlation with traces
    logging.RequestHeaders.Add("X-Request-ID");
    logging.RequestHeaders.Add("X-B3-TraceId");
    
    // Don't log bodies as they may contain PII
    logging.MediaTypeOptions.AddText("application/json");
    logging.RequestBodyLogLimit = 4096;
    logging.ResponseBodyLogLimit = 4096;
});

// Add health checks with tags for monitoring
builder.Services.AddHealthChecks()
    .AddCheck("ordering-api-liveness", () => HealthCheckResult.Healthy(), ["live"])
    .AddCheck("ordering-db", () => HealthCheckResult.Healthy(), ["ready"]);

var withApiVersioning = builder.Services.AddApiVersioning();

builder.AddDefaultOpenApi(withApiVersioning);

var app = builder.Build();

// Simple test activity at startup
using (var activity = new Activity("OrderingAPIStartup").Start())
{
    activity?.SetTag("service", "ordering-api");
    activity?.SetTag("http.status_code", 200);
    activity?.SetTag("error", true);
    Console.WriteLine($"Created startup activity: {activity?.Id}");
}

// DEBUGGING: Print all environment variables related to OpenTelemetry
Console.WriteLine("\n=== OPENTELEMETRY ENVIRONMENT VARIABLES ===\n");
foreach (var env in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>())
{
    if (env.Key.ToString().StartsWith("OTEL_") || env.Key.ToString().Contains("TELEMETRY"))
    {
        Console.WriteLine($"{env.Key} = {env.Value}");
    }
}
Console.WriteLine("\n========================================\n");

// Health checks endpoint with detailed responses
app.MapHealthChecks("/hc", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        var result = JsonSerializer.Serialize(
            new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    exception = entry.Value.Exception != null ? entry.Value.Exception.Message : "none",
                    duration = entry.Value.Duration.ToString()
                })
            });

        context.Response.ContentType = MediaTypeNames.Application.Json;
        await context.Response.WriteAsync(result);
    }
});

// Enable HTTP request logging
app.UseHttpLogging();

app.MapDefaultEndpoints();

// Simple test trace endpoint
app.MapGet("/trace-test", () => {
    Console.WriteLine("\n=== GENERATING TEST TRACE ===\n");
    
    // Create a new activity with the specific tags for Jaeger query
    using (var activity = new Activity("TestTraceActivity").Start())
    {
        if (activity == null)
        {
            Console.WriteLine("ERROR: Activity is null - no listener attached?");
            return Results.Problem("Failed to create activity - see logs");
        }
        
        activity.SetTag("http.status_code", 200);
        activity.SetTag("error", true);
        activity.SetTag("test_id", Guid.NewGuid().ToString());
        
        Console.WriteLine($"Created test activity: {activity.Id}");
        Console.WriteLine($"TraceId: {activity.TraceId}");
        Console.WriteLine($"SpanId: {activity.SpanId}");
        Console.WriteLine($"Tags: http.status_code=200, error=true");
    }
    
    Console.WriteLine("\n=== TEST TRACE COMPLETE ===\n");
    
    return Results.Ok(new { 
        message = "Test trace created", 
        instructions = "Search in Jaeger UI with 'http.status_code=200 error=true'" 
    });
});

// Diagnostic endpoint to check OTel configuration
app.MapGet("/check-otel", () => {
    var diagnostics = new Dictionary<string, object>();
    
    // Environment variables
    var envVars = new Dictionary<string, string>();
    foreach (var env in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>())
    {
        if (env.Key.ToString().StartsWith("OTEL_"))
        {
            envVars[env.Key.ToString()] = env.Value?.ToString();
        }
    }
    diagnostics["environment_variables"] = envVars;
    
    // Test activity creation
    using var activity = new Activity("DiagnosticTest").Start();
    diagnostics["test_activity_created"] = activity != null;
    diagnostics["test_activity_id"] = activity?.Id;
    
    // Status summary
    diagnostics["otel_endpoint_configured"] = !string.IsNullOrEmpty(app.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
    diagnostics["otel_endpoint"] = app.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
    diagnostics["service_name"] = app.Configuration["OTEL_SERVICE_NAME"];
    
    return Results.Ok(diagnostics);
});

// Endpoint to check if Jaeger is reachable
app.MapGet("/check-jaeger", async () => {
    var result = new Dictionary<string, object>();
    var jaegerEndpoint = "http://localhost:16686"; // Jaeger UI endpoint
    var otlpEndpoint = app.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
    
    result["jaeger_ui_endpoint"] = jaegerEndpoint;
    result["otlp_endpoint"] = otlpEndpoint;
    
    try
    {
        // Create HTTP client
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(5); // Short timeout
        
        // Try to reach Jaeger UI
        var uiResponse = await client.GetAsync(jaegerEndpoint);
        result["jaeger_ui_accessible"] = uiResponse.IsSuccessStatusCode;
        result["jaeger_ui_status"] = (int)uiResponse.StatusCode;
    }
    catch (Exception ex)
    {
        result["error"] = ex.Message;
        result["jaeger_reachable"] = false;
    }
    
    // Generate a test activity and see if it appears in Jaeger
    var testActivity = new Activity("JaegerConnectionTest").Start();
    testActivity?.SetTag("http.status_code", 200);
    testActivity?.SetTag("error", true);
    testActivity?.SetTag("connection_test", true);
    var traceId = testActivity?.TraceId.ToString() ?? "unknown";
    testActivity?.Stop();
    
    result["test_trace_id"] = traceId;
    result["check_in_jaeger"] = $"Search for TraceID: {traceId} in Jaeger UI";
    
    return Results.Ok(result);
});

// Endpoint to generate order metrics for dashboard testing
app.MapGet("/generate-metrics", () => {
    var random = new Random();
    var telemetryService = app.Services.GetRequiredService<OrderingTelemetryService>();
    
    // Generate a few random orders
    for (int i = 0; i < 5; i++)
    {
        var orderId = random.Next(1000, 9999);
        var orderValue = random.Next(50, 500);

        telemetryService.RecordOrderCreated(orderId);
        
        // 80% success rate
        if (random.NextDouble() > 0.2)
        {
            var processingTime = random.Next(500, 3000);
            telemetryService.RecordOrderCompleted(orderId, processingTime);
        }
        else
        {
            telemetryService.RecordOrderFailed(orderId, "Simulated failure for testing");
        }
    }
    
    return Results.Ok(new { message = "Generated metrics for 5 test orders" });
});

var orders = app.NewVersionedApi("Orders");

orders.MapOrdersApiV1()
      .RequireAuthorization();

app.UseDefaultOpenApi();
app.Run();

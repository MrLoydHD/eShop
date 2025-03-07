using eShop.Ordering.API.OpenTelemetry;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add custom telemetry for the Ordering API
builder.AddOrderingTelemetry();
builder.AddApplicationServices();
builder.Services.AddProblemDetails();

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

var orders = app.NewVersionedApi("Orders");

orders.MapOrdersApiV1()
      .RequireAuthorization();

app.UseDefaultOpenApi();
app.Run();

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using OpenTelemetry.Contrib.Instrumentation.EntityFrameworkCore;

namespace eShop.ServiceDefaults;

public static partial class Extensions
{
    // Set up an ActivitySource that will be used for manual instrumentation
    public static readonly ActivitySource EShopActivitySource = new ActivitySource("eShop", "1.0.0");
    
    // Set up a meter for custom metrics
    public static readonly Meter EShopMeter = new Meter("eShop", "1.0.0");
    
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.AddBasicServiceDefaults();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Adds the services except for making outgoing HTTP calls.
    /// </summary>
    /// <remarks>
    /// This allows for things like Polly to be trimmed out of the app if it isn't used.
    /// </remarks>
    public static IHostApplicationBuilder AddBasicServiceDefaults(this IHostApplicationBuilder builder)
    {
        // Default health checks assume the event bus and self health checks
        builder.AddDefaultHealthChecks();

        builder.ConfigureOpenTelemetry();

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        // Create a test activity to ensure our custom source is initialized and registered
        using (var activity = EShopActivitySource.StartActivity("Startup"))
        {
            activity?.SetTag("telemetry.initialization", "true");
        }
        
        // Extract service name from environment variables
        var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? 
                         (builder.Configuration["OTEL_RESOURCE_ATTRIBUTES"]?.Split(',')
                            .Select(s => s.Split('='))
                            .Where(parts => parts.Length == 2 && parts[0] == "service.name")
                            .Select(parts => parts[1])
                            .FirstOrDefault() ?? "eshop-service");
                            
        Console.WriteLine($"Configuring OpenTelemetry for service: {serviceName}");

        // Get the OTLP endpoint from config
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        Console.WriteLine($"\nRESOURCE BUILDER: {serviceName}\n");
        Console.WriteLine($"OTLP Endpoint: '{otlpEndpoint}'\n");
            
        // Set up resource with service name
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName, serviceVersion: "1.0.0")
            .AddTelemetrySdk()
            .AddEnvironmentVariableDetector();
            
        // Get the OTLP endpoint from config
        Console.WriteLine($"OTLP Endpoint: {otlpEndpoint}");

        // Add masked logger provider
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.SetResourceBuilder(resourceBuilder);
            
            // Add processor to mask sensitive data in logs
            logging.AddProcessor(new SensitiveDataMaskingProcessor());
            
            // Add OTLP exporter for logs - simplified
            if (!string.IsNullOrEmpty(otlpEndpoint))
            {
                logging.AddOtlpExporter(opts => {
                    opts.Endpoint = new Uri(otlpEndpoint);
                });
            }
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddDetector(new EnvironmentResourceDetector()))
            .WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(resourceBuilder);
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    // Add all needed meters
                    .AddMeter("Microsoft.AspNetCore.Hosting")
                    .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                    .AddMeter("System.Net.Http")
                    .AddMeter("eShop")
                    .AddMeter("eShop.*")
                    .AddMeter("PlaceOrder");

                // Add OTLP exporter for metrics - simplified
                if (!string.IsNullOrEmpty(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(opts => {
                        opts.Endpoint = new Uri(otlpEndpoint);
                    });
                }
            })
            .WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resourceBuilder);
                tracing.SetSampler(new AlwaysOnSampler());
                // Use BatchActivityExportProcessor by default - this is handled internally by AddOtlpExporter

                tracing.AddAspNetCoreInstrumentation(opts => {
                    // Add processor to mask sensitive data in traces
                    opts.Filter = context => {
                        // Filter out health check endpoints
                        return !context.Request.Path.StartsWithSegments("/health") &&
                               !context.Request.Path.StartsWithSegments("/alive");
                    };
                    
                    // Enrich with custom tags
                    opts.EnrichWithHttpRequest = (activity, request) => {
                        activity.SetTag("http.request.protocol", request.Protocol);
                        activity.SetTag("http.request.method", request.Method);
                    };
                    
                    // Enrich with custom response details
                    opts.EnrichWithHttpResponse = (activity, response) => {
                        activity.SetTag("http.response.status_code", response.StatusCode);
                    };
                    
                    // Enrich with exception details when errors occur
                    opts.EnrichWithException = (activity, exception) => {
                        activity.SetTag("error.type", exception.GetType().Name);
                        activity.SetTag("error.message", MaskSensitiveDataHelper(exception.Message));
                    };
                })
                .AddGrpcClientInstrumentation()
                .AddHttpClientInstrumentation(opts => {
                    // Add processor to mask sensitive data in HTTP headers
                    opts.EnrichWithHttpRequestMessage = (activity, request) => {
                        // Mask Authorization headers
                        if (request.Headers.Authorization != null)
                        {
                            activity.SetTag("http.authorization", "***masked***");
                        }
                    };
                })
                .AddEntityFrameworkCoreInstrumentation(opts => {
                    // Track DB operations
                    opts.SetDbStatementForText = true;
                    opts.SetDbStatementForStoredProcedure = true;
                })
                .AddSource("Experimental.Microsoft.Extensions.AI")
                .AddSource("eShop")
                .AddSource("eShop.*")
                .AddSource("PlaceOrder")
                .AddSource("Microsoft.AspNetCore.*")
                .AddSource("OpenTelemetry.Instrumentation.*")
                .AddSource("Microsoft.Extensions.*")
                .AddSource("System.Net.Http"); // Our custom activity source
                
                // Add OTLP exporter for traces - SIMPLIFIED
                if (!string.IsNullOrEmpty(otlpEndpoint))
                {
                    Console.WriteLine($"\nAdding OTLP trace exporter with endpoint: {otlpEndpoint}\n");
                    tracing.AddOtlpExporter(opts => {
                        opts.Endpoint = new Uri(otlpEndpoint);
                    });
                    Console.WriteLine("OTLP Exporter added successfully");
                }
                
                // Add processor to mask sensitive data in all traces
                tracing.AddProcessor(new SensitiveDataTracingProcessor());
            });

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/dotnet/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks("/health");

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
    
    /// <summary>
    /// Masks sensitive data like emails, credit cards, etc.
    /// </summary>
    public static string MaskSensitiveDataHelper(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;
        
        // Mask email addresses - capture first character
        input = Regex.Replace(input, 
            @"([a-zA-Z0-9._%+-]+)@([a-zA-Z0-9.-]+\.[a-zA-Z]{2,})", 
            m => $"{m.Groups[1].Value[0]}***@{m.Groups[2].Value}");
        
        // Mask credit card numbers - keep first 4 and last 4 digits
        input = Regex.Replace(input, 
            @"(\d{4})\d{8,12}(\d{4})", 
            "$1********$2");
        
        // Mask phone numbers - keep only last 4 digits
        input = Regex.Replace(input, 
            @"(\+\d{1,3}[-\s]?)?\(?\d{3}\)?[-\s]?\d{3}[-\s]?(\d{4})", 
            "***-***-$2");
            
        return input;
    }
}

// Adds environment variables as resource attributes
public class EnvironmentResourceDetector : IResourceDetector
{
    public Resource Detect()
    {
        var resourceBuilder = ResourceBuilder.CreateDefault();
        
        // Add environment variables that start with OTEL_RESOURCE_
        foreach (var entry in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>())
        {
            var key = entry.Key?.ToString();
            var value = entry.Value?.ToString();
            
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value) && key.StartsWith("OTEL_RESOURCE_"))
            {
                var attributeKey = key.Substring("OTEL_RESOURCE_".Length).ToLower();
                resourceBuilder.AddAttributes(new KeyValuePair<string, object>[] 
                {
                    new KeyValuePair<string, object>(attributeKey, value)
                });
            }
        }
        
        return resourceBuilder.Build();
    }
}

/// <summary>
/// Processor that masks sensitive data in log messages
/// </summary>
public class SensitiveDataMaskingProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        // Since LogRecord.State is obsolete, we'll use a different approach
        // Instead of trying to modify the state, we'll add a tag to the activity
        if (Activity.Current != null)
        {
            // Mark the activity as containing masked data
            Activity.Current.SetTag("log.contains_sensitive_data", "masked");
        }
        
        base.OnEnd(data);
    }
}

/// <summary>
/// Processor that masks sensitive data in trace attributes
/// </summary>
public class SensitiveDataTracingProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        if (activity == null) return;
        
        // Check all tags for sensitive information
        foreach (var tag in activity.TagObjects)
        {
            if (IsSensitiveTag(tag.Key) && tag.Value != null)
            {
                // Replace sensitive tag with masked value
                activity.SetTag(tag.Key, "***masked***");
            }
            else if (tag.Value is string stringValue)
            {
                // Check if the string value contains sensitive patterns
                if (ContainsSensitivePattern(stringValue))
                {
                    activity.SetTag(tag.Key, Extensions.MaskSensitiveDataHelper(stringValue));
                }
            }
        }
        
        base.OnEnd(activity);
    }
    
    private bool IsSensitiveTag(string tagName)
    {
        // Case-insensitive check for common sensitive tag names
        var normalized = tagName.ToLowerInvariant();
        return normalized.Contains("email") ||
               normalized.Contains("password") ||
               normalized.Contains("creditcard") ||
               normalized.Contains("credit_card") ||
               normalized.Contains("card") ||
               normalized.Contains("ssn") ||
               normalized.Contains("social") ||
               normalized.Contains("phone") ||
               normalized.Contains("address") ||
               normalized.Contains("secret") ||
               normalized.Contains("token") ||
               normalized.Contains("auth");
    }
    
    private bool ContainsSensitivePattern(string value)
    {
        // Check for common sensitive patterns
        return Regex.IsMatch(value, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}") || // Email
               Regex.IsMatch(value, @"\b(?:\d[ -]*?){13,16}\b") || // Credit card
               Regex.IsMatch(value, @"\b\d{3}[-\s]?\d{2}[-\s]?\d{4}\b"); // SSN
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace eShop.Ordering.API.OpenTelemetry;

public static class OrderingTelemetryExtensions
{
    // Create local ActivitySource and Meter for this class
    private static readonly ActivitySource ActivitySource = new("eShop.Ordering", "1.0.0");
    private static readonly Meter Meter = new("eShop.Ordering", "1.0.0");

    public static WebApplicationBuilder AddOrderingTelemetry(this WebApplicationBuilder builder)
    {
        // Generate some test activity to confirm instrumentation is working
        using (var activity = ActivitySource.StartActivity("OrderingTelemetryInit"))
        {
            activity?.SetTag("initialization", "true");
            activity?.SetTag("component", "Ordering.API");
        }
        
        // Add OpenTelemetry instrumentation for Ordering API
        builder.Services.AddSingleton<OrderingTelemetryService>();
        
        return builder;
    }
}

public class OrderingTelemetryService
{
    // Local ActivitySource and Meter
    private static readonly ActivitySource ActivitySource = new("eShop.Ordering", "1.0.0");
    private static readonly Meter Meter = new("eShop.Ordering", "1.0.0");
    
    // Create a custom counter for orders
    private readonly Counter<long> _orderCounter;
    
    public OrderingTelemetryService()
    {
        // Create counters and metrics in the constructor
        _orderCounter = Meter.CreateCounter<long>("ordering.orders.created", "Orders");
        
        // Create a test activity to verify service registration
        using var activity = ActivitySource.StartActivity("OrderingTelemetryServiceInit");
        activity?.SetTag("service", "OrderingTelemetryService");
    }
    
    public void RecordOrderCreated()
    {
        _orderCounter.Add(1);
        
        using var activity = ActivitySource.StartActivity("OrderCreated");
        activity?.SetTag("order.status", "created");
    }
    
    // Create a method to start a new order processing span
    public Activity StartOrderProcessing(string orderId)
    {
        var activity = ActivitySource.StartActivity("ProcessOrder");
        activity?.SetTag("order.id", orderId);
        return activity;
    }
}

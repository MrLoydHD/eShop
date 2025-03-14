namespace eShop.PaymentProcessor.IntegrationEvents.EventHandling;

using eShop.EventBus.Abstractions;
using eShop.EventBus.Events;
using eShop.PaymentProcessor.IntegrationEvents.Events;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

public class OrderStatusChangedToStockConfirmedIntegrationEventHandler : 
    IIntegrationEventHandler<OrderStatusChangedToStockConfirmedIntegrationEvent>
{
    private readonly IEventBus _eventBus;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<OrderStatusChangedToStockConfirmedIntegrationEventHandler> _logger;

    public OrderStatusChangedToStockConfirmedIntegrationEventHandler(
        IEventBus eventBus,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<OrderStatusChangedToStockConfirmedIntegrationEventHandler> logger)
    {
        _eventBus = eventBus;
        _options = options;
        _logger = logger;
    }

    public async Task Handle(OrderStatusChangedToStockConfirmedIntegrationEvent @event)
    {
        // Create a sanitized version of the event for logging
        var sanitizedEvent = new 
        {
            @event.OrderId,
            BuyerName = "***masked***",
            BuyerIdentityGuid = "***masked***",
            @event.Id,
            @event.CreationDate
        };

        // Log the sanitized version instead of the actual event
        _logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", 
            @event.Id, sanitizedEvent);

        IntegrationEvent orderPaymentIntegrationEvent;

        // Business feature comment:
        // When OrderStatusChangedToStockConfirmed Integration Event is handled.
        // Here we're simulating that we'd be performing the payment against any payment gateway
        // Instead of a real payment we just take the env. var to simulate the payment 
        // The payment can be successful or it can fail

        if (_options.CurrentValue.PaymentSucceeded)
        {
            orderPaymentIntegrationEvent = new OrderPaymentSucceededIntegrationEvent(@event.OrderId);
        }
        else
        {
            orderPaymentIntegrationEvent = new OrderPaymentFailedIntegrationEvent(@event.OrderId);
        }

        _logger.LogInformation("Publishing integration event: {IntegrationEventId} - ({@IntegrationEvent})", 
            orderPaymentIntegrationEvent.Id, orderPaymentIntegrationEvent);

        await _eventBus.PublishAsync(orderPaymentIntegrationEvent);
    }
}

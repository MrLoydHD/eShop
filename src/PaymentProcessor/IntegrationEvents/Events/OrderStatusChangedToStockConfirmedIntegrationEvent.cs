namespace eShop.PaymentProcessor.IntegrationEvents.Events;

public record OrderStatusChangedToStockConfirmedIntegrationEvent(int OrderId) : IntegrationEvent
{
    public object OrderStatus { get; set; }
}

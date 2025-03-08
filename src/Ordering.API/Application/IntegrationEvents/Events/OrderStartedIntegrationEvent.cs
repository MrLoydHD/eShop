// Add a parameterless constructor to OrderStartedIntegrationEvent.cs
namespace eShop.Ordering.API.Application.IntegrationEvents.Events
{
    public record OrderStartedIntegrationEvent : IntegrationEvent
    {
        public string UserId { get; init; }

        // Add this parameterless constructor for deserialization
        public OrderStartedIntegrationEvent()
        {
            // Empty constructor for serialization
        }

        public OrderStartedIntegrationEvent(string userId)
            => UserId = userId;

        // This constructor can be removed or fixed
        public OrderStartedIntegrationEvent(object userId, string commandUserId, string commandUserName, List<OrderItemDTO> toList)
        {
            throw new NotImplementedException();
        }
    }
}

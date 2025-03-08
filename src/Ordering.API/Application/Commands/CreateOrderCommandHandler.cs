using eShop.Ordering.API.Application.IntegrationEvents;
using eShop.Ordering.API.Application.Models;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;
using eShop.Ordering.API.OpenTelemetry;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using eShop.Ordering.API.Application.IntegrationEvents.Events;
using System.Diagnostics.Metrics;

namespace eShop.Ordering.API.Application.Commands
{
    // Regular command handler enriched with OpenTelemetry instrumentation
    public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, bool>
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IOrderingIntegrationEventService _orderingIntegrationEventService;
        private readonly ILogger<CreateOrderCommandHandler> _logger;
        private readonly OrderingTelemetryService _telemetryService;
        
        // Constructor with all dependencies
        public CreateOrderCommandHandler(
            IOrderRepository orderRepository, 
            IOrderingIntegrationEventService orderingIntegrationEventService,
            ILogger<CreateOrderCommandHandler> logger,
            OrderingTelemetryService telemetryService)
        {
            _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
            _orderingIntegrationEventService = orderingIntegrationEventService ?? throw new ArgumentNullException(nameof(orderingIntegrationEventService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        }

        public async Task<bool> Handle(CreateOrderCommand command, CancellationToken cancellationToken)
        {
            // Start timing the operation
            var startTime = Stopwatch.GetTimestamp();
            
            // Start a new activity/span for order creation
            using var orderActivity = _telemetryService.StartOrderProcessing(-1); // Will update with real ID
            orderActivity?.SetTag("order.buyer", command.UserName);
            orderActivity?.SetTag("order.items_count", command.OrderItems.Count());
            
            // Calculate order total for metrics
            decimal orderTotal = command.OrderItems.Sum(i => i.UnitPrice * i.Units);
            
            try
            {
                // Create the Order AggregateRoot
                var address = new Address(command.Street, command.City, command.State, command.Country, command.ZipCode);
                
                // Create Order using the fully qualified name to resolve ambiguity
                var order = new eShop.Ordering.Domain.AggregatesModel.OrderAggregate.Order(
                    command.UserId,             // string userId
                    command.UserName,           // string userName
                    address,                    // Address address
                    command.CardTypeId,         // int cardTypeId
                    command.CardNumber,         // string cardNumber
                    command.CardSecurityNumber, // string cardSecurityNumber
                    command.CardHolderName,     // string cardHolderName
                    command.CardExpiration      // DateTime cardExpiration
                );

                foreach (var item in command.OrderItems)
                {
                    order.AddOrderItem(item.ProductId, item.ProductName, item.UnitPrice, item.Discount, item.PictureUrl, item.Units);
                }

                _logger.LogInformation("----- Creating Order - Order: {@Order}", order);

                // Add the order to the repository
                _orderRepository.Add(order);
                
                // Save changes
                await _orderRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken);
                
                // Record the order creation with the real ID
                _telemetryService.RecordOrderCreated(order.Id, (double)orderTotal);
                
                // Update the activity with the real order ID
                orderActivity?.SetTag("order.id", order.Id);
                
                // Publish integration event to notify other services
                // Use the simple constructor that only takes userId
                var orderStartedIntegrationEvent = new OrderStartedIntegrationEvent(command.UserId);
                
                await _orderingIntegrationEventService.AddAndSaveEventAsync(orderStartedIntegrationEvent);
                
                // Mask sensitive data in logs
                var maskedCardNumber = _telemetryService.MaskSensitiveData(command.CardNumber, true);
                
                _logger.LogInformation("----- Order {OrderId} created successfully for user {UserName}, payment method: {PaymentMethod} ({CardNumber})",
                    order.Id, command.UserName, "Credit Card", maskedCardNumber);
                
                // Calculate and record operation duration
                var elapsedMs = GetElapsedMilliseconds(startTime);
                _telemetryService.RecordOrderCompleted(order.Id, elapsedMs);
                
                return true;
            }
            catch (Exception ex)
            {
                // Record failure with exception details
                _logger.LogError(ex, "Error creating order for user {UserName}", command.UserName);
                
                // Record the failure in telemetry
                _telemetryService.RecordOrderFailed(-1, ex.Message);
                
                // Mark the activity as failed
                orderActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                
                throw;
            }
        }
        
        // Helper method to calculate elapsed milliseconds
        private static double GetElapsedMilliseconds(long start)
        {
            return (Stopwatch.GetTimestamp() - start) * 1000 / (double)Stopwatch.Frequency;
        }
    }
}

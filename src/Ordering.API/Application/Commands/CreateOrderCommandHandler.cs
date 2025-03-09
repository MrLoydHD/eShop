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
    public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, bool>
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IOrderingIntegrationEventService _orderingIntegrationEventService;
        private readonly ILogger<CreateOrderCommandHandler> _logger;
        private readonly OrderingTelemetryService _telemetryService;
        
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
            
            // Start a new activity/span for order processing
            using var orderActivity = _telemetryService.StartOrderProcessing(-1); // Will update with real ID
            orderActivity?.SetTag("order.buyer", command.UserName);
            orderActivity?.SetTag("order.items_count", command.OrderItems.Count());
            
            // Calculate order total for metrics (but don't record it yet)
            decimal orderTotal = command.OrderItems.Sum(i => i.UnitPrice * i.Units);
            
            try
            {
                // Create the Order AggregateRoot
                var address = new Address(command.Street, command.City, command.State, command.Country, command.ZipCode);
                
                // Create Order using the fully qualified name to resolve ambiguity
                var order = new eShop.Ordering.Domain.AggregatesModel.OrderAggregate.Order(
                    command.UserId,
                    command.UserName,
                    address,
                    command.CardTypeId,
                    command.CardNumber,
                    command.CardSecurityNumber,
                    command.CardHolderName,
                    command.CardExpiration
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
                
                // Update the activity with the real order ID
                orderActivity?.SetTag("order.id", order.Id);
                
                // Publish integration event to notify other services
                var orderStartedIntegrationEvent = new OrderStartedIntegrationEvent(command.UserId);
                
                await _orderingIntegrationEventService.AddAndSaveEventAsync(orderStartedIntegrationEvent);
                
                // Mask sensitive data in logs
                var maskedCardNumber = _telemetryService.MaskSensitiveData(command.CardNumber, true);
                
                _logger.LogInformation("----- Order {OrderId} created successfully for user {UserName}, payment method: {PaymentMethod} ({CardNumber})",
                    order.Id, command.UserName, "Credit Card", maskedCardNumber);
                
                // Calculate elapsed time
                var elapsedMs = GetElapsedMilliseconds(startTime);
                
                // Note: We don't call RecordOrderCompleted here as it's handled at the API layer
                // Just update the activity with timing data
                orderActivity?.SetTag("processing.time.ms", elapsedMs);
                
                return true;
            }
            catch (Exception ex)
            {
                // Record failure with exception details
                _logger.LogError(ex, "Error creating order for user {UserName}", command.UserName);
                
                // Mark the activity as failed
                orderActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                orderActivity?.SetTag("error.type", ex.GetType().Name);
                orderActivity?.SetTag("error.message", ex.Message);
                orderActivity?.SetTag("error.stack_trace", ex.StackTrace);
                
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

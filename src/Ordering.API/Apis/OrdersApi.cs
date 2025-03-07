using Microsoft.AspNetCore.Http.HttpResults;
using System.Diagnostics;
using CardType = eShop.Ordering.API.Application.Queries.CardType;
using Order = eShop.Ordering.API.Application.Queries.Order;
using eShop.Ordering.API.OpenTelemetry;
using System.Diagnostics.Metrics;

public static class OrdersApi
{
    // Create a dedicated ActivitySource and Meter for this class
    private static readonly ActivitySource ActivitySource = new("eShop.Ordering", "1.0.0");
    private static readonly Meter Meter = new("eShop.Ordering", "1.0.0");

    // Helpers to mask sensitive data
    private static string MaskUserId(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return userId;

        // If it's a UUID/GUID format, keep first and last components
        if (Guid.TryParse(userId, out _))
        {
            var parts = userId.Split('-');
            if (parts.Length >= 5)
            {
                return $"{parts[0]}-****-****-****-{parts[4]}";
            }
        }

        // If it's an email, mask the local part except first character
        if (userId.Contains('@'))
        {
            var parts = userId.Split('@');
            if (parts.Length == 2 && !string.IsNullOrEmpty(parts[0]))
            {
                return $"{parts[0][0]}***@{parts[1]}";
            }
        }

        // For other formats, mask the middle portion
        if (userId.Length > 6)
        {
            int visibleChars = Math.Max(2, userId.Length / 4);
            return $"{userId.Substring(0, visibleChars)}****{userId.Substring(userId.Length - visibleChars)}";
        }

        // For short IDs, just return a generic mask
        return "****";
    }

    public static RouteGroupBuilder MapOrdersApiV1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("api/orders").HasApiVersion(1.0);

        api.MapPut("/cancel", CancelOrderAsync);
        api.MapPut("/ship", ShipOrderAsync);
        api.MapGet("{orderId:int}", GetOrderAsync);
        api.MapGet("/", GetOrdersByUserAsync);
        api.MapGet("/cardtypes", GetCardTypesAsync);
        api.MapPost("/draft", CreateOrderDraftAsync);
        api.MapPost("/", CreateOrderAsync);

        return api;
    }

    public static async Task<Results<Ok, BadRequest<string>, ProblemHttpResult>> CancelOrderAsync(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        CancelOrderCommand command,
        [AsParameters] OrderServices services)
    {
        if (requestId == Guid.Empty)
        {
            return TypedResults.BadRequest("Empty GUID is not valid for request ID");
        }

        // Create a child activity for order cancellation
        using var activity = ActivitySource.StartActivity("CancelOrder");
        activity?.SetTag("order.number", command.OrderNumber);
        activity?.SetTag("request.id", requestId.ToString());
            
        var requestCancelOrder = new IdentifiedCommand<CancelOrderCommand, bool>(command, requestId);

        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            requestCancelOrder.GetGenericTypeName(),
            nameof(requestCancelOrder.Command.OrderNumber),
            requestCancelOrder.Command.OrderNumber,
            requestCancelOrder);

        try
        {
            var commandResult = await services.Mediator.Send(requestCancelOrder);

            if (!commandResult)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Cancel order failed to process");
                return TypedResults.Problem(detail: "Cancel order failed to process.", statusCode: 500);
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return TypedResults.Ok();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            // Replace RecordException with setting exception details as tags
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            activity?.SetTag("error.stack_trace", ex.StackTrace);
            throw;
        }
    }

    public static async Task<Results<Ok, BadRequest<string>, ProblemHttpResult>> ShipOrderAsync(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        ShipOrderCommand command,
        [AsParameters] OrderServices services)
    {
        if (requestId == Guid.Empty)
        {
            return TypedResults.BadRequest("Empty GUID is not valid for request ID");
        }

        // Create a child activity for order shipping
        using var activity = ActivitySource.StartActivity("ShipOrder");
        activity?.SetTag("order.number", command.OrderNumber);
        activity?.SetTag("request.id", requestId.ToString());

        var requestShipOrder = new IdentifiedCommand<ShipOrderCommand, bool>(command, requestId);

        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            requestShipOrder.GetGenericTypeName(),
            nameof(requestShipOrder.Command.OrderNumber),
            requestShipOrder.Command.OrderNumber,
            requestShipOrder);

        try
        {
            var commandResult = await services.Mediator.Send(requestShipOrder);

            if (!commandResult)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Ship order failed to process");
                return TypedResults.Problem(detail: "Ship order failed to process.", statusCode: 500);
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return TypedResults.Ok();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            // Replace RecordException with setting exception details as tags
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            activity?.SetTag("error.stack_trace", ex.StackTrace);
            throw;
        }
    }

    public static async Task<Results<Ok<Order>, NotFound>> GetOrderAsync(int orderId, [AsParameters] OrderServices services)
    {
        using var activity = ActivitySource.StartActivity("GetOrder");
        activity?.SetTag("order.id", orderId);
            
        try
        {
            var order = await services.Queries.GetOrderAsync(orderId);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return TypedResults.Ok(order);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            // Replace RecordException with setting exception details as tags
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            activity?.SetTag("error.stack_trace", ex.StackTrace);
            return TypedResults.NotFound();
        }
    }

    public static async Task<Ok<IEnumerable<OrderSummary>>> GetOrdersByUserAsync([AsParameters] OrderServices services)
    {
        var userId = services.IdentityService.GetUserIdentity();
        
        using var activity = ActivitySource.StartActivity("GetOrdersByUser");
        
        // Mask the user ID in our telemetry
        if (activity != null)
        {
            // We use a special method to mask the user ID while preserving some info for debugging
            activity.SetTag("user.id.masked", MaskUserId(userId));
        }
        
        var orders = await services.Queries.GetOrdersFromUserAsync(userId);
        
        // Record the count as a metric
        activity?.SetTag("orders.count", orders.Count());
        activity?.SetStatus(ActivityStatusCode.Ok);
        
        return TypedResults.Ok(orders);
    }

    public static async Task<Ok<IEnumerable<CardType>>> GetCardTypesAsync(IOrderQueries orderQueries)
    {
        var cardTypes = await orderQueries.GetCardTypesAsync();
        return TypedResults.Ok(cardTypes);
    }

    public static async Task<OrderDraftDTO> CreateOrderDraftAsync(CreateOrderDraftCommand command, [AsParameters] OrderServices services)
    {
        using var activity = ActivitySource.StartActivity("CreateOrderDraft");
        
        if (activity != null)
        {
            // We'll just add the buyer ID as a tag
            activity.SetTag("user.id.masked", MaskUserId(command.BuyerId));
        }
        
        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            command.GetGenericTypeName(),
            nameof(command.BuyerId),
            command.BuyerId,
            command);

        try
        {
            var result = await services.Mediator.Send(command);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            // Replace RecordException with setting exception details as tags
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            activity?.SetTag("error.stack_trace", ex.StackTrace);
            throw;
        }
    }

    public static async Task<Results<Ok, BadRequest<string>>> CreateOrderAsync(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        CreateOrderRequest request,
        [AsParameters] OrderServices services,
        OrderingTelemetryService telemetryService)
    {
        // Start the main order creation activity
        using var orderActivity = ActivitySource.StartActivity("PlaceOrder");
        orderActivity?.SetTag("request.id", requestId.ToString());
        orderActivity?.SetTag("order.items.count", request.Items.Count);
        orderActivity?.SetTag("user.id.masked", MaskUserId(request.UserId));
            
        // Record order value for metrics
        // Use Quantity instead of Units
        double orderTotal = request.Items.Sum(i => (double)(i.UnitPrice * i.Quantity)); // Explicit cast to double
        Meter.CreateHistogram<double>("order.value").Record(orderTotal);
            
        // Log the beginning of order process - careful not to log sensitive data
        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId}",
            request.GetGenericTypeName(),
            nameof(request.UserId),
            request.UserId); // Don't log the request as it has CC number

        if (requestId == Guid.Empty)
        {
            orderActivity?.SetStatus(ActivityStatusCode.Error, "Invalid request - RequestId is missing");
            services.Logger.LogWarning("Invalid IntegrationEvent - RequestId is missing");
            return TypedResults.BadRequest("RequestId is missing.");
        }

        using (services.Logger.BeginScope(new List<KeyValuePair<string, object>> { new("IdentifiedCommandId", requestId) }))
        {
            // Create payment processing sub-activity
            using var paymentActivity = ActivitySource.StartActivity("ProcessPayment");
            paymentActivity?.SetTag("card.type.id", request.CardTypeId);
            paymentActivity?.SetTag("payment.amount", orderTotal);
                
            try
            {
                // Mask credit card information both for logging and telemetry
                var maskedCCNumber = request.CardNumber.Substring(request.CardNumber.Length - 4).PadLeft(request.CardNumber.Length, 'X');
                
                if (paymentActivity != null)
                {
                    // Only add masked data to telemetry
                    paymentActivity.SetTag("payment.card.masked", maskedCCNumber);
                    paymentActivity.SetTag("payment.card.expiration.year", request.CardExpiration.Year);
                    paymentActivity.SetTag("payment.card.expiration.month", request.CardExpiration.Month);
                }
                
                // Create the order command with masked CC data
                var createOrderCommand = new CreateOrderCommand(request.Items, request.UserId, request.UserName, request.City, request.Street,
                    request.State, request.Country, request.ZipCode,
                    maskedCCNumber, request.CardHolderName, request.CardExpiration,
                    request.CardSecurityNumber, request.CardTypeId);

                var requestCreateOrder = new IdentifiedCommand<CreateOrderCommand, bool>(createOrderCommand, requestId);

                services.Logger.LogInformation(
                    "Sending command: {CommandName} - {IdProperty}: {CommandId}",
                    requestCreateOrder.GetGenericTypeName(),
                    nameof(requestCreateOrder.Id),
                    requestCreateOrder.Id);

                // Record start time for processing duration metric
                var startTime = Stopwatch.GetTimestamp();
                
                var result = await services.Mediator.Send(requestCreateOrder);
                
                // Calculate and record processing time
                var endTime = Stopwatch.GetTimestamp();
                var elapsedMilliseconds = Stopwatch.GetElapsedTime(startTime, endTime).TotalMilliseconds;
                Meter.CreateHistogram<double>("order.processing.time").Record(elapsedMilliseconds);
                orderActivity?.SetTag("processing.time.ms", elapsedMilliseconds);

                if (result)
                {
                    services.Logger.LogInformation("CreateOrderCommand succeeded - RequestId: {RequestId}", requestId);
                    telemetryService.RecordOrderCreated();
                    paymentActivity?.SetStatus(ActivityStatusCode.Ok);
                    orderActivity?.SetStatus(ActivityStatusCode.Ok);
                }
                else
                {
                    services.Logger.LogWarning("CreateOrderCommand failed - RequestId: {RequestId}", requestId);
                    Meter.CreateCounter<long>("orders.failed").Add(1);
                    paymentActivity?.SetStatus(ActivityStatusCode.Error, "Payment processing failed");
                    orderActivity?.SetStatus(ActivityStatusCode.Error, "Order creation failed");
                }

                return TypedResults.Ok();
            }
            catch (Exception ex)
            {
                // Replace RecordException with setting exception details as tags
                if (paymentActivity != null)
                {
                    paymentActivity.SetStatus(ActivityStatusCode.Error, ex.Message);
                    paymentActivity.SetTag("error.type", ex.GetType().Name);
                    paymentActivity.SetTag("error.message", ex.Message);
                    paymentActivity.SetTag("error.stack_trace", ex.StackTrace);
                }
                
                if (orderActivity != null)
                {
                    orderActivity.SetStatus(ActivityStatusCode.Error, ex.Message);
                    orderActivity.SetTag("error.type", ex.GetType().Name);
                    orderActivity.SetTag("error.message", ex.Message);
                    orderActivity.SetTag("error.stack_trace", ex.StackTrace);
                }
                
                Meter.CreateCounter<long>("orders.failed").Add(1);
                throw;
            }
        }
    }
}

public record CreateOrderRequest(
    string UserId,
    string UserName,
    string City,
    string Street,
    string State,
    string Country,
    string ZipCode,
    string CardNumber,
    string CardHolderName,
    DateTime CardExpiration,
    string CardSecurityNumber,
    int CardTypeId,
    string Buyer,
    List<BasketItem> Items);

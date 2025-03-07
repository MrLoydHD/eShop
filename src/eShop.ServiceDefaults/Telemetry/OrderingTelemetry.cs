using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace eShop.ServiceDefaults.Telemetry;

/// <summary>
/// Provides telemetry instrumentation for the order placement flow
/// </summary>
public static class OrderingTelemetry
{
    // Ensure the ActivitySource name is more distinctive for easier identification
    /// <summary>
    /// ActivitySource for the Place Order flow
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("eShop.Ordering.PlaceOrder", "1.0.0");

    /// <summary>
    /// Metrics for order processing
    /// </summary>
    public static readonly Meter Meter = new("eShop.Ordering", "1.0.0");

    /// <summary>
    /// Counter for total orders
    /// </summary>
    public static readonly Counter<long> OrdersCreated = Meter.CreateCounter<long>("orders.created", "Orders");

    /// <summary>
    /// Counter for completed orders
    /// </summary>
    public static readonly Counter<long> OrdersCompleted = Meter.CreateCounter<long>("orders.completed", "Orders");

    /// <summary>
    /// Counter for failed orders
    /// </summary>
    public static readonly Counter<long> OrdersFailed = Meter.CreateCounter<long>("orders.failed", "Orders");

    /// <summary>
    /// Histogram for order processing time
    /// </summary>
    public static readonly Histogram<double> OrderProcessingTime = Meter.CreateHistogram<double>("order.processing.time", "ms");

    /// <summary>
    /// Histogram for order value
    /// </summary>
    public static readonly Histogram<double> OrderValue = Meter.CreateHistogram<double>("order.value", "USD");

    // Static constructor to ensure ActivitySource is registered
    static OrderingTelemetry()
    {
        // Force registration by creating a test activity
        using var activity = ActivitySource.StartActivity("Startup");
        if (activity != null)
        {
            activity.SetTag("telemetry.initialization", "true");
            activity.Stop();
        }
    }

    /// <summary>
    /// Get an activity builder for starting order creation
    /// </summary>
    public static ActivityBuilder CreateOrderActivity(string userId, string basketId)
    {
        // Skip if no listeners
        if (!ActivitySource.HasListeners())
            return new ActivityBuilder(null);

        var activity = ActivitySource.CreateActivity(
            "PlaceOrder",
            ActivityKind.Internal,
            parentContext: Activity.Current?.Context ?? default);

        if (activity == null)
            return new ActivityBuilder(null);

        // Add common attributes to the activity
        activity.SetTag("order.flow", "creation");
        activity.SetTag("user.id", MaskUserId(userId)); // Mask user ID for privacy
        activity.SetTag("basket.id", basketId);

        return new ActivityBuilder(activity);
    }

    /// <summary>
    /// Create an activity for processing a payment
    /// </summary>
    public static ActivityBuilder ProcessPaymentActivity(string orderId, decimal amount)
    {
        // Skip if no listeners
        if (!ActivitySource.HasListeners())
            return new ActivityBuilder(null);

        var activity = ActivitySource.CreateActivity(
            "ProcessPayment",
            ActivityKind.Internal,
            parentContext: Activity.Current?.Context ?? default);

        if (activity == null)
            return new ActivityBuilder(null);

        // Add payment-related attributes to the activity
        activity.SetTag("order.id", orderId);
        activity.SetTag("payment.amount", amount);
        // Notice we don't include sensitive payment method details

        return new ActivityBuilder(activity);
    }

    /// <summary>
    /// Create an activity for updating order status
    /// </summary>
    public static ActivityBuilder UpdateOrderStatusActivity(string orderId, string status)
    {
        // Skip if no listeners
        if (!ActivitySource.HasListeners())
            return new ActivityBuilder(null);

        var activity = ActivitySource.CreateActivity(
            "UpdateOrderStatus",
            ActivityKind.Internal,
            parentContext: Activity.Current?.Context ?? default);

        if (activity == null)
            return new ActivityBuilder(null);

        // Add order status update attributes
        activity.SetTag("order.id", orderId);
        activity.SetTag("order.status", status);

        return new ActivityBuilder(activity);
    }

    /// <summary>
    /// Mask sensitive user ID information
    /// </summary>
    public static string MaskUserId(string userId)
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

    /// <summary>
    /// Helper struct for creating and managing activities
    /// </summary>
    public readonly struct ActivityBuilder
    {
        private readonly Activity? _activity;

        public ActivityBuilder(Activity? activity)
        {
            _activity = activity;
        }

        /// <summary>
        /// Start the activity
        /// </summary>
        public Activity? Start()
        {
            _activity?.Start();
            return _activity;
        }

        /// <summary>
        /// Add a tag to the activity
        /// </summary>
        public ActivityBuilder WithTag(string key, object? value)
        {
            _activity?.SetTag(key, value);
            return this;
        }

        /// <summary>
        /// Add tags to the activity
        /// </summary>
        public ActivityBuilder WithTags(IEnumerable<KeyValuePair<string, object?>> tags)
        {
            if (_activity != null)
            {
                foreach (var tag in tags)
                {
                    _activity.SetTag(tag.Key, tag.Value);
                }
            }
            return this;
        }
    }
}

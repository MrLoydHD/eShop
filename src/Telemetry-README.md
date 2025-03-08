# OpenTelemetry & Security Implementation in eShop

This document outlines the implementation of OpenTelemetry tracing and security enhancements for the "Place an Order" feature in the eShop application.

## 1. Architecture Overview

### 1.1 Instrumented Flow: "Place an Order"

I've chosen to implement OpenTelemetry instrumentation for the "Place an Order" feature, which spans across multiple microservices:

```
Web/Mobile App → Basket API → Ordering API → OrderProcessor → Payment Processor
```

### 1.2 Components Modified

- **Ordering.API**: Enhanced with custom telemetry service and middleware
- **CreateOrderCommandHandler**: Instrumented with tracing for the order creation flow
- **OrderingTelemetry Extensions**: Added metrics, traces, and context propagation
- **Load Testing**: Custom k6 script to generate traffic and telemetry data

## 2. OpenTelemetry Implementation

### 2.1 Tracing Implementation

- Added custom `ActivitySource` for the Ordering API
- Instrumented the main order processing flow with spans and events
- Added context propagation between services using HTTP headers
- Masked sensitive data (PII) in traces using custom processors

### 2.2 Metrics Implementation

The following metrics are now collected:

| Metric Name | Type | Description |
|-------------|------|-------------|
| `orders_created_total` | Counter | Number of orders created |
| `orders_completed_total` | Counter | Number of orders completed successfully |
| `orders_failed_total` | Counter | Number of orders that failed |
| `order_processing_time` | Histogram | Time taken to process an order (ms) |
| `order_value` | Histogram | Value of the order in USD |

### 2.3 Data Masking & Security

- Implemented sensitive data masking in logs and traces:
  - Credit card numbers (last 4 digits preserved)
  - Email addresses (domain part preserved)
  - Payment security codes (fully masked)
- Added JSON sanitization for request/response bodies
- Configured HTTP logging to exclude sensitive headers

## 3. Dashboard Implementation

The dashboard has been integrated with the metrics to visualize:

- Total orders created, completed, and failed
- Order processing time distribution
- Order value distribution
- Error rates by endpoint
- Recent order traces with links to Jaeger UI

## 4. Load Testing

A custom k6 load test script (`order-flow-load-test.js`) has been created that:

1. Simulates users logging in
2. Browses the catalog
3. Adds items to cart
4. Completes checkout
5. Verifies order creation

The script includes proper headers for distributed tracing and generates a realistic load pattern.

## 5. How to Run

### 5.1 Start Observability Stack

```bash
cd observability
docker-compose up -d
```

This will start:
- Jaeger (tracing)
- Prometheus (metrics)
- Grafana (visualization)
- OpenTelemetry Collector (data collection)

### 5.2 Build and Run the eShop Application

```bash
dotnet build
dotnet run --project src/eShop.AppHost
```

### 5.3 Run the Load Test

```bash
cd loadtest
k6 run order-flow-load-test.js
```

### 5.4 View the Results

- **Grafana Dashboard**: http://localhost:3000/d/eshop-orders
- **Jaeger UI**: http://localhost:16686
- **Prometheus**: http://localhost:9090

## 6. Implementation Details

### 6.1 Code Structure Changes

- Added `OpenTelemetry` folder to Ordering.API
- Enhanced `CreateOrderCommandHandler` with tracing
- Added middleware for trace context propagation
- Created telemetry service for application code to use

### 6.2 Security Enhancements

- Column masking is implemented through the `MaskSensitiveData` and `SanitizeJsonPayload` methods
- Payment information is masked in logs and traces
- Personal identifiable information is protected in all telemetry data

## 7. Future Improvements

- Add additional metrics for order items, categories, and user behavior
- Implement more granular performance tracking for database operations
- Add anomaly detection for order processing time and failures
- Expand telemetry to cover additional services in the order flow

## 8. References

- [OpenTelemetry Documentation](https://opentelemetry.io/docs/)
- [Grafana Dashboard JSON](https://github.com/dotnet/eShop/blob/main/observability/grafana/dashboards/order-processing.json)
- [Load Testing with k6](https://k6.io/docs/)
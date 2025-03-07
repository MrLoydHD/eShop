# OpenTelemetry & Security Implementation for eShop

This project implements OpenTelemetry tracing, metrics, and logging for the eShop application, with a focus on the "Place an Order" flow. It also includes security measures to mask sensitive data in telemetry and logs.

## Features Implemented

1. **End-to-End Tracing** for the "Place an Order" flow across multiple services
2. **Sensitive Data Masking** for PII and payment information
3. **Metrics Collection** for performance and business KPIs
4. **Grafana Dashboard** for visualizing traces and metrics
5. **Load Testing** to simulate user activity and generate telemetry data

## Architecture

The implementation adds the following components to the eShop microservices architecture:

- **Jaeger**: Distributed tracing system for collecting and visualizing trace data
- **Prometheus**: Time series database for storing metrics
- **Grafana**: Dashboard for visualizing metrics and traces

The data flow is as follows:
1. eShop services generate telemetry data using OpenTelemetry instrumentation
2. Sensitive data is masked or excluded before being recorded
3. Trace data is sent to Jaeger via OTLP
4. Metrics are exposed via Prometheus endpoints
5. Grafana visualizes the collected data

## Technical Details

### OpenTelemetry Configuration

We've configured OpenTelemetry in the `eShop.ServiceDefaults` project to ensure consistent behavior across all services. Key components include:

- Activity sources for custom tracing
- Metrics meters for business and technical KPIs
- Processors to mask sensitive data
- Exporters for Jaeger and Prometheus

### Data Masking Implementation

We've implemented two types of data masking:

1. **Pattern-based masking**: Uses regex patterns to detect and mask email addresses, credit card numbers, etc.
2. **Field-based masking**: Identifies sensitive fields by name (e.g., "password", "creditCard") and masks them automatically

Example of masked data:
- Credit Card: `4111111111111111` → `4111********1111`
- Email: `user@example.com` → `u***@example.com`
- User ID (UUID): `123e4567-e89b-12d3-a456-426614174000` → `123e4567-****-****-****-426614174000`

### Instrumented API Endpoints

The following API endpoints in the "Place an Order" flow have been instrumented:

- `POST /api/orders` - Create a new order
- `POST /api/orders/draft` - Create an order draft
- `GET /api/orders` - Get orders for a user
- `GET /api/orders/{orderId}` - Get a specific order
- `PUT /api/orders/cancel` - Cancel an order
- `PUT /api/orders/ship` - Mark an order as shipped

### Metrics

We collect the following business and technical metrics:

- **Business Metrics**:
  - Order count (created, completed, failed)
  - Order value distribution
  - Order processing time

- **Technical Metrics**:
  - HTTP request duration
  - HTTP request rate
  - HTTP error rate
  - Database operation duration

## Setup and Configuration

### Prerequisites

- Docker Desktop
- .NET 9 SDK
- K6 (for load testing)

### Running the Application with Telemetry

1. Ensure Docker is running
2. Run the application using:
   ```bash
   dotnet run --project src/eShop.AppHost/eShop.AppHost.csproj
   ```
3. Access the dashboards:
   - Aspire Dashboard: http://localhost:19888
   - Grafana: http://localhost:3000 (user: admin, password: admin)
   - Jaeger UI: http://localhost:16686
   - Prometheus: http://localhost:9090

### Running the Load Test

To generate traffic and telemetry data:

1. Install K6:
   ```bash
   # macOS
   brew install k6
   
   # Windows
   choco install k6
   
   # Linux
   apt-get install k6
   ```

2. Run the load test:
   ```bash
   k6 run src/tests/LoadTests/PlaceOrderTest.js
   ```

## Exploring the Telemetry Data

### Viewing Traces in Jaeger

1. Open Jaeger UI at http://localhost:16686
2. Select "ordering-api" from the Service dropdown
3. Click "Find Traces" to see all traces
4. Look for traces with "PlaceOrder" operation to see the order flow

### Using the Grafana Dashboard

1. Open Grafana at http://localhost:3000 (user: admin, password: admin)
2. Navigate to the "eShop Order Processing Dashboard"
3. The dashboard shows:
   - Order volume metrics
   - Order processing time
   - Order value distribution
   - Recent order traces

## Security Considerations

This implementation includes several security measures:

1. **Data Masking**: Sensitive data is masked in logs and traces
2. **Data Minimization**: Only necessary data is collected
3. **Short Retention**: Configured reasonable retention periods for telemetry data
4. **Secure Transmission**: All telemetry data is transmitted within the internal network

## Future Improvements

- Add column-level masking in the database layer
- Implement role-based access control for telemetry data
- Add anomaly detection for security events
- Extend tracing to cover more user flows
- Implement distributed tracing for the frontend applications

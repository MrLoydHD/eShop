#!/bin/bash
# Test script to generate telemetry data for Jaeger

# Wait for services to start up
echo "Waiting for services to start..."
sleep 10

# Make requests to Ordering API endpoints to generate telemetry
echo "Making requests to Ordering API..."

# Get API endpoints from Aspire dashboard or use default ports
ORDERING_API="http://localhost:5102"

# Make several requests to generate telemetry data
echo "Generating telemetry data for order-related endpoints..."

# Get all orders
echo "Getting orders..."
curl -s "${ORDERING_API}/api/orders" \
  -H "Content-Type: application/json" \
  -H "x-requestid: $(uuidgen)" \
  > /dev/null

# Get order by ID (this might 404, but will still generate telemetry)
echo "Getting order by ID..."
curl -s "${ORDERING_API}/api/orders/1" \
  -H "Content-Type: application/json" \
  -H "x-requestid: $(uuidgen)" \
  > /dev/null

# Get card types
echo "Getting card types..."
curl -s "${ORDERING_API}/api/orders/cardtypes" \
  -H "Content-Type: application/json" \
  -H "x-requestid: $(uuidgen)" \
  > /dev/null

# Create an order draft
echo "Creating order draft..."
curl -s -X POST "${ORDERING_API}/api/orders/draft" \
  -H "Content-Type: application/json" \
  -H "x-requestid: $(uuidgen)" \
  -d '{
    "buyerId": "test-buyer",
    "items": [
      {
        "productId": 1,
        "productName": "Test Product",
        "unitPrice": 10,
        "quantity": 1
      }
    ]
  }' \
  > /dev/null

echo "Test requests completed. Check Jaeger for traces."
echo "Jaeger UI: http://localhost:16686"

import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend } from 'k6/metrics';
import { randomIntBetween } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

// Custom metrics
const failRate = new Rate('failed_requests');
const orderCreationTrend = new Trend('order_creation_time');

export const options = {
    // Simple test with 5 iterations (each will create a failed order)
    scenarios: {
        order_failures: {
            executor: 'per-vu-iterations',
            vus: 1,             // Single virtual user
            iterations: 5,      // Run exactly 5 times
            maxDuration: '30s', // Stop after 30s in case of issues
        },
    },
    thresholds: {
        'failed_requests': ['rate>0.9'], // We expect high failure rate
    },
    insecureSkipTLSVerify: true,
};

export function setup() {
    const identityServerUrl = 'https://localhost:5243';
    const tokenUrl = `${identityServerUrl}/connect/token`;

    const payload = {
        client_id: 'loadtest',
        client_secret: 'secret',
        grant_type: 'password',
        username: 'alice',
        password: 'Pass123$',
        scope: 'openid profile orders'
    };

    const params = {
        headers: {
            'Content-Type': 'application/x-www-form-urlencoded',
        },
    };

    const tokenResponse = http.post(tokenUrl, payload, params);

    const tokenSuccess = check(tokenResponse, {
        'token request successful': (r) => r.status === 200,
        'has access token': (r) => r.json('access_token') !== undefined,
    });

    if (!tokenSuccess) {
        throw new Error('Failed to get authentication token. Please check if Identity service is running.');
    }

    console.log('Successfully obtained authentication token');

    return {
        token: tokenResponse.json('access_token'),
        userName: payload.username,
    };
}

// Define the error scenarios we'll cycle through
const errorScenarios = [
    'negative_price',
    'expired_card',
    'empty_card_number',
    'missing_fields',
    'zero_products',
];

// Generate invalid order items based on error type
function generateInvalidOrderItems(errorType) {
    switch(errorType) {
        case 'negative_price':
            return [{
                productId: randomIntBetween(1, 100),
                productName: 'Negative Price Product',
                unitPrice: -50, // Negative price to cause validation failure
                quantity: randomIntBetween(1, 5),
                pictureUrl: `https://example.com/products/${randomIntBetween(1, 100)}.jpg`
            }];

        case 'zero_products':
            return []; // Empty order

        default:
            return [{
                productId: randomIntBetween(1, 100),
                productName: 'Test Product',
                unitPrice: randomIntBetween(10, 100),
                quantity: randomIntBetween(1, 5),
                pictureUrl: `https://example.com/products/${randomIntBetween(1, 100)}.jpg`
            }];
    }
}

// Get invalid card data
function getInvalidCardData(errorType) {
    if (errorType === 'expired_card') {
        // Card expired in the past
        return new Date(new Date().getFullYear() - 1, randomIntBetween(1, 12) - 1, 1).toISOString();
    } else {
        // Valid date for comparison (next year)
        return new Date(new Date().getFullYear() + 1, randomIntBetween(1, 12) - 1, 1).toISOString();
    }
}

export default function(data) {
    // Get which error scenario to test in this iteration (cycle through our list)
    const iterationIndex = __ITER % errorScenarios.length;
    const errorScenario = errorScenarios[iterationIndex];

    const requestId = uuidv4();
    const orderingApiUrl = 'http://localhost:5224/api';

    group(`Order Failure Test ${__ITER + 1} - ${errorScenario}`, function() {
        const orderUrl = `${orderingApiUrl}/orders?api-version=1.0`;

        // Prepare order data with the specific problem for this scenario
        let orderData = {
            userId: data.userName,
            userName: data.userName,
            city: "Seattle",
            street: "123 Test St",
            state: "WA",
            country: "USA",
            zipCode: "98101",
            cardNumber: "4111111111111111",
            cardHolderName: "Test User",
            cardExpiration: getInvalidCardData(errorScenario),
            cardSecurityNumber: "123",
            cardTypeId: 1,
            buyer: data.userName,
            items: generateInvalidOrderItems(errorScenario)
        };

        // Apply additional error scenarios
        if (errorScenario === 'empty_card_number') {
            orderData.cardNumber = "";
        } else if (errorScenario === 'missing_fields') {
            delete orderData.cardNumber;
            delete orderData.cardHolderName;
        }

        let requestHeaders = {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${data.token}`,
            'x-requestid': requestId
        };

        console.log(`Creating order with request ID: ${requestId}, scenario: ${errorScenario}`);

        const startTime = new Date();
        const orderResponse = http.post(
            orderUrl,
            JSON.stringify(orderData),
            {
                headers: requestHeaders
            }
        );
        const endTime = new Date();

        // Track order creation time
        orderCreationTrend.add(endTime - startTime);

        // Check if it failed as expected
        const orderFailedAsExpected = check(orderResponse, {
            'order creation received response': (r) => r.status !== 0,
            'order creation failed as expected': (r) => r.status >= 400 || r.status < 200,
        });

        if (orderFailedAsExpected) {
            console.log(`✓ Order failed as expected with status ${orderResponse.status}`);
            failRate.add(true); // We count success when it fails (this is an error test)
        } else {
            console.log(`✗ Expected failure but got status ${orderResponse.status}`);
            failRate.add(false);
        }
    });

    // Small pause between iterations
    sleep(1);
}

export function handleSummary(data) {
    return {
        stdout: JSON.stringify({
            'simple-failure-test': {
                description: 'Testing order failure metrics',
                iterations: 5,
                metrics: {
                    failed_requests: data.metrics.failed_requests,
                    order_creation_time: data.metrics.order_creation_time
                },
                note: "Check your Prometheus/Grafana dashboard for 'orders.failed' metric"
            }
        }, null, 2),
    };
}
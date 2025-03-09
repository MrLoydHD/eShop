import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend } from 'k6/metrics';
import { randomString, randomIntBetween } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

// Custom metrics
const failRate = new Rate('failed_requests');
const orderCreationTrend = new Trend('order_creation_time');
const failuresTrend = new Trend('order_failures');

export const options = {
    scenarios: {
        order_failure_tests: {
            executor: 'ramping-vus',
            startVUs: 1,
            stages: [
                { duration: '10s', target: 10 },
                { duration: '20s', target: 20 },
                { duration: '10s', target: 0 },
                {duration: '20s', target: 10 },
            ],
            gracefulRampDown: '5s',
        },
    },
    thresholds: {
        'failed_requests': ['rate>0.5'], // We expect a high failure rate in this test
        'order_creation_time': ['p(95)<3000'],
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

    // Check if ordering API is available
    const orderingApiUrl = 'http://localhost:5224/api';
    const orderingResponse = http.get(`${orderingApiUrl}/orders?PageSize=1&PageIndex=0&api-version=1.0`, {
        headers: {
            'Authorization': `Bearer ${tokenResponse.json('access_token')}`,
        },
    });

    const orderingSuccess = check(orderingResponse, {
        'Ordering API is reachable': (r) => r.status === 200 || r.status === 401,
    });

    if (!orderingSuccess) {
        throw new Error('Cannot reach Ordering API. Please check if the Ordering service is running.');
    }

    return {
        token: tokenResponse.json('access_token'),
        userName: payload.username,
    };
}

// Generate invalid order items to force errors
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

        case 'excessive_quantity':
            return [{
                productId: randomIntBetween(1, 100),
                productName: 'Excessive Quantity Product',
                unitPrice: randomIntBetween(10, 100),
                quantity: 99999999, // Unreasonably large quantity
                pictureUrl: `https://example.com/products/${randomIntBetween(1, 100)}.jpg`
            }];

        case 'zero_products':
            return []; // Empty order

        case 'invalid_product_id':
            return [{
                productId: -1, // Invalid product ID
                productName: 'Invalid Product',
                unitPrice: randomIntBetween(10, 100),
                quantity: randomIntBetween(1, 5),
                pictureUrl: `https://example.com/products/${randomIntBetween(1, 100)}.jpg`
            }];

        default:
            return [{
                productId: randomIntBetween(1, 100),
                productName: `Test Product ${randomString(8)}`,
                unitPrice: randomIntBetween(10, 100),
                quantity: randomIntBetween(1, 5),
                pictureUrl: `https://example.com/products/${randomIntBetween(1, 100)}.jpg`
            }];
    }
}

// Generate invalid card data to cause payment failures
function getInvalidCardData(errorType) {
    switch(errorType) {
        case 'expired_card':
            // Card expired in the past
            return new Date(new Date().getFullYear() - 1, randomIntBetween(1, 12) - 1, 1).toISOString();

        case 'invalid_date_format':
            // Invalid date format
            return "12/25"; // Wrong format for API

        case 'far_future':
            // Date too far in the future
            return new Date(new Date().getFullYear() + 20, randomIntBetween(1, 12) - 1, 1).toISOString();

        default:
            // Valid date for comparison (next year)
            return new Date(new Date().getFullYear() + 1, randomIntBetween(1, 12) - 1, 1).toISOString();
    }
}

export default function(data) {
    // Generate unique request ID as a GUID/UUID for tracking
    const requestId = uuidv4();
    const orderingApiUrl = 'http://localhost:5224/api';

    // Determine which error scenario to test in this iteration
    const errorScenarios = [
        'negative_price',
        'excessive_quantity',
        'zero_products',
        'invalid_product_id',
        'expired_card',
        'invalid_date_format',
        'far_future',
        'empty_card_number',
        'invalid_request_id',
        'missing_fields'
    ];

    const errorScenario = errorScenarios[Math.floor(Math.random() * errorScenarios.length)];

    group(`Order Creation Failure Test - ${errorScenario}`, function() {
        const orderUrl = `${orderingApiUrl}/orders?api-version=1.0`;

        // Prepare order data with intentionally problematic data
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

        // Apply additional error scenarios that don't fit neatly into the functions
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

        // Use invalid request ID for specific test
        if (errorScenario === 'invalid_request_id') {
            requestHeaders['x-requestid'] = 'not-a-valid-guid';
        }

        console.log(`Creating order with request ID: ${requestId} and error scenario: ${errorScenario}`);

        const startOrderTime = new Date();
        const orderResponse = http.post(
            orderUrl,
            JSON.stringify(orderData),
            {
                headers: requestHeaders
            }
        );
        const endOrderTime = new Date();

        // Track order creation performance
        orderCreationTrend.add(endOrderTime - startOrderTime);

        // We expect this to fail, so we're checking if it properly fails
        const orderFailedAsExpected = check(orderResponse, {
            'order creation response received': (r) => r.status !== 0,
            'order creation failed as expected': (r) => r.status >= 400 || r.status < 200,
        });

        if (orderFailedAsExpected) {
            console.log(`Order creation failed as expected with status ${orderResponse.status} for scenario ${errorScenario}`);
            failuresTrend.add(1); // Record a successful failure test
        } else {
            console.log(`Unexpected success with status ${orderResponse.status} for scenario ${errorScenario}`);
            failRate.add(true); // This is actually a failure of our test
        }

        // After intentional failure, check metrics endpoint to see if failures are being recorded
        sleep(1); // Give a second for metrics to be updated

        // Optional: Query Prometheus metrics endpoint if accessible from K6
        // This part might not work directly from K6 depending on your environment
        // You might want to check Prometheus/Grafana manually after running this test
        try {
            const metricsUrl = 'http://localhost:9090/api/v1/query?query=orders_failed_total';
            const metricsResponse = http.get(metricsUrl);

            if (metricsResponse.status === 200) {
                console.log(`Prometheus metrics response: ${metricsResponse.body}`);
            }
        } catch (e) {
            console.log("Could not query Prometheus directly, check dashboard manually");
        }
    });

    // Short pause between test iterations
    sleep(1);
}

// Summary handler for better reporting
export function handleSummary(data) {
    return {
        'stdout': JSON.stringify({
            metrics: {
                order_creation_time: data.metrics.order_creation_time,
                failed_requests: data.metrics.failed_requests,
                order_failures: data.metrics.failuresTrend
            },
            error_tests: {
                description: "This test was designed to generate failures to verify error metrics collection",
                note: "Check your Prometheus/Grafana dashboard for 'orders_failed_total' metric"
            }
        }, null, 2),
    };
}
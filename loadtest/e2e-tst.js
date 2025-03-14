import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend } from 'k6/metrics';
import { randomString, randomIntBetween } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

const failRate = new Rate('failed_requests');
const pageLoadTrend = new Trend('page_load_time');
const detailsLoadTrend = new Trend('details_load_time');
const checkoutTrend = new Trend('checkout_time');
const orderCreationTrend = new Trend('order_creation_time');

export const options = {
    scenarios: {
        ui_browsing_flow: {
            executor: 'ramping-vus',
            startVUs: 1,
            stages: [
                { duration: '3600s', target: 50 },
                { duration: '1m', target: 25 },
                { duration: '30s', target: 50 },
                { duration: '1m', target: 50 },
                { duration: '30s', target: 0 },
            ],
            gracefulRampDown: '10s',
        },
    },
    thresholds: {
        http_req_duration: ['p(95)<800'],
        'failed_requests': ['rate<0.1'],
        'page_load_time': ['p(95)<1000'],
        'details_load_time': ['p(95)<600'],
        'checkout_time': ['p(95)<2000'],
        'order_creation_time': ['p(95)<2000'],
    },
    insecureSkipTLSVerify: true,
    http2: true,
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

    const webAppUrl = 'https://localhost:7298';
    const webResponse = http.get(webAppUrl);

    const webSuccess = check(webResponse, {
        'Web UI is reachable': (r) => r.status === 200,
    });

    if (!webSuccess) {
        throw new Error('Cannot reach Web UI. Please check if the Web application is running.');
    }

    const catalogApiUrl = 'http://localhost:5222/api/catalog';
    const catalogResponse = http.get(`${catalogApiUrl}/items?PageSize=1&PageIndex=0&api-version=1.0`);

    const catalogSuccess = check(catalogResponse, {
        'Catalog API is reachable': (r) => r.status === 200,
    });

    if (!catalogSuccess) {
        throw new Error('Cannot reach Catalog API. Please check if the Catalog service is running.');
    }

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

// Generate items for basket and orders
function generateOrderItems(count) {
    const items = [];
    for (let i = 0; i < count; i++) {
        items.push({
            productId: randomIntBetween(1, 100),
            productName: `Test Product ${randomString(8)}`,
            unitPrice: randomIntBetween(10, 100),
            quantity: randomIntBetween(1, 5),
            pictureUrl: `https://example.com/products/${randomIntBetween(1, 100)}.jpg`
        });
    }
    return items;
}

// Generate a proper datetime format for card expiration
function getCardExpirationDate() {
    const year = new Date().getFullYear() + 1;  // Next year
    const month = randomIntBetween(1, 12);
    // Format: 2025-03-08T00:00:00.000Z (ISO format)
    return new Date(year, month - 1, 1).toISOString();
}

export default function(data) {
    // Generate unique request ID as a GUID/UUID for tracking
    const requestId = uuidv4();

    const webAppUrl = 'https://localhost:7298';
    const catalogApiUrl = 'http://localhost:5222/api/catalog';
    const basketApiUrl = 'http://localhost:5224/api/basket';
    const orderingApiUrl = 'http://localhost:5224/api';

    const authHeaders = {
        'Authorization': `Bearer ${data.token}`,
        'Content-Type': 'application/json',
    };

    group('Browse Catalog', function() {
        const startHomeTime = new Date();
        const homeResponse = http.get(webAppUrl, {
            headers: {
                'Accept': 'text/html',
            }
        });

        const homeSuccess = check(homeResponse, {
            'home page loaded successfully': (r) => r.status === 200,
        });

        failRate.add(!homeSuccess);
        const endHomeTime = new Date();
        pageLoadTrend.add(endHomeTime - startHomeTime);

        const startBrowsingTime = new Date();
        const itemsResponse = http.get(`${catalogApiUrl}/items?PageSize=12&PageIndex=0&api-version=1.0`);

        const browsingSuccess = check(itemsResponse, {
            'catalog browsing successful': (r) => r.status === 200,
            'catalog has items': (r) => r.json().data && r.json().data.length > 0,
        });

        failRate.add(!browsingSuccess);

        const endBrowsingTime = new Date();
        pageLoadTrend.add(endBrowsingTime - startBrowsingTime);

        if (browsingSuccess && itemsResponse.json().data.length > 0) {
            const items = itemsResponse.json().data;
            const randomIndex = Math.floor(Math.random() * items.length);
            const item = items[randomIndex];

            const startDetailTime = new Date();
            const itemDetailResponse = http.get(`${catalogApiUrl}/items/${item.id}?api-version=1.0`);
            const endDetailTime = new Date();

            detailsLoadTrend.add(endDetailTime - startDetailTime);

            const itemDetailSuccess = check(itemDetailResponse, {
                'item detail loaded successfully': (r) => r.status === 200,
                'item has name': (r) => r.json().name !== undefined,
            });

            failRate.add(!itemDetailSuccess);

            const brandsResponse = http.get(`${catalogApiUrl}/catalogbrands?api-version=1.0`);
            const typesResponse = http.get(`${catalogApiUrl}/catalogtypes?api-version=1.0`);

            check(brandsResponse, {
                'brands loaded successfully': (r) => r.status === 200,
                'has brands': (r) => Array.isArray(r.json()),
            });

            check(typesResponse, {
                'types loaded successfully': (r) => r.status === 200,
                'has types': (r) => Array.isArray(r.json()),
            });
        }
    });

    // Small pause to simulate user browsing and decision making
    sleep(randomIntBetween(1, 3));

    // Checkout process and order creation
    group('Checkout and Order Creation', function() {
        // 1. First trigger the checkout process
        const checkoutUrl = `${webAppUrl}/checkout`;

        // Create a payload similar to what the checkout would receive
        const checkoutData = {
            requestId: requestId,
            userId: data.userName,
            userName: data.userName,
            items: generateOrderItems(randomIntBetween(1, 5))
        };

        const checkoutHeaders = {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${data.token}`,
            'x-requestid': requestId
        };

        console.log(`Initiating checkout with request ID: ${requestId}`);

        const startCheckoutTime = new Date();
        const checkoutResponse = http.post(
            checkoutUrl,
            JSON.stringify(checkoutData),
            {
                headers: checkoutHeaders
            }
        );
        const endCheckoutTime = new Date();

        // Track checkout performance
        checkoutTrend.add(endCheckoutTime - startCheckoutTime);

        const checkoutSuccess = check(checkoutResponse, {
            'checkout response received': (r) => r.status !== 0,
            'checkout process started': (r) => r.status >= 200 && r.status < 400,
        });

        failRate.add(!checkoutSuccess);

        if (checkoutSuccess) {
            console.log(`Checkout process started successfully in ${endCheckoutTime - startCheckoutTime}ms`);

            // 2. Now create the actual order
            const orderUrl = `${orderingApiUrl}/orders?api-version=1.0`;

            // Prepare order data with payment info and proper DateTime format for card expiration
            const orderData = {
                userId: data.userName,
                userName: data.userName,
                city: "Seattle",
                street: "123 Test St",
                state: "WA",
                country: "USA",
                zipCode: "98101",
                cardNumber: "4111111111111111",
                cardHolderName: "Test User",
                cardExpiration: getCardExpirationDate(), // Use ISO date format
                cardSecurityNumber: "123",
                cardTypeId: 1,
                buyer: data.userName,
                items: checkoutData.items  // Use the same items from checkout
            };

            const orderHeaders = {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${data.token}`,
                'x-requestid': requestId  // Use the same request ID for correlation
            };

            console.log(`Creating order with request ID: ${requestId}`);
            console.log(`Using card expiration date: ${orderData.cardExpiration}`);

            const startOrderTime = new Date();
            const orderResponse = http.post(
                orderUrl,
                JSON.stringify(orderData),
                {
                    headers: orderHeaders
                }
            );
            const endOrderTime = new Date();

            // Track order creation performance
            orderCreationTrend.add(endOrderTime - startOrderTime);

            const orderSuccess = check(orderResponse, {
                'order creation response received': (r) => r.status !== 0,
                'order created successfully': (r) => r.status >= 200 && r.status < 400,
            });

            failRate.add(!orderSuccess);

            if (orderSuccess) {
                console.log(`Order created successfully in ${endOrderTime - startOrderTime}ms`);

                // 3. Verify order status (optional)
                const ordersUrl = `${orderingApiUrl}/orders?PageSize=10&PageIndex=0&api-version=1.0`;
                const ordersResponse = http.get(ordersUrl, {
                    headers: authHeaders
                });

                check(ordersResponse, {
                    'order history loaded': (r) => r.status === 200,
                });
            } else {
                console.log(`Order creation failed with status ${orderResponse.status}: ${orderResponse.body}`);
            }
        } else {
            console.log(`Checkout failed with status ${checkoutResponse.status}: ${checkoutResponse.body}`);
        }
    });

    // Wait between iterations
    sleep(2);
}

// Optional: Add a summary handler for better reporting
export function handleSummary(data) {
    return {
        'stdout': JSON.stringify({
            metrics: {
                page_load_time: data.metrics.page_load_time,
                details_load_time: data.metrics.details_load_time,
                checkout_time: data.metrics.checkout_time,
                order_creation_time: data.metrics.order_creation_time,
                failed_requests: data.metrics.failed_requests,
                http_req_duration: data.metrics.http_req_duration
            }
        }, null, 2),
    };
}
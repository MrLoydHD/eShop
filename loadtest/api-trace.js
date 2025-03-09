import http from 'k6/http';
import { check, sleep } from 'k6';

// This script traces API calls to help understand the exact endpoints
export const options = {
    vus: 1,
    iterations: 1,
    insecureSkipTLSVerify: true,
};

// Helper function to make a request and print details
function traceRequest(method, url, body = null, headers = {}) {
    console.log(`\n[TRACE] ${method} ${url}`);
    console.log(`[HEADERS] ${JSON.stringify(headers)}`);

    if (body) {
        console.log(`[BODY] ${typeof body === 'string' ? body : JSON.stringify(body)}`);
    }

    let response;
    if (method.toUpperCase() === 'GET') {
        response = http.get(url, { headers });
    } else if (method.toUpperCase() === 'POST') {
        response = http.post(url, body, { headers });
    } else if (method.toUpperCase() === 'PUT') {
        response = http.put(url, body, { headers });
    }

    console.log(`[RESPONSE] Status: ${response.status}`);
    console.log(`[RESPONSE HEADERS] ${JSON.stringify(response.headers)}`);

    try {
        const responseBody = response.json();
        console.log(`[RESPONSE BODY] ${JSON.stringify(responseBody).substring(0, 500)}${JSON.stringify(responseBody).length > 500 ? '...' : ''}`);
    } catch (e) {
        console.log(`[RESPONSE BODY] ${response.body.substring(0, 500)}${response.body.length > 500 ? '...' : ''}`);
    }

    return response;
}

// Main function to trace API endpoints
export default function() {
    console.log("\n=== STARTING API TRACING ===");

    // 1. Check identity server endpoints
    console.log("\n=== IDENTITY SERVER ENDPOINTS ===");
    const identityServerUrl = 'https://localhost:5243';
    traceRequest('GET', `${identityServerUrl}/.well-known/openid-configuration`);

    // 2. Check catalog API endpoints
    console.log("\n=== CATALOG API ENDPOINTS ===");
    const catalogApiUrl = 'http://localhost:5222/api/catalog';
    traceRequest('GET', `${catalogApiUrl}/items?PageSize=1&PageIndex=0&api-version=1.0`);

    // 3. Check ordering API endpoints
    console.log("\n=== ORDERING API ENDPOINTS ===");
    const orderingApiUrl = 'http://localhost:5224/api/orders';
    traceRequest('GET', orderingApiUrl);

    // 4. Trace a Client Credentials token request
    console.log("\n=== CLIENT CREDENTIALS TOKEN REQUEST ===");
    const tokenUrl = `${identityServerUrl}/connect/token`;
    const clientCredentialsPayload = {
        client_id: 'orderingswaggerui',
        client_secret: 'secret',
        grant_type: 'client_credentials',
        scope: 'orders'
    };
    const tokenHeaders = {
        'Content-Type': 'application/x-www-form-urlencoded',
    };
    const tokenResponse = traceRequest('POST', tokenUrl, clientCredentialsPayload, tokenHeaders);

    let token = null;
    try {
        token = tokenResponse.json('access_token');
    } catch (e) {
        console.log("Failed to obtain token, skipping authenticated requests");
    }

    if (token) {
        // 5. Trace a sample order creation request
        console.log("\n=== ORDER CREATION REQUEST ===");
        const requestId = Math.random().toString(36).substring(2, 15);

        const orderData = {
            userId: "test-user",
            userName: "Test User",
            city: "Seattle",
            street: "123 Test St",
            state: "WA",
            country: "USA",
            zipCode: "98101",
            cardNumber: "4111111111111111",
            cardHolderName: "Test User",
            cardExpiration: "12/2025",
            cardSecurityNumber: "123",
            cardTypeId: 1,
            buyer: "test-user",
            items: [
                {
                    productId: 1,
                    productName: "Test Product",
                    unitPrice: 10,
                    quantity: 1,
                    pictureUrl: "https://test.com/product.jpg"
                }
            ]
        };

        const orderHeaders = {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json',
            'x-requestid': requestId
        };

        traceRequest('POST', orderingApiUrl, JSON.stringify(orderData), orderHeaders);

        // 6. Trace an order retrieval request
        console.log("\n=== ORDER RETRIEVAL REQUEST ===");
        traceRequest('GET', orderingApiUrl, null, {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json'
        });
    }

    console.log("\n=== API TRACING COMPLETE ===");
}
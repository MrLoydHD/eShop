import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend } from 'k6/metrics';
import { randomString } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';

// Custom metrics
const failRate = new Rate('failed_requests');
const tokenObtainTime = new Trend('token_obtain_time');

export const options = {
    scenarios: {
        login_performance: {
            executor: 'ramping-vus',
            startVUs: 1,
            stages: [
                { duration: '30s', target: 5 },
                { duration: '1m', target: 5 },
                { duration: '30s', target: 20 },
                { duration: '1m', target: 20 },
                { duration: '30s', target: 0 },
            ],
            gracefulRampDown: '10s',
        },
    },
    thresholds: {
        http_req_duration: ['p(95)<500'],
        'token_obtain_time': ['p(95)<400'],
        'failed_requests': ['rate<0.1'],
    },
    insecureSkipTLSVerify: true,
};

export default function() {
    // Authentication step
    group('Authentication', function() {
        const tokenStartTime = new Date().getTime();
        const requestId = randomString(16); // Generate unique request ID for telemetry

        const identityServerUrl = 'https://localhost:5243';
        const clientId = 'loadtest';
        const clientSecret = 'secret';
        const username = 'alice';
        const password = 'Pass123$';
        const tokenUrl = `${identityServerUrl}/connect/token`;

        const payload = {
            client_id: clientId,
            client_secret: clientSecret,
            grant_type: 'password',
            username: username,
            password: password,
            scope: 'openid profile orders basket'
        };

        const params = {
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'x-requestid': requestId
            },
            tags: { name: 'token_request' }
        };

        const response = http.post(tokenUrl, payload, params);

        // Calculate and record token acquisition time
        const tokenEndTime = new Date().getTime();
        tokenObtainTime.add(tokenEndTime - tokenStartTime);

        const success = check(response, {
            'status is 200': (r) => r.status === 200,
            'has access token': (r) => r.json('access_token') !== undefined,
        });

        failRate.add(!success);

        if (success) {
            // Uncomment and modify this section when you're ready to add API calls
            /*
            const token = response.json('access_token');
            const apiResponse = http.get(`${apiUrl}/some-endpoint`, {
              headers: {
                Authorization: `Bearer ${token}`,
                'x-requestid': requestId
              },
            });
            
            check(apiResponse, {
              'API request successful': (r) => r.status === 200,
            });
            */
        }
    });

    // Wait between iterations
    sleep(1);
}

export function setup() {
    console.log("Starting setup - checking Identity Server availability");
    const identityServerUrl = 'https://localhost:5243';
    const res = http.get(`${identityServerUrl}/.well-known/openid-configuration`);

    const success = check(res, {
        'Identity Server is reachable': (r) => r.status === 200,
    });

    if (!success) {
        throw new Error('Cannot reach Identity Server. Please check the URL and try again.');
    }

    console.log("Identity Server is available, proceeding with test");
    return {};
}
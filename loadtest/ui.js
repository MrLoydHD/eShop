import http from 'k6/http';
import { check, sleep } from 'k6';

// Configuration to ignore SSL certificate validation
const params = {
  insecureSkipTLSVerify: true,
};

// Test configuration
export const options = {
  stages: [
    { duration: '30s', target: 50 }, // Ramp up to 10 virtual users over 30 seconds
    { duration: '1m', target: 50 },  // Stay at 10 virtual users for 1 minute
    { duration: '30s', target: 0 },  // Ramp down to 0 users over 30 seconds
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'], // 95% of requests should be below 500ms
    http_req_failed: ['rate<0.01'],   // Less than 1% of requests should fail
  },
};

// Main function executed by each virtual user
export default function() {
  // Main page request
  const mainPageRes = http.get('https://localhost:7298/', params);
  
  check(mainPageRes, {
    'status is 200': (r) => r.status === 200,
    'page contains expected content': (r) => r.body.includes('html'),
  });
  
  // Add more requests to test specific pages or endpoints
  // const loginPageRes = http.get('https://localhost:7298/login', params);
  // const apiRes = http.get('https://localhost:7298/api/data', params);
  
  // Sleep between iterations to simulate real user behavior
  sleep(Math.random() * 3 + 1); // Random sleep between 1-4 seconds
}
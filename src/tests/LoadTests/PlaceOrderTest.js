import http from 'k6/http';
import { check, sleep } from 'k6';
import { randomString } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';

// Test configuration
export const options = {
  stages: [
    { duration: '30s', target: 5 },  // Ramp up to 5 users
    { duration: '1m', target: 5 },   // Stay at 5 users for 1 minute
    { duration: '30s', target: 0 }   // Ramp down to 0 users
  ],
  thresholds: {
    http_req_duration: ['p(95)<1500'], // 95% of requests should be below 1.5s
    'checks': ['rate>0.9'],           // 90% of checks should pass
  }
};

// Base URLs - adjust these based on your deployment
const BASE_URL = 'http://localhost:5100'; // WebApp
const IDENTITY_URL = 'http://localhost:5105'; // Identity API
const BASKET_URL = 'http://localhost:5103'; // Basket API
const CATALOG_URL = 'http://localhost:5101'; // Catalog API
const ORDERING_URL = 'http://localhost:5102'; // Ordering API

// Test data
const TEST_USER = {
  email: 'demouser@microsoft.com',
  password: 'Pass@word1'
};

// Sample product IDs to add to cart (you might need to adjust these based on your catalog)
const PRODUCT_IDS = [1, 2, 3, 4, 5];

// Sample payment info
function getPaymentInfo() {
  return {
    cardNumber: '4012888888881881', // Test credit card number
    cardHolderName: 'Demo User',
    cardExpiration: '12/2030',
    cardSecurityNumber: '123',
    cardTypeId: 1, // Visa
    city: 'Seattle',
    country: 'USA',
    state: 'WA',
    street: '123 Main St',
    zipCode: '98101'
  };
}

// Main test flow
export default function() {
  // 1. Login to get auth token
  let loginRes = login();
  if (!loginRes.success) {
    console.error('Login failed, skipping this iteration');
    sleep(1);
    return;
  }
  
  let token = loginRes.token;
  let userId = loginRes.userId;
  
  // 2. Get products from catalog
  let products = getProducts();
  if (!products || products.length === 0) {
    console.error('Failed to get products, skipping this iteration');
    sleep(1);
    return;
  }
  
  // 3. Create/Get basket
  let basketId = createBasket(token, userId);
  
  // 4. Add items to basket
  let addedItems = addItemsToBasket(token, basketId, products);
  
  // 5. Checkout (Create order)
  let orderCreated = checkout(token, basketId, userId, addedItems, getPaymentInfo());
  
  // Wait between iterations
  sleep(3);
}

// Helper functions
function login() {
  const url = `${IDENTITY_URL}/connect/token`;
  const payload = {
    username: TEST_USER.email,
    password: TEST_USER.password,
    grant_type: 'password',
    scope: 'openid profile basket ordering'
  };
  
  const params = {
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
    },
  };
  
  let res = http.post(url, payload, params);
  
  let success = check(res, {
    'login status is 200': (r) => r.status === 200,
    'has access token': (r) => r.json('access_token') !== '',
  });
  
  if (!success) {
    return { success: false };
  }
  
  // Extract user ID from the token (this would need proper JWT parsing in a real scenario)
  let userId = TEST_USER.email; // simplified for the test
  
  return { 
    success: true,
    token: res.json('access_token'),
    userId: userId
  };
}

function getProducts() {
  const url = `${CATALOG_URL}/api/v1/catalog/items?pageSize=10`;
  
  let res = http.get(url);
  
  let success = check(res, {
    'get products status is 200': (r) => r.status === 200,
    'products returned': (r) => r.json('data').length > 0,
  });
  
  if (!success) {
    return null;
  }
  
  return res.json('data');
}

function createBasket(token, userId) {
  const basketId = userId;
  const url = `${BASKET_URL}/api/v1/basket/${basketId}`;
  
  const headers = {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${token}`
  };
  
  // Check if basket exists or create a new one
  let res = http.get(url, { headers });
  
  if (res.status !== 200) {
    // Create a new basket
    res = http.post(url, JSON.stringify({ customerId: userId }), { headers });
    
    check(res, {
      'create basket status is 200': (r) => r.status === 200
    });
  }
  
  return basketId;
}

function addItemsToBasket(token, basketId, products) {
  const url = `${BASKET_URL}/api/v1/basket/${basketId}/items`;
  
  const headers = {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${token}`
  };
  
  // Select 1-3 random products
  const numItems = Math.floor(Math.random() * 3) + 1;
  let selectedProducts = [];
  let addedItems = [];
  
  for (let i = 0; i < numItems; i++) {
    if (products.length > 0) {
      const randomIndex = Math.floor(Math.random() * products.length);
      const product = products[randomIndex];
      
      // Remove to avoid duplicates
      products.splice(randomIndex, 1);
      
      selectedProducts.push(product);
    }
  }
  
  // Add each product to basket
  for (const product of selectedProducts) {
    const item = {
      productId: product.id,
      productName: product.name,
      unitPrice: product.price,
      quantity: Math.floor(Math.random() * 3) + 1, // 1-3 items
      pictureUrl: product.pictureUri
    };
    
    let res = http.post(url, JSON.stringify(item), { headers });
    
    check(res, {
      'add item to basket status is 200': (r) => r.status === 200
    });
    
    addedItems.push(item);
  }
  
  return addedItems;
}

function checkout(token, basketId, userId, items, paymentInfo) {
  // 1. Get basket for checkout
  const basketUrl = `${BASKET_URL}/api/v1/basket/${basketId}`;
  
  const headers = {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${token}`,
    'x-requestid': randomString(32) // Generate a unique request ID
  };
  
  let basketRes = http.get(basketUrl, { headers });
  
  check(basketRes, {
    'get basket for checkout status is 200': (r) => r.status === 200
  });
  
  // 2. Create order
  const orderUrl = `${ORDERING_URL}/api/orders`;
  
  const orderData = {
    userId: userId,
    userName: TEST_USER.email,
    city: paymentInfo.city,
    street: paymentInfo.street,
    state: paymentInfo.state,
    country: paymentInfo.country,
    zipCode: paymentInfo.zipCode,
    cardNumber: paymentInfo.cardNumber,
    cardHolderName: paymentInfo.cardHolderName,
    cardExpiration: paymentInfo.cardExpiration,
    cardSecurityNumber: paymentInfo.cardSecurityNumber,
    cardTypeId: paymentInfo.cardTypeId,
    buyer: userId,
    items: items
  };
  
  let orderRes = http.post(orderUrl, JSON.stringify(orderData), { headers });
  
  return check(orderRes, {
    'create order status is 200': (r) => r.status === 200
  });
}

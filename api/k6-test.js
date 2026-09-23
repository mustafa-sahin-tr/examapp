import http from 'k6/http';
import { sleep } from 'k6';

export let options = {
  vus: 10, // Number of virtual users
  duration: '30s', // Duration of the test
  thresholds: {
    http_req_duration: ['p(95)<200'], // 95% of requests should be below 200ms
  },
};

export default function () {
    const url = 'http://localhost:5079/api/worksheet/list?search=&pageNumber=1&pageSize=10'; // Replace with your API endpoint
    
    
    // Issue #238: token'ı ortam değişkeninden al (k6 run --env TOKEN=... k6-test.js),
    // sabit/uzun ömürlü bir JWT asla commit'e girmez.
    const params = {
        headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${__ENV.TOKEN}`,
        },
    };
    
    const res = http.get(url, payload, params);
    
    // Check the response status
    if (res.status !== 200) {
        console.error(`Request failed. Status: ${res.status}`);
    }
    
    sleep(1); // Sleep for 1 second between requests
}
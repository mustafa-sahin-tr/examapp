import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate } from 'k6/metrics';

export let options = {
  stages: [
    { duration: '1m', target: 10 }, // Ramp up to 10 users
    { duration: '2m', target: 10 }, // Stay at 10 users for 2 minutes
    { duration: '1m', target: 0 }, // Ramp down to 0 users
  ],
};

export default function () {
  const url = 'http://exam-dotnet-api:5079/api/worksheet/list?search=&pageNumber=1&pageSize=10'; // Replace with your API endpoint
  const params = {
    headers: {
      'Content-Type': 'application/json',
      Authorization: 'Bearer YOUR_TOKEN', // Replace with your actual token
    },
  };

  group('GET request', function () {
    let res = http.get(url, params);
    check(res, {
      'is status 200': (r) => r.status === 200,
      'response time < 200ms': (r) => r.timings.duration < 200,
    });
  });

  sleep(1);
}

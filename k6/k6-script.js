import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate } from 'k6/metrics';

export let options = {
  stages: [
    { duration: '30s', target: 500 }, // Ramp up to 10 users
    { duration: '2m', target: 1500 }, // Stay at 10 users for 2 minutes
    { duration: '1m', target: 0 }, // Ramp down to 0 users
  ],
};

export default function () {
  const url = 'http://exam-dotnet-api:5079/api/worksheet/list?search=&pageNumber=1&pageSize=10'; // Replace with your API endpoint
  const params = {
    headers: {
      'Content-Type': 'application/json',
      Authorization: 'Bearer eyJhbGciOiJSUzI1NiIsInR5cCIgOiAiSldUIiwia2lkIiA6ICIyMUk5UDJzMEpJZGNxM3NhZU9OWTc0U3loQllmdWRYWVRzMkphS2N0X1I0In0.eyJleHAiOjE3NDY2MjA5NjEsImlhdCI6MTc0NjYyMDY2MSwiYXV0aF90aW1lIjoxNzQ2NjIwNjYwLCJqdGkiOiJhMDU5ZGUzNS02ZGQ5LTRlNTMtYjliNi02NWVmNzM5ZjAwYjIiLCJpc3MiOiJodHRwOi8vbG9jYWxob3N0OjU2NzgvcmVhbG1zL2V4YW0tcmVhbG0iLCJhdWQiOiJhY2NvdW50Iiwic3ViIjoiYzBjYzMyMDUtMzU1OS00MmRmLWI2NzItYzYyY2FhNWRlMjlhIiwidHlwIjoiQmVhcmVyIiwiYXpwIjoiZXhhbS1jbGllbnQiLCJzZXNzaW9uX3N0YXRlIjoiZWMxYmQ3NTEtOGVhNi00NzgwLTg1ODEtZjRlZWNjMmIxNWM3IiwiYWNyIjoiMSIsImFsbG93ZWQtb3JpZ2lucyI6WyJodHRwOi8vbG9jYWxob3N0OjU2NzgiXSwicmVhbG1fYWNjZXNzIjp7InJvbGVzIjpbImRlZmF1bHQtcm9sZXMtZXhhbS1yZWFsbSIsIm9mZmxpbmVfYWNjZXNzIiwidW1hX2F1dGhvcml6YXRpb24iLCJTdHVkZW50Il19LCJyZXNvdXJjZV9hY2Nlc3MiOnsiYWNjb3VudCI6eyJyb2xlcyI6WyJtYW5hZ2UtYWNjb3VudCIsIm1hbmFnZS1hY2NvdW50LWxpbmtzIiwidmlldy1wcm9maWxlIl19fSwic2NvcGUiOiJvcGVuaWQgcHJvZmlsZSBlbWFpbCIsInNpZCI6ImVjMWJkNzUxLThlYTYtNDc4MC04NTgxLWY0ZWVjYzJiMTVjNyIsImVtYWlsX3ZlcmlmaWVkIjpmYWxzZSwibmFtZSI6InN0dWRlbnQ5IGFzZCIsInByZWZlcnJlZF91c2VybmFtZSI6InN0dWRlbnQ5QGV4YW0uY29tIiwiZ2l2ZW5fbmFtZSI6InN0dWRlbnQ5IiwiZmFtaWx5X25hbWUiOiJhc2QiLCJlbWFpbCI6InN0dWRlbnQ5QGV4YW0uY29tIn0.iuUixedEEznJwu_JODJXzvzOueM-qLFI3_0sE-zTwQpTMoYYU-Hwne9YrW2DABqkdrg9ORZjTTq40Zt4tmGImX1kuzgaXNTnwoQTeiRjaHS-579x-LMuYwnAfwjBD9ro6CuxmBmVVKM3Q4SFYNqyk-mSSqgIq4DAmu8AyEVGcLvCscXFbM5HNPevAsOgf10Cct_ERXCVM3pAGOYEUP-l72mVDNPh6YtFRy-xhEwpGi7hH4Ly93acJiNBDAaI23SgnPqFXhwbG1uaGcfRlDxybTm8QboCmPbVePDUNZGhGGrNk5dt0xHvR9VwafaxlrSonRJ8rBTlOZzJBxD53T_yOQ', // Replace with your actual token
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

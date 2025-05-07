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
    
    
    const params = {
        headers: {
        'Content-Type': 'application/json',
        'Authorization': 'Bearer eyJhbGciOiJSUzI1NiIsInR5cCIgOiAiSldUIiwia2lkIiA6ICIyMUk5UDJzMEpJZGNxM3NhZU9OWTc0U3loQllmdWRYWVRzMkphS2N0X1I0In0.eyJleHAiOjE3NDY2MTA3NjUsImlhdCI6MTc0NjYxMDQ2NSwiYXV0aF90aW1lIjoxNzQ2NjEwNDY1LCJqdGkiOiJiMWI0MmE0Ni0xZTExLTQwNWUtYTE3Yi02MGMxYTNhYTkxZjgiLCJpc3MiOiJodHRwOi8vbG9jYWxob3N0OjU2NzgvcmVhbG1zL2V4YW0tcmVhbG0iLCJhdWQiOiJhY2NvdW50Iiwic3ViIjoiYzBjYzMyMDUtMzU1OS00MmRmLWI2NzItYzYyY2FhNWRlMjlhIiwidHlwIjoiQmVhcmVyIiwiYXpwIjoiZXhhbS1jbGllbnQiLCJzZXNzaW9uX3N0YXRlIjoiY2EyMTYyY2MtMmZiMi00OGQ4LTg5N2UtZjRmMjlmZTM2OWQ5IiwiYWNyIjoiMSIsImFsbG93ZWQtb3JpZ2lucyI6WyJodHRwOi8vbG9jYWxob3N0OjU2NzgiXSwicmVhbG1fYWNjZXNzIjp7InJvbGVzIjpbImRlZmF1bHQtcm9sZXMtZXhhbS1yZWFsbSIsIm9mZmxpbmVfYWNjZXNzIiwidW1hX2F1dGhvcml6YXRpb24iLCJTdHVkZW50Il19LCJyZXNvdXJjZV9hY2Nlc3MiOnsiYWNjb3VudCI6eyJyb2xlcyI6WyJtYW5hZ2UtYWNjb3VudCIsIm1hbmFnZS1hY2NvdW50LWxpbmtzIiwidmlldy1wcm9maWxlIl19fSwic2NvcGUiOiJvcGVuaWQgcHJvZmlsZSBlbWFpbCIsInNpZCI6ImNhMjE2MmNjLTJmYjItNDhkOC04OTdlLWY0ZjI5ZmUzNjlkOSIsImVtYWlsX3ZlcmlmaWVkIjpmYWxzZSwibmFtZSI6InN0dWRlbnQ5IGFzZCIsInByZWZlcnJlZF91c2VybmFtZSI6InN0dWRlbnQ5QGV4YW0uY29tIiwiZ2l2ZW5fbmFtZSI6InN0dWRlbnQ5IiwiZmFtaWx5X25hbWUiOiJhc2QiLCJlbWFpbCI6InN0dWRlbnQ5QGV4YW0uY29tIn0.V-0qEXjK_tMcNhyxWqX2oBBxIL_dBDLf56oRYSAbKRejE9kzWgNQbUvEf4N7DQuNdKnWg5i2j-EzcL9K4VZKxB9nxSbr6GgdyfPFD8xDSua7EfH1ywY-AhkcfFcX5uz0IqszREDBeRPd1aT25ciwJvQDORFPcpdutwsid7fWZ5XZa_a6ObBy0mGZLMGD2VZaHnvcQl_guSUFB8HkGR9tPWks6L6_u0I3l65tDvOZ9OKQ8H52PXTmpCVp8uo1s3hP_YmrC3HDYa8E-uYqIM7n5gK1vqlAgxX33RsbpQP4MEm2o6jl38G_2u2N2I9HPviV9Gg3nz1JVXHrRUJ5J4Rwyw', // Replace with your actual token
        },
    };
    
    const res = http.get(url, payload, params);
    
    // Check the response status
    if (res.status !== 200) {
        console.error(`Request failed. Status: ${res.status}`);
    }
    
    sleep(1); // Sleep for 1 second between requests
}
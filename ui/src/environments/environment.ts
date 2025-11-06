export const environment = {
  production: false,
  apiUrl: 'http://localhost:5079/api', // 🔹 API Container Port'u
  reportsApiUrl: 'http://127.0.0.1:8006/api',
  worksheetCardTheme: 'enhanced' as const, // 'minimal' | 'standard' | 'enhanced' | 'full'
};

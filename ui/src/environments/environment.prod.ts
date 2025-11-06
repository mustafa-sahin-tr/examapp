export const environment = {
  production: true,
  apiUrl: 'http://exam_dotnet_8_api:8005/api', // 🔹 Production için API adresi
  reportsApiUrl: 'http://exam_reports_api:8006/api',
  worksheetCardTheme: 'standard' as const, // Production'da daha minimal tema
};

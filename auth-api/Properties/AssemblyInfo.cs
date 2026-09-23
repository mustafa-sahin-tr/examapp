using System.Runtime.CompilerServices;

// auth-api csproj'unda GenerateAssemblyInfo=false olduğu için <InternalsVisibleTo> item'ı
// üretilmez; attribute burada elle verilir (#228: StartupConfigDump testleri).
[assembly: InternalsVisibleTo("AuthApi.Tests")]

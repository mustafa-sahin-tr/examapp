# Üçüncü parti skill'ler

Aşağıdaki skill klasörleri bu repo için değil, upstream kaynaklardan **aynen** alınmıştır.
Repo-özel skill'lerimizden (`dotnet-endpoint`, `ef-migration`, `outbox-event`, `angular-feature`, vb.)
farkı: bunlar framework'ün/dilin kendisini derinlemesine öğretir, bu projenin mimarisini bilmez.
Amaç, kendi agent'larımızın (dotnet-api-dev, angular-dev, devops-aspire, test-engineer) güncel resmi/
uzman bilgiyle beslenmesi.

## angular-developer

- **Kaynak:** [angular/skills](https://github.com/angular/skills) — Angular ekibinin (Google) resmi repo'su
- **Lisans:** MIT — `Copyright 2026 Google LLC`
- **İçerik:** Signals, `linkedSignal`, `resource`/`httpResource`, Signal Forms, DI, routing, SSR, ARIA,
  test, CLI — Angular'ın en güncel (v21+) resmi rehberi.
- Kendi `angular-conventions.md` kuralımızla (standalone, Material, CSS token) çakışmaz; onu tamamlar.
  Çakışma çıkarsa **kendi kuralımız önceliklidir** (Material yerine Tailwind örneği gibi bazı bölümler
  bizim stack'imizde geçerli değil).

## efcore-patterns, database-performance, csharp-coding-standards,
## microsoft-extensions-dependency-injection, aspire-integration-testing,
## aspire-configuration, aspire-service-defaults, testcontainers

- **Kaynak:** [Aaronontheweb/dotnet-skills](https://github.com/Aaronontheweb/dotnet-skills)
  (Aaron Stannard — Akka.NET yaratıcısı, Petabridge kurucusu)
- **Lisans:** MIT — `Copyright (c) 2025 Aaron Stannard`
- 30 skill'lik koleksiyondan yalnızca bu projenin stack'iyle (EF Core + PostgreSQL, .NET Aspire, ASP.NET
  Core, xUnit/Testcontainers) doğrudan örtüşenler seçilmiştir; Akka.NET, R3, Blazor/Playwright gibi
  kullanmadığımız parçalar bilinçli olarak alınmadı.

## Güncelleme

Bu klasörler elle kopyalanmıştır, otomatik senkronize olmaz. Upstream'de önemli bir güncelleme
(ör. yeni Angular sürümü, yeni EF Core pattern'i) olduğunda ilgili klasörü yeniden kopyalayıp
`git diff` ile neyin değiştiğine bakmak yeterli.

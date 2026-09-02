---
description: PR açmadan önceki hazırlık kontrol listesini çalıştırır
---

Bu dalı PR'a hazır hale getir. Sırayla:

1. **Derleme**
   - `cd api/ExamApp.Api && dotnet build`
   - `cd ui && npx tsc --noEmit`
2. **Testler** — `test-engineer` agent'ı ile çalıştır, sadece başarısızları raporlasın.
3. **Migration** — Yeni migration var mı? Adı anlamlı mı? Yıkıcı işlem (drop/rename/tip daraltma) içeriyor mu?
   İçeriyorsa ayrıca uyar.
4. **Gateway** — Yeni endpoint eklendiyse `Services/Gateway/ocelot.json` güncellenmiş mi,
   auth ayarı eşdeğer route'larla tutarlı mı?
5. **Config senkronu** — Yeni env değişkeni `.env.example`'a eklenmiş mi? Port değiştiyse
   `docker-compose.yml`, `ocelot.json`, Angular environment ve `local-dev.md` birlikte güncellenmiş mi?
6. **Secret taraması** — Diff'te credential, token veya gerçek connection string var mı?
7. **Güvenlik** — Değişiklik auth, dosya yükleme veya kullanıcı verisine dokunuyorsa
   `security-reviewer` agent'ını çalıştır.

Sonuç: her madde için ✅ / ⚠️ / ❌ ve tek satır **PR'A HAZIR / HAZIR DEĞİL** kararı.
Ardından PR başlığı ve gövdesini (ne değişti, neden, nasıl test edildi, risk) taslak olarak yaz.
Commit veya push yapma.

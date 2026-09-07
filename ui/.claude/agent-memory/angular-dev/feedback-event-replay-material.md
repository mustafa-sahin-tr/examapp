---
name: feedback-event-replay-material
description: ui/ hydration'da withEventReplay() kullanma - Material overlay/datepicker tıklamalarını yutuyor
metadata:
  type: feedback
---

`ui/src/app/app.config.ts` içinde `provideClientHydration()` **withEventReplay() olmadan** kullanılır.

**Why:** Angular/CDK 19.1.x + `withEventReplay()` kombinasyonunda `mat-datepicker-toggle`
gibi Material overlay tetikleyicilerinin ilk tıklaması jsaction replay mekanizmasına
takılıp yutuluyor; takvim popup'ı hiç açılmıyordu (issue #43). Düzeltme CDK 19.2'de,
ama framework tam sürüm yükseltmesi bu repoda ayrı bir iş.

**How to apply:** Hydration ayarına dokunurken `withEventReplay()` geri ekleme. Material
sürümü (core/material/cdk) 19.2+ ve `@angular/core` 19.2+ ile hizalandığında yeniden
değerlendirilebilir.

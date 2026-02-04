# Finance iOS (SwiftUI)

Bu klasör, `finance-app`’in iOS (native) karşılığı için başlangıç iskeletidir.

## Hedef
- Dashboard
- Transaction ekleme
- Her `AssetType` için ayrı “Asset Portfolio” ekranı
- Uygulama açıkken her **15 saniyede** bir fiyatlar ve kur güncellemesi
- İnternet yoksa **en son cache’lenmiş** değerleri gösterme + “Last updated” zamanı

## Veri Kaynakları (finance-api ile aynı)
- Stocks & US Stocks: Google Finance HTML (scrape)
- Funds: TEFAS HTML (scrape)
- Crypto: CoinGecko `simple/price`
- FX Rates (TRY/USD): Frankfurter API

> Not: Scraping kırılgan olabilir (sayfa yapısı değişebilir). Bu iskelet, backend’siz çalışmayı hedefler.

## Kurulum
1. Xcode’da **iOS App (SwiftUI)** projesi oluştur: örn. `FinanceTracker`.
2. Deployment target’ı iOS 17+ seç (SwiftData için).
3. Bu repo içinden `finance-ios/FinanceTracker/` klasörünü Xcode projesine **drag & drop** ederek ekle.
4. Target Membership: eklenen tüm `.swift` dosyaları app target’ında işaretli olmalı.
5. İlk çalıştırma:
   - `Add Asset` ekranından birkaç asset ekle.
   - Uygulama açıkken 15 saniyede bir refresh görürsün.

## Notlar
- Background’da 15 saniyelik refresh iOS tarafından garanti edilmez; sadece foreground’da çalışır.

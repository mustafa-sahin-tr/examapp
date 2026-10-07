/**
 * Ürünün kullanıcıya görünen marka adı (issue #409) — auth-ui'deki tek kaynak.
 *
 * Ana uygulamada (`ui/`) marka adı kök Transloco sözlüğündeki `brand.name` anahtarından gelir
 * (`ui/public/i18n/tr.json` ve `en.json`, ikisinde de aynı değer). auth-ui'de Transloco/i18n altyapısı
 * yok ve metinleri yalnız Türkçe; bu yüzden aynı değer burada tek sabit olarak tutulur. Marka adı
 * değişirse beş yer birlikte güncellenir:
 *  1. `ui/public/i18n/tr.json` ve `en.json` → `brand.name`
 *  2. bu sabit (`BRAND_NAME`)
 *  3. Keycloak login teması `brandName` mesajı
 *     (`deploy/keycloak/keycloak-themes/my-theme/login/messages/messages_{tr,en}.properties`)
 *  4. `deploy/keycloak/dev-import/realm-export.json` → `displayName` (Keycloak e-postaları bunu kullanır)
 *  5. aynı dosyada `smtpServer.fromDisplayName` (e-posta gönderen adı)
 * Çalışan/prod Keycloak'ta 4 ve 5 elle ayarlanır (`.claude/rules/local-dev.md`, `deploy/README.md`).
 *
 * Teknik adlar (namespace, proje, realm/client id, DB adı) bu sabitle ilgili değildir; değişmez.
 */
export const BRAND_NAME = 'Hedef Okul';

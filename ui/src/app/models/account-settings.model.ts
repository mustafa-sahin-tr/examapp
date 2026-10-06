/**
 * Issue #417: Keycloak hesap konsolu (ad/e-posta, şifre değiştirme, oturumlar). Göreli yol bilerek kullanılır —
 * uygulama gateway (localhost:5678) origin'inden sunulur ve gateway `/realms/{everything}` ile `/resources/{everything}`
 * yollarını Keycloak'a iletir; doğrudan Keycloak portuna (8081) gidilmez.
 */
export const KEYCLOAK_ACCOUNT_CONSOLE_PATH = '/realms/exam-realm/account';

/** Ayarlar sayfasında gösterilen birincil rol (çeviri anahtarı `settings.roles.<rol>`). */
export type SettingsRole = 'Admin' | 'Teacher';

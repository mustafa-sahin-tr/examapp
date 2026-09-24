/**
 * Issue #265: dashboard gün sınırlarının (öğretmen aktivite penceresi, admin trend gün kovaları) saat dilimi.
 *
 * Backend bu hesapları Türkiye yerel gününe göre yapar; değer sunucu tarafında `Dashboard:TimeZone`
 * (api/ExamApp.Api/appsettings.json) ile yapılandırılır. Bu sabit ONUNLA AYNI OLMALIDIR — biri değişirse
 * diğeri de değişmeli, aksi hâlde "bugün"/tarih aralığı gece yarısı civarında backend'den bir gün kayar.
 */
export const DASHBOARD_TIME_ZONE = 'Europe/Istanbul';

/** `en-CA` biçimi `yyyy-MM-dd` üretir; parçalar yine de `formatToParts` ile okunur (biçime güvenmeden). */
const isoDayFormatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: DASHBOARD_TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

/**
 * `instant` anının dashboard saat dilimindeki takvim günü, `yyyy-MM-dd` olarak.
 * Tarayıcının saat diliminden bağımsızdır.
 */
export function dashboardIsoDate(instant: Date): string {
  const parts = isoDayFormatter.formatToParts(instant);
  const get = (type: Intl.DateTimeFormatPartTypes): string => parts.find((p) => p.type === type)?.value ?? '';
  return `${get('year')}-${get('month')}-${get('day')}`;
}

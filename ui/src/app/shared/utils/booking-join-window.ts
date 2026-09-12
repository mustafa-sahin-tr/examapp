/**
 * Görüşmeye katılım penceresi (issue #97).
 *
 * Nihai karar backend'e aittir (`Video:JoinWindowBeforeMinutes` / `JoinWindowAfterMinutes`,
 * varsayılan 15/30 dk). Buradaki hesap yalnızca UI'da butonu erken/geç durumda pasifleştirmek
 * ve doğru ipucunu göstermek için; backend 409 dönerse mesajı yine backend'den gösteririz.
 */

/** Ders başlangıcından bu kadar dakika önce oda açılır. */
export const JOIN_WINDOW_BEFORE_MINUTES = 15;

/** Ders bitişinden bu kadar dakika sonra oda kapanır. */
export const JOIN_WINDOW_AFTER_MINUTES = 30;

const MINUTE_MS = 60_000;

/**
 * Listelerde "şimdi"nin tazelenme aralığı. Pencere sınırı dakika çözünürlüklü olduğundan
 * 30 sn yeterli: kullanıcı sayfada beklerken buton kendiliğinden aktifleşir.
 */
export const JOIN_WINDOW_TICK_MS = 30_000;

export type JoinWindowState = 'tooEarly' | 'open' | 'closed' | 'unknown';

export interface JoinWindowInfo {
  state: JoinWindowState;
  canJoin: boolean;
  /** Buton pasifken gösterilecek açıklama; katılıma açıkken null. */
  hint: string | null;
}

/** Randevunun `startUtc`/`endUtc` değerlerine göre katılım penceresi durumu. */
export function getJoinWindow(
  startUtcIso: string,
  endUtcIso: string,
  now: number = Date.now()
): JoinWindowInfo {
  const start = new Date(startUtcIso).getTime();
  const end = new Date(endUtcIso).getTime();

  if (Number.isNaN(start) || Number.isNaN(end)) {
    return { state: 'unknown', canJoin: false, hint: 'Ders saati okunamadı.' };
  }

  if (now < start - JOIN_WINDOW_BEFORE_MINUTES * MINUTE_MS) {
    return {
      state: 'tooEarly',
      canJoin: false,
      hint: `Ders başlamadan ${JOIN_WINDOW_BEFORE_MINUTES} dk önce aktif olur`,
    };
  }

  if (now > end + JOIN_WINDOW_AFTER_MINUTES * MINUTE_MS) {
    return { state: 'closed', canJoin: false, hint: 'Ders süresi doldu' };
  }

  return { state: 'open', canJoin: true, hint: null };
}

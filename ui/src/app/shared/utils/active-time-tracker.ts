/** Issue #572: bu süreden uzun etkileşimsiz aralık sayılmaz (ms). */
export const ACTIVITY_IDLE_MS = 2 * 60 * 1000;
/** Issue #572: etkinlik başına etkin süre üst sınırı (sn); sunucu da aynı tavanı uygular. */
export const ACTIVITY_MAX_ACTIVE_SECONDS = 1800;

export interface ActiveTimeTrackerOptions {
  idleMs?: number;
  capSeconds?: number;
  now?: () => number;
}

/**
 * Issue #572: etkin süre sayacı (saf sınıf, DOM'a bağlı değil). Kural:
 * - Süre yalnız sekme görünürken ve iki etkileşim arası `idleMs`'i aşmıyorsa birikir.
 * - `idleMs`'ten uzun bir boşluk hiç sayılmaz (yalnız son etkileşime kadarki süre sayılmıştır).
 * - Sekme gizlenince o ana kadarki (boşta değilse) süre eklenir, sayım durur; görünür olunca yeniden başlar.
 * - Toplam `capSeconds` ile sınırlıdır.
 */
export class ActiveTimeTracker {
  private readonly idleMs: number;
  private readonly capMs: number;
  private readonly now: () => number;

  private accumulatedMs = 0;
  /** Sayımın son işaretlendiği an (null = duraklatıldı/başlamadı). */
  private markAt: number | null = null;
  private visible = true;

  constructor(options: ActiveTimeTrackerOptions = {}) {
    this.idleMs = options.idleMs ?? ACTIVITY_IDLE_MS;
    this.capMs = (options.capSeconds ?? ACTIVITY_MAX_ACTIVE_SECONDS) * 1000;
    this.now = options.now ?? (() => Date.now());
  }

  /** Sayacı sıfırlar; `seedSeconds` önceki oturumdan gelen süredir. */
  start(seedSeconds = 0, visible = true): void {
    this.accumulatedMs = Math.max(0, Math.min(this.capMs, seedSeconds * 1000));
    this.visible = visible;
    this.markAt = visible ? this.now() : null;
  }

  /** Durdurur (etkinlik bitti); sonraki etkileşimler sayılmaz. */
  stop(): void {
    this.flush();
    this.markAt = null;
    this.visible = false;
  }

  /** Kullanıcı etkileşimi (pointer / klavye / dokunma / kaydırma). */
  activity(): void {
    if (!this.visible) return;
    this.flush();
    this.markAt = this.now();
  }

  setVisible(visible: boolean): void {
    if (visible === this.visible) return;
    if (!visible) {
      this.flush();
      this.markAt = null;
      this.visible = false;
      return;
    }
    this.visible = true;
    this.markAt = this.now();
  }

  /** Şu ana kadarki etkin süre (tam saniye, tavanla sınırlı). Durumu değiştirmez. */
  seconds(): number {
    return Math.floor(Math.min(this.capMs, this.accumulatedMs + this.pendingMs()) / 1000);
  }

  private pendingMs(): number {
    if (this.markAt === null) return 0;
    const gap = this.now() - this.markAt;
    return gap > 0 && gap <= this.idleMs ? gap : 0;
  }

  private flush(): void {
    this.accumulatedMs = Math.min(this.capMs, this.accumulatedMs + this.pendingMs());
    if (this.markAt !== null) this.markAt = this.now();
  }
}

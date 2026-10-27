import { InjectionToken } from '@angular/core';

/** Yazılıp henüz kaydedilmemiş değeri olan etkileşim oynatıcısı: kontrol / bitir / teslim öncesi `flush()` çağrılır. */
export interface FlushablePlayer {
  flush(): void;
}

/**
 * Issue #582: oynatıcıyı sınıfıyla değil bu belirteçle sorgulamak için (`viewChildren(FLUSHABLE_PLAYER)`). Sınıf referansı
 * `@defer` bloğundaki oynatıcıyı ilk pakete çekerdi; belirteç küçük ve bağımsızdır.
 */
export const FLUSHABLE_PLAYER = new InjectionToken<FlushablePlayer>('FLUSHABLE_PLAYER');

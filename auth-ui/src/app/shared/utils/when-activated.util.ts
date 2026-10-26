import { DestroyRef } from '@angular/core';

/**
 * Speculation Rules / Chrome adres çubuğu ön-yüklemesi (prerender) — issue #595.
 *
 * Chrome sık yazılan bir adresi Enter'dan önce arka planda ön-yükler. Ön-yükleme bağlamının sessionStorage'ı
 * sekmeninkinin kopyasıdır ve sayfa etkinleşince atılır; o sırada yazılan kayıt (ör. OIDC `oidc_flow:<state>`)
 * kaybolur. Yan etkili başlangıç işleri (depolamaya yazma, yönlendirme, istek gönderme) sayfa gerçekten
 * etkinleşene kadar bekletilmelidir.
 */
export type PrerenderAwareDocument = Document & { readonly prerendering?: boolean };

export const PRERENDERING_CHANGE_EVENT = 'prerenderingchange';

export function isPrerendering(doc: Document): boolean {
  return (doc as PrerenderAwareDocument).prerendering === true;
}

/**
 * `run`'ı sayfa etkinken hemen (senkron — ön-yükleme yoksa davranış değişmez), ön-yükleme halindeyse
 * `prerenderingchange` olayında **bir kez** çalıştırır. `destroyRef` verilirse komponent etkinleşmeden
 * yok edildiğinde dinleyici kaldırılır ve `run` hiç çalışmaz.
 *
 * Senkron geri çağırma bilinçli: Promise tabanlı bir sürüm ön-yükleme yokken de işi bir mikro göreve ertelerdi.
 */
export function runWhenPageActivated(run: () => void, doc: Document, destroyRef?: DestroyRef): void {
  if (!isPrerendering(doc)) {
    run();
    return;
  }
  // `once` dinleyiciyi ilk olayda kaldırır; destroy etkinleşmeden önce gelirse dinleyiciyi kaldırır.
  const onActivated = (): void => run();
  doc.addEventListener(PRERENDERING_CHANGE_EVENT, onActivated, { once: true });
  destroyRef?.onDestroy(() => doc.removeEventListener(PRERENDERING_CHANGE_EVENT, onActivated));
}

import { DestroyRef } from '@angular/core';

import { PRERENDERING_CHANGE_EVENT, isPrerendering, runWhenPageActivated } from './when-activated.util';

/** Issue #595: ön-yükleme (prerender) halindeki sayfada yan etkili başlangıç etkinleşmeye kadar bekler. */
describe('runWhenPageActivated', () => {
  /** Gerçek `document` yerine olay hedefi + ayarlanabilir `prerendering` taşıyan sahte belge. */
  function fakeDocument(prerendering: boolean | undefined): { doc: Document; activate: () => void } {
    const target = new EventTarget() as EventTarget & { prerendering?: boolean };
    target.prerendering = prerendering;
    return {
      doc: target as unknown as Document,
      activate: () => {
        target.prerendering = false;
        target.dispatchEvent(new Event(PRERENDERING_CHANGE_EVENT));
      },
    };
  }

  function fakeDestroyRef(): { ref: DestroyRef; destroy: () => void } {
    const callbacks: (() => void)[] = [];
    return {
      ref: { onDestroy: (cb: () => void) => (callbacks.push(cb), () => undefined) } as unknown as DestroyRef,
      destroy: () => callbacks.forEach((cb) => cb()),
    };
  }

  it('prerendering yoksa senkron hemen çalıştırır', () => {
    const run = vi.fn();
    runWhenPageActivated(run, fakeDocument(undefined).doc);
    expect(run).toHaveBeenCalledTimes(1);
  });

  it('prerendering=false ise hemen çalıştırır', () => {
    const run = vi.fn();
    runWhenPageActivated(run, fakeDocument(false).doc);
    expect(run).toHaveBeenCalledTimes(1);
  });

  it('ön-yüklemede çalıştırmaz; prerenderingchange sonrası bir kez çalıştırır', () => {
    const run = vi.fn();
    const { doc, activate } = fakeDocument(true);

    runWhenPageActivated(run, doc);
    expect(isPrerendering(doc)).toBe(true);
    expect(run).not.toHaveBeenCalled();

    activate();
    activate();
    expect(run).toHaveBeenCalledTimes(1);
  });

  it('etkinleşmeden önce yok edilirse hiç çalıştırmaz', () => {
    const run = vi.fn();
    const { doc, activate } = fakeDocument(true);
    const destroyRef = fakeDestroyRef();

    runWhenPageActivated(run, doc, destroyRef.ref);
    destroyRef.destroy();
    activate();

    expect(run).not.toHaveBeenCalled();
  });

  it('gerçek tarayıcı belgesi testte ön-yüklemede değildir', () => {
    expect(isPrerendering(document)).toBe(false);
  });
});

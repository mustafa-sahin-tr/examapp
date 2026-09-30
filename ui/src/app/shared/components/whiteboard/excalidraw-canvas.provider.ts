import { Provider } from '@angular/core';

import { WHITEBOARD_CANVAS_LOADER, WhiteboardCanvasMount } from './whiteboard-canvas';

/**
 * Excalidraw font/asset'lerinin uygulamanın kendi sunduğu yolu. `angular.json` `assets` bunları
 * `node_modules/@excalidraw/excalidraw/dist/prod/fonts` altından buraya kopyalar (Xiaolai/CJK hariç).
 */
export const EXCALIDRAW_ASSET_PATH = '/excalidraw-assets/';

/**
 * Excalidraw'ın CSS'i: `angular.json` `styles` içinde `bundleName: "excalidraw"`, `inject: false` — ilk yüklemeye
 * girmez, yalnızca tahta açılınca burada `<link>` ile eklenir. İçindeki font `url()`'leri derleyicide `media/`'ya taşınır.
 */
export const EXCALIDRAW_STYLESHEET_HREF = 'excalidraw.css';

const STYLESHEET_ATTR = 'data-excalidraw-styles';

declare global {
  interface Window {
    /** Excalidraw'ın okuduğu global (font URL tabanı). */
    EXCALIDRAW_ASSET_PATH?: string | string[];
  }
}

/** Excalidraw CSS'ini bir kez ekler; yüklenince (ya da hata verince — tuval yine çalışsın) çözülür. */
export function ensureExcalidrawStylesheet(doc: Document = document): Promise<void> {
  const existing = doc.head.querySelector<HTMLLinkElement>(`link[${STYLESHEET_ATTR}]`);
  if (existing) {
    return existing.dataset['loaded'] === 'true' ? Promise.resolve() : waitForLink(existing);
  }
  const link = doc.createElement('link');
  link.rel = 'stylesheet';
  link.href = EXCALIDRAW_STYLESHEET_HREF;
  link.setAttribute(STYLESHEET_ATTR, '');
  const loaded = waitForLink(link);
  doc.head.appendChild(link);
  return loaded;
}

function waitForLink(link: HTMLLinkElement): Promise<void> {
  return new Promise((resolve) => {
    const done = (): void => {
      link.dataset['loaded'] = 'true';
      resolve();
    };
    link.addEventListener('load', done, { once: true });
    link.addEventListener('error', () => resolve(), { once: true });
  });
}

/**
 * Excalidraw + React'i dinamik `import()` ile ayrı chunk'tan yükler. Asset yolu modül değerlendirilmeden ÖNCE
 * ayarlanır (Excalidraw font URL'lerini bu global'den kurar); CSS paralel yüklenir.
 */
export async function loadExcalidrawCanvas(): Promise<WhiteboardCanvasMount> {
  window.EXCALIDRAW_ASSET_PATH = EXCALIDRAW_ASSET_PATH;
  const [module] = await Promise.all([import('./excalidraw-canvas'), ensureExcalidrawStylesheet()]);
  return module.mountExcalidrawCanvas;
}

/** `app-whiteboard` kullanan sayfaya eklenir (ör. `LessonVideoComponent.providers`). */
export function provideExcalidrawCanvas(): Provider {
  return { provide: WHITEBOARD_CANVAS_LOADER, useValue: loadExcalidrawCanvas };
}

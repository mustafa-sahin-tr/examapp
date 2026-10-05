/**
 * Tahtada Excalidraw'ın hangi yüzeylerinin kapalı olduğu (issue #98, security L1). Excalidraw'ı import etmez; hem
 * tuval (`excalidraw-canvas.ts`) hem testler kullanır.
 *
 * Kütüphane (library) paneli Excalidraw'ın "default" kenar çubuğundadır: "Browse libraries" dış siteye
 * (libraries.excalidraw.com) gider, "Publish" öğeleri dış sunucuya gönderir. Panel şu yollarla açılabilir:
 * sağ üst tetikleyici (`.default-sidebar-trigger`), mobil menü, komut paleti ("Library") ve API. Tetikleyiciler CSS ile
 * gizlenir (`whiteboard.component.scss`); hangi yoldan açılırsa açılsın `onChange` bu kuralla paneli anında kapatır.
 * Not: 0.18'de "0" kısayolu silgi aracıdır (kütüphane değil); kütüphanenin klavye kısayolu yoktur.
 */

/** Excalidraw `DEFAULT_SIDEBAR.name` — kütüphane (varsayılan sekme) ve arama sekmelerini taşıyan kenar çubuğu. */
export const EXCALIDRAW_DEFAULT_SIDEBAR = 'default';

/** Açık kenar çubuğu kapatılmalı mı (default kenar çubuğunun tamamı — kütüphane sekmesine geçiş engellensin). */
export function isBlockedSidebar(openSidebar: { name?: unknown } | null | undefined): boolean {
  return !!openSidebar && openSidebar.name === EXCALIDRAW_DEFAULT_SIDEBAR;
}

/**
 * Excalidraw bağlantı balonundaki gerçek `<a href>` (0.18.1 dist: `className: "excalidraw-hyperlinkContainer-link"`).
 * Sol tık React `onClick` → `onLinkOpen` yolundan geçer; ama orta tık, bağlam menüsü ("yeni sekmede aç") ve sürükle-bırak
 * tarayıcının kendi bağlantı davranışıdır ve onayı atlatır (issue #332, security O1).
 */
export const EXCALIDRAW_HYPERLINK_ANCHOR_SELECTOR = 'a.excalidraw-hyperlinkContainer-link';

/**
 * Tuval host'una capture-phase dinleyiciler kurar: bağlantı balonu anchor'ında orta tık (`auxclick`) engellenip
 * uygulamanın onay akışına (`onLinkOpen`) yönlendirilir; `contextmenu` ve `dragstart` yalnızca engellenir.
 * Dönen fonksiyon dinleyicileri kaldırır (tuval yıkılırken çağrılır).
 */
export function guardHyperlinkAnchors(host: HTMLElement, onLinkOpen: (link: string | null) => void): () => void {
  const anchorOf = (event: Event): Element | null =>
    event.target instanceof Element ? event.target.closest(EXCALIDRAW_HYPERLINK_ANCHOR_SELECTOR) : null;

  const onAuxClick = (event: MouseEvent): void => {
    const anchor = anchorOf(event);
    if (!anchor) {
      return;
    }
    event.preventDefault();
    if (event.button === 1) {
      onLinkOpen(anchor.getAttribute('href'));
    }
  };
  const block = (event: Event): void => {
    if (anchorOf(event)) {
      event.preventDefault();
    }
  };

  host.addEventListener('auxclick', onAuxClick, true);
  host.addEventListener('contextmenu', block, true);
  host.addEventListener('dragstart', block, true);
  return () => {
    host.removeEventListener('auxclick', onAuxClick, true);
    host.removeEventListener('contextmenu', block, true);
    host.removeEventListener('dragstart', block, true);
  };
}

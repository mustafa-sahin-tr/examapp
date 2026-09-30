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

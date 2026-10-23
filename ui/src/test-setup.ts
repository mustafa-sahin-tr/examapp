import { afterEach, beforeEach, vi } from 'vitest';

// Jasmine her spec'ten sonra spyOn'ları geri alıyordu; Vitest almıyor ve unit-test builder'ı dosyaları aynı tarayıcı
// bağlamında (isolate: false) koşuyor. Geri alınmayan global spy'lar (console, window.matchMedia, prototype'lar)
// sonraki dosyalara sızmasın (#545).
afterEach(() => {
  vi.restoreAllMocks();
});

// `provideServerRendering()` (ör. `app.config.server` yüklenince) global `ngServerMode = true` yapıp hiç geri almıyor.
// Sızarsa sonraki testlerde `afterNextRender` sessizce no-op olur; CI'daki dosya dağılımına göre comment-composer
// odak testleri düşüyordu (#550, #551). Tarayıcı testlerinde hiçbir zaman açık kalmasın; statik import bir dosyanın
// ilk testine de sızmasın diye hem önce hem sonra temizlenir.
const clearServerMode = () => {
  delete (globalThis as { ngServerMode?: boolean }).ngServerMode;
};
beforeEach(clearServerMode);
afterEach(clearServerMode);

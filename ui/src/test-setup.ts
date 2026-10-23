import { afterEach, vi } from 'vitest';

// Jasmine her spec'ten sonra spyOn'ları geri alıyordu; Vitest almıyor ve unit-test builder'ı dosyaları aynı tarayıcı
// bağlamında (isolate: false) koşuyor. Geri alınmayan global spy'lar (console, window.matchMedia, prototype'lar)
// sonraki dosyalara sızmasın (#545).
afterEach(() => {
  vi.restoreAllMocks();
});

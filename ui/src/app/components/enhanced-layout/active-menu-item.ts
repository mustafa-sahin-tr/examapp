/** Aktif menü öğesi türetmesinin ihtiyaç duyduğu menü öğesi alanları. */
export interface RoutedMenuEntry {
  id: string;
  route: string;
  type: 'menu' | 'divider';
}

/**
 * Issue #385: menüde kendi öğesi olmayan alt/detay sayfaları, üst menü öğesinin rotasına eşlenir.
 * Anahtar URL yolu öneki (segment sınırında eşleşir), değer menü öğesinin rotası.
 * - `/test/:id` (worksheet detayı), `/tests-enhanced`, `/testsolve/...` → Sınavlar (`/tests`)
 * - `/program-create` → Programlarım (`/programs`); `/programs/:id/detail` önek eşleşmesiyle zaten bulunur
 * - Öğretmen soru akışı `/questioncanvas...`, `/question...`, `/questions/view`, `/imageselect` → Test Ekleme (`/exam`):
 *   soru ekleme, test oluşturma/detay akışından açılır
 *
 * Değişmez: önek uzunluğuna göre AZALAN sırada tutulur — `find` ilk (en uzun) eşleşmeyi döndürür.
 */
const ROUTE_ALIASES: ReadonlyArray<readonly [prefix: string, target: string]> = [
  ['/tests-enhanced', '/tests'],
  ['/questioncanvas', '/exam'],
  ['/program-create', '/programs'],
  ['/questions/view', '/exam'],
  ['/imageselect', '/exam'],
  ['/testsolve', '/tests'],
  ['/question', '/exam'],
  ['/test', '/tests'],
];

/** `path`, `prefix`'in kendisi ya da onun alt yolu mu (segment sınırında: `/test` → `/tests`'i kapsamaz). */
function matchesPrefix(path: string, prefix: string): boolean {
  return path === prefix || path.startsWith(`${prefix}/`);
}

/** Sorgu/fragment ve sondaki `/` atılmış yol. */
function normalizePath(url: string): string {
  const path = url.split(/[?#]/)[0] || '/';
  return path.length > 1 && path.endsWith('/') ? path.slice(0, -1) : path;
}

/**
 * Issue #385: URL'den seçili menü öğesini türetir. En uzun önek kazanır (`/admin/teachers` → `/admin` değil,
 * `/admin/teachers` öğesi). Hiçbir öğeye karşılık gelmeyen sayfada `null` döner — Dashboard'a düşülmez.
 * `items` yalnız kullanıcının gördüğü öğeler olmalı (rolüne kapalı öğe seçilmez).
 */
export function resolveActiveMenuItemId(url: string, items: readonly RoutedMenuEntry[]): string | null {
  let path = normalizePath(url);
  const alias = ROUTE_ALIASES.find(([prefix]) => matchesPrefix(path, prefix));
  if (alias) {
    path = alias[1];
  }

  let best: RoutedMenuEntry | null = null;
  for (const item of items) {
    if (item.type !== 'menu' || !item.route || !matchesPrefix(path, item.route)) {
      continue;
    }
    if (!best || item.route.length > best.route.length) {
      best = item;
    }
  }
  return best?.id ?? null;
}

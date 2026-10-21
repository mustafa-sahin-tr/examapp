/**
 * İşaretleme modu: klasör havuzundan rastgele, daha önce eğitime alınmamış sayfa seçimi.
 * Saf fonksiyonlar (DOM / Angular yok) — birim testleri doğrudan bunları sınar.
 */

/** Görsel sayılan uzantılar (tarayıcı bazı dosyalarda `type`'ı boş bırakabilir). */
export const LABELING_IMAGE_EXTENSIONS: readonly string[] = ['webp', 'jpg', 'jpeg', 'png'];

/** question-detector `page_id` uzunluğu: `sha256(bytes).hexdigest()[:16]`. */
export const PAGE_ID_LENGTH = 16;

export function isLabelingImage(file: Pick<File, 'name' | 'type'>): boolean {
  if (file.type?.startsWith('image/')) {
    return true;
  }
  const dot = file.name.lastIndexOf('.');
  if (dot < 0) {
    return false;
  }
  return LABELING_IMAGE_EXTENSIONS.includes(file.name.slice(dot + 1).toLowerCase());
}

/**
 * Havuzdan rastgele bir öğe çıkarır (yerinde, O(1): seçilen son öğeyle yer değiştirip atılır — sıra önemsiz).
 * Havuz boşsa `undefined`. `rng` [0, 1) döndürmeli.
 */
export function takeRandom<T>(pool: T[], rng: () => number = Math.random): T | undefined {
  if (pool.length === 0) {
    return undefined;
  }
  const raw = Math.floor(rng() * pool.length);
  const index = Math.min(Math.max(raw, 0), pool.length - 1);
  const picked = pool[index];
  pool[index] = pool[pool.length - 1];
  pool.pop();
  return picked;
}

export function toHex(buffer: ArrayBuffer): string {
  return Array.from(new Uint8Array(buffer), (b) => b.toString(16).padStart(2, '0')).join('');
}

/**
 * Dosyanın question-detector'daki sayfa id'si. UI görseli `readAsDataURL` ile ham baytlarından gönderir,
 * servis base64'ü çözüp aynı baytların sha256'sını alır — bu yüzden dosya içeriğinin hash'i eşleşir.
 */
export async function pageIdOf(file: Blob, subtle: SubtleCrypto = crypto.subtle): Promise<string> {
  const digest = await subtle.digest('SHA-256', await file.arrayBuffer());
  return toHex(digest).slice(0, PAGE_ID_LENGTH);
}

export interface LabelingCandidate<F> {
  file: F;
  pageId: string;
}

export interface PickOptions<F> {
  /** Kalan (henüz gösterilmemiş) dosyalar; seçilen/atlanan her dosya buradan çıkarılır. */
  pool: F[];
  /** Eğitim veri setinde zaten bulunan sayfa id'leri. */
  labeled: ReadonlySet<string>;
  hash: (file: F) => Promise<string>;
  rng?: () => number;
  /** Daha önce işaretlenmiş olduğu için atlandı. */
  onSkipLabeled?: (file: F, pageId: string) => void;
  /** Dosya okunamadı (hash alınamadı); havuzdan çıkarılır ve atlanır. */
  onUnreadable?: (file: F, error: unknown) => void;
  /** true dönerse arama bırakılır (`null`): yeni klasör seçildi / sayfa kapandı. */
  isCancelled?: () => boolean;
}

/**
 * Havuzdan, `labeled` kümesinde olmayan rastgele bir sayfa bulana dek çeker. Her deneme havuzu bir
 * küçülttüğü için döngü sınırlıdır (en çok havuz boyu kadar). Havuz biterse ya da iptal edilirse `null`.
 */
export async function pickNextUnlabeled<F>(options: PickOptions<F>): Promise<LabelingCandidate<F> | null> {
  const { pool, labeled, hash, rng = Math.random } = options;
  while (pool.length > 0) {
    if (options.isCancelled?.()) {
      return null;
    }
    const file = takeRandom(pool, rng)!;
    let pageId: string;
    try {
      pageId = await hash(file);
    } catch (error) {
      options.onUnreadable?.(file, error);
      continue;
    }
    if (options.isCancelled?.()) {
      return null;
    }
    if (labeled.has(pageId)) {
      options.onSkipLabeled?.(file, pageId);
      continue;
    }
    return { file, pageId };
  }
  return null;
}

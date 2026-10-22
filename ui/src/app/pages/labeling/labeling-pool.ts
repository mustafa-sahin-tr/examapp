/**
 * İşaretleme modu: klasör havuzundan ad sırasıyla, daha önce eğitime alınmamış sayfa seçimi.
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

const PATH_COLLATOR = new Intl.Collator('tr', { numeric: true, sensitivity: 'base' });

/**
 * Klasör seçiminden gelen göreli yolları ad sırasıyla karşılaştırır: önce klasör yolu (parça parça; bir klasörün
 * kendi görselleri alt klasörlerinden önce), sonra dosya adı. Sayılar doğal sırada (`p2` < `p10`).
 */
export function compareLabelingPaths(a: string, b: string): number {
  const segA = a.split('/');
  const segB = b.split('/');
  const dirA = segA.slice(0, -1);
  const dirB = segB.slice(0, -1);
  for (let i = 0; i < Math.min(dirA.length, dirB.length); i++) {
    const diff = PATH_COLLATOR.compare(dirA[i], dirB[i]);
    if (diff !== 0) {
      return diff;
    }
  }
  if (dirA.length !== dirB.length) {
    return dirA.length - dirB.length;
  }
  return PATH_COLLATOR.compare(segA[segA.length - 1], segB[segB.length - 1]);
}

/** Klasör seçimindeki dosyaları gösterim sırasına dizer (yeni dizi). */
export function sortLabelingFiles<F extends Pick<File, 'name' | 'webkitRelativePath'>>(files: readonly F[]): F[] {
  const pathOf = (file: F) => file.webkitRelativePath || file.name;
  // Yalnız büyük/küçük harf ya da aksanla ayrışan adlarda da sıra sabit kalsın (FileList sırasına bağlı olmasın).
  const ordinal = (a: string, b: string) => (a < b ? -1 : a > b ? 1 : 0);
  return [...files].sort((a, b) => compareLabelingPaths(pathOf(a), pathOf(b)) || ordinal(pathOf(a), pathOf(b)));
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
  /** Kalan (henüz gösterilmemiş) dosyalar, gösterim sırasıyla; seçilen/atlanan her dosya baştan çıkarılır. */
  pool: F[];
  /** Eğitim veri setinde zaten bulunan sayfa id'leri. */
  labeled: ReadonlySet<string>;
  hash: (file: F) => Promise<string>;
  /** Daha önce işaretlenmiş olduğu için atlandı. */
  onSkipLabeled?: (file: F, pageId: string) => void;
  /** Dosya okunamadı (hash alınamadı); havuzdan çıkarılır ve atlanır. */
  onUnreadable?: (file: F, error: unknown) => void;
  /** true dönerse arama bırakılır (`null`): yeni klasör seçildi / sayfa kapandı. */
  isCancelled?: () => boolean;
}

/**
 * Havuzun başından, `labeled` kümesinde olmayan ilk sayfayı bulana dek çeker. Her deneme havuzu bir
 * küçülttüğü için döngü sınırlıdır (en çok havuz boyu kadar). Havuz biterse ya da iptal edilirse `null`.
 */
export async function pickNextUnlabeled<F>(options: PickOptions<F>): Promise<LabelingCandidate<F> | null> {
  const { pool, labeled, hash } = options;
  while (pool.length > 0) {
    if (options.isCancelled?.()) {
      return null;
    }
    const file = pool.shift()!;
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

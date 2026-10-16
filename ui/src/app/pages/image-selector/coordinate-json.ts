import { AnswerChoice, ClassificationSource, QuestionClassificationPayload, QuestionRegion } from '../../models/draws';

/**
 * Issue #454: klasördeki `question-coordinates.json` ile soru ekleme ("JSON modu").
 *
 * Bu dosya saf fonksiyonlardan oluşur (DOM/Angular bağımlılığı yok) ki birim testi kolay olsun:
 * - `parseCoordinateJson`: ham metni doğrular, bozuk kayıtları atlar, dosya adına göre gruplar.
 * - `selectCoordinateImageFiles`: klasördeki görsellerden yalnız JSON'da geçenleri doğal sırayla seçer.
 * - `buildRegionsFromCoordinates`: bir sayfanın kayıtlarından question-detector'ın ürettiği şekilde
 *   `QuestionRegion[]` kurar (detector çağrılmaz).
 *
 * Koordinatlar görselin doğal piksel boyutundadır (tuval görselin doğal boyutunda çizilir).
 */

export const COORDINATE_JSON_FILE_NAME = 'question-coordinates.json';

/** Şık kutuları soru kutusunun dışına taştığında soru kutusunun altına eklenen pay (px). */
export const COORDINATE_BOX_BOTTOM_PADDING = 15;

/** Bundan büyük koordinat JSON'u okunmaz (585 soruluk gerçek örnek ~250 KB). */
export const COORDINATE_JSON_MAX_BYTES = 5 * 1024 * 1024;

export interface CoordinateOption {
  label: string;
  relX: number;
  relY: number;
  width: number;
  height: number;
}

export interface CoordinateQuestion {
  /** JSON'daki dosya adı (yalnız taban ad, yol ayıklanmış). */
  filename: string;
  soruNo: string;
  x: number;
  y: number;
  width: number;
  height: number;
  /** Etikete göre (A, B, C, ...) sıralı. */
  options: CoordinateOption[];
  subjectId: number | null;
  topicId: number | null;
  subTopicIds: number[];
}

export interface CoordinateJsonParseResult {
  /** Anahtar: küçük harfli dosya adı. Değer: `soru_no` sırasına dizilmiş sorular. */
  pages: Map<string, CoordinateQuestion[]>;
  /** Geçerli soru sayısı. */
  validCount: number;
  /** Atlanan (bozuk) kayıt sayısı. */
  skippedCount: number;
  /** Metin JSON olarak okunamadı ya da kök bir dizi değil. */
  invalidJson: boolean;
}

export interface CoordinateImageSelection {
  /** JSON'da geçen ve klasörde bulunan görseller, sayfa numarasına göre doğal sıralı. */
  files: File[];
  /** JSON'da geçip klasörde bulunmayan dosya adları (doğal sıralı). */
  missing: string[];
}

export interface ImageBounds {
  width: number;
  height: number;
}

export function isCoordinateJsonFile(file: Pick<File, 'name'>): boolean {
  return file.name.trim().toLowerCase() === COORDINATE_JSON_FILE_NAME;
}

export function coordinateFileKey(name: string): string {
  const parts = name.split(/[\\/]/);
  return (parts[parts.length - 1] ?? '').trim().toLowerCase();
}

const naturalCompare = (a: string, b: string): number =>
  a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' });

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** Sonlu sayı (string sayı da kabul) → number; değilse null. */
function toFiniteNumber(value: unknown): number | null {
  if (typeof value === 'string' && value.trim() === '') {
    return null;
  }
  if (typeof value !== 'number' && typeof value !== 'string') {
    return null;
  }
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

/** Sonlu ve negatif olmayan sayı → number; değilse null. */
function toNonNegativeNumber(value: unknown): number | null {
  const n = toFiniteNumber(value);
  return n !== null && n >= 0 ? n : null;
}

/** Pozitif tam sayı id (JSON'da string gelir) → number; değilse null. */
function toPositiveId(value: unknown): number | null {
  if (typeof value !== 'number' && typeof value !== 'string') {
    return null;
  }
  const raw = typeof value === 'string' ? value.trim() : value;
  if (raw === '') {
    return null;
  }
  const n = Number(raw);
  return Number.isInteger(n) && n > 0 ? n : null;
}

function idOf(node: unknown): number | null {
  return isRecord(node) ? toPositiveId(node['id']) : null;
}

function parseOption(label: string, raw: unknown): CoordinateOption | null {
  if (!isRecord(raw)) {
    return null;
  }
  // rel değerleri negatif olabilir (şık soru kökünün solunda/üstünde); mutlak konum parseEntry'de denetlenir.
  const relX = toFiniteNumber(raw['rel_x']);
  const relY = toFiniteNumber(raw['rel_y']);
  const width = toNonNegativeNumber(raw['width']);
  const height = toNonNegativeNumber(raw['height']);
  if (relX === null || relY === null || width === null || height === null || width <= 0 || height <= 0) {
    return null;
  }
  return { label, relX, relY, width, height };
}

function parseEntry(raw: unknown): CoordinateQuestion | null {
  if (!isRecord(raw)) {
    return null;
  }

  const filenameRaw = raw['filename'];
  if (typeof filenameRaw !== 'string') {
    return null;
  }
  const filenameParts = filenameRaw.trim().split(/[\\/]/);
  const filename = (filenameParts[filenameParts.length - 1] ?? '').trim();
  if (!filename) {
    return null;
  }

  const soruNoRaw = raw['soru_no'];
  if (typeof soruNoRaw !== 'string' && typeof soruNoRaw !== 'number') {
    return null;
  }
  const soruNo = String(soruNoRaw).trim();
  if (!soruNo) {
    return null;
  }

  const x = toNonNegativeNumber(raw['x']);
  const y = toNonNegativeNumber(raw['y']);
  const width = toNonNegativeNumber(raw['width']);
  const height = toNonNegativeNumber(raw['height']);
  if (x === null || y === null || width === null || height === null || width <= 0 || height <= 0) {
    return null;
  }

  // Şık listesi boş olabilir (açık uçlu soru); ama tek bir bozuk şık kaydın tamamını geçersiz kılar —
  // eksik şıklı bir çoktan seçmeli soru sessizce kaydedilmesin.
  const siklar = raw['siklar'] ?? {};
  if (!isRecord(siklar)) {
    return null;
  }
  const options: CoordinateOption[] = [];
  for (const [key, value] of Object.entries(siklar)) {
    const label = key.trim().toUpperCase();
    if (!label) {
      return null;
    }
    const option = parseOption(label, value);
    if (!option || x + option.relX < 0 || y + option.relY < 0) {
      return null; // bozuk ya da sayfanın dışına (sola/yukarı) düşen şık
    }
    options.push(option);
  }
  options.sort((a, b) => naturalCompare(a.label, b.label));

  const altKonular = Array.isArray(raw['alt_konular']) ? raw['alt_konular'] : [];
  const subTopicIds: number[] = [];
  for (const node of altKonular) {
    const id = idOf(node);
    if (id !== null && !subTopicIds.includes(id)) {
      subTopicIds.push(id);
    }
  }

  return {
    filename,
    soruNo,
    x,
    y,
    width,
    height,
    options,
    subjectId: idOf(raw['ders']),
    topicId: idOf(raw['konu']),
    subTopicIds,
  };
}

/** `question-coordinates.json` metnini savunmacı biçimde ayrıştırır; hiçbir durumda fırlatmaz. */
export function parseCoordinateJson(text: string): CoordinateJsonParseResult {
  const empty = (invalidJson: boolean, skippedCount = 0): CoordinateJsonParseResult => ({
    pages: new Map(),
    validCount: 0,
    skippedCount,
    invalidJson,
  });

  let data: unknown;
  try {
    data = JSON.parse(text);
  } catch {
    return empty(true);
  }
  if (!Array.isArray(data)) {
    return empty(true);
  }

  const pages = new Map<string, CoordinateQuestion[]>();
  let validCount = 0;
  let skippedCount = 0;
  for (const raw of data) {
    const entry = parseEntry(raw);
    if (!entry) {
      skippedCount++;
      continue;
    }
    const key = coordinateFileKey(entry.filename);
    const list = pages.get(key) ?? [];
    list.push(entry);
    pages.set(key, list);
    validCount++;
  }

  for (const list of pages.values()) {
    list.sort((a, b) => naturalCompare(a.soruNo, b.soruNo));
  }

  return { pages, validCount, skippedCount, invalidJson: false };
}

/**
 * Klasördeki dosyalardan JSON'da geçen görselleri seçer (diğerleri atlanır). Aynı ad birden fazla
 * alt klasörde varsa ilki alınır. Sıralama dosya adına göre doğaldır (page_2 < page_10).
 */
export function selectCoordinateImageFiles(
  files: readonly File[],
  pages: ReadonlyMap<string, CoordinateQuestion[]>
): CoordinateImageSelection {
  const byKey = new Map<string, File>();
  for (const file of files) {
    if (!file.type.startsWith('image/')) {
      continue;
    }
    const key = coordinateFileKey(file.name);
    if (pages.has(key) && !byKey.has(key)) {
      byKey.set(key, file);
    }
  }

  const missing: string[] = [];
  for (const [key, list] of pages) {
    if (!byKey.has(key)) {
      missing.push(list[0]?.filename ?? key);
    }
  }

  return {
    files: [...byKey.values()].sort((a, b) => naturalCompare(a.name, b.name)),
    missing: missing.sort(naturalCompare),
  };
}

/**
 * Sayfa kayıtlarından bölgeleri kurar. Şık koordinatı = soru + rel (sayfaya göre mutlak). Soru kutusu
 * yalnız soru kökünü kapsadığından şıkları kapsayacak şekilde uzatılır (negatif rel → sola/yukarı, aksi
 * halde sağa/aşağı); aşağı uzatıldıysa en alttaki şığın altına {@link COORDINATE_BOX_BOTTOM_PADDING}
 * eklenir. `bounds` verilirse kutular görselin dışına taşmaz (kenara kadar uzanabilir). Hiçbir şık doğru
 * işaretli gelmez.
 */
export function buildRegionsFromCoordinates(
  questions: readonly CoordinateQuestion[],
  bounds?: ImageBounds
): QuestionRegion[] {
  const maxRight = bounds && bounds.width > 0 ? bounds.width : Number.POSITIVE_INFINITY;
  const maxBottom = bounds && bounds.height > 0 ? bounds.height : Number.POSITIVE_INFINITY;

  return questions.map((q, index) => {
    const stemLeft = Math.round(q.x);
    const stemTop = Math.round(q.y);
    const stemRight = Math.round(q.x + q.width);
    const stemBottom = Math.round(q.y + q.height);

    const optionLefts = q.options.map((o) => Math.round(q.x + o.relX));
    const optionTops = q.options.map((o) => Math.round(q.y + o.relY));
    const optionRights = q.options.map((o) => Math.round(q.x + o.relX + o.width));
    const optionBottoms = q.options.map((o) => Math.round(q.y + o.relY + o.height));

    const x = Math.min(stemLeft, ...optionLefts);
    const y = Math.min(stemTop, ...optionTops);
    const right = Math.max(stemRight, ...optionRights);
    const optionsBottom = Math.max(stemBottom, ...optionBottoms);
    const bottom = optionsBottom > stemBottom ? optionsBottom + COORDINATE_BOX_BOTTOM_PADDING : stemBottom;

    const width = Math.max(1, Math.min(right, maxRight) - x);
    const height = Math.max(1, Math.min(bottom, maxBottom) - y);

    const answers: AnswerChoice[] = q.options.map((o, answerIndex) => ({
      label: o.label,
      x: Math.round(q.x + o.relX),
      y: Math.round(q.y + o.relY),
      width: Math.round(o.width),
      height: Math.round(o.height),
      isCorrect: false,
      id: answerIndex,
      imageUrl: '',
    }));

    const classification: QuestionClassificationPayload = {
      subjectId: q.subjectId,
      topicId: q.topicId,
      subTopicIds: [...q.subTopicIds],
      source: 'AI',
    };

    const region: QuestionRegion = {
      // predict() ile aynı adlandırma: exampleFlags / interactionTypes gibi haritalar region.name'e bağlı.
      name: `Soru ${index + 1}`,
      x,
      y,
      width,
      height,
      answers,
      isExample: false,
      exampleAnswer: null,
      id: index,
      passageId: '',
      imageId: '',
      imageUrl: '',
      classification,
      classificationSource: ClassificationSource.AI,
    };
    // Müfettiş panelinin sınıflandırmayı göstermesi için düz alanlar da doldurulur (backend bunları
    // yok sayar; kaydedilen değer `classification` nesnesidir).
    if (q.subjectId !== null) {
      region.subjectId = q.subjectId;
    }
    if (q.topicId !== null) {
      region.topicId = q.topicId;
    }
    if (q.subTopicIds.length) {
      region.subtopicId = q.subTopicIds[0];
      region.subtopicIds = [...q.subTopicIds];
    }
    return region;
  });
}

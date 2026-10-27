import { BlockInteractionType, PredictionList } from './prediction';

/** Sunucudaki işaretleme kaynağı (exam API `LabelingSource`). */
export type LabelingSourceName = 'Model' | 'Ai' | 'Manual';

/** `GET /api/exam/labeling/ai-models`: izinli Claude modelleri ve 1M token fiyatları. */
export interface LabelingAiModel {
  id: string;
  displayName: string;
  inputUsdPerMTok: number;
  outputUsdPerMTok: number;
  isDefault: boolean;
}

export interface LabelingResult {
  source: LabelingSourceName;
  modelName: string;
  predictions: PredictionList;
  baseSource: LabelingSourceName | null;
  baseModelName: string | null;
  inputTokens: number | null;
  outputTokens: number | null;
  estimatedCostUsd: number | null;
  /** AI sonucunu üreten prompt sürümü (0 = varsayılan); diğer kaynaklarda null. */
  promptVersion: number | null;
  createdByName: string | null;
  createdAt: string;
  updatedAt: string;
  /** Manuel kayıt Claude sonucundan başladıysa o sonucun prompt sürümü. */
  basePromptVersion?: number | null;
}

/** Sayfanın soru bankasına son kaydı (issue #470). */
export interface LabelingBankInfo {
  savedAt: string;
  savedByName: string | null;
  questionCount: number;
}

/** `GET /api/exam/labeling/pages/{sha256}`: sayfanın kayıtlı tüm kaynakları (kayıt yoksa 404). */
export interface LabelingPage {
  imageSha256: string;
  width: number;
  height: number;
  results: LabelingResult[];
  /** Sayfa soru bankasına kaydedildiyse son kaydın özeti. */
  bank?: LabelingBankInfo | null;
}

export interface LabelingAiDetectRequest {
  imageBase64: string;
  model: string;
  force: boolean;
}

/** `origin`: `live` Claude'a soruldu (maliyetli), `cached` kayıtlı sonuç döndü. */
export interface LabelingAiDetectResponse {
  origin: 'live' | 'cached';
  imageSha256: string;
  result: LabelingResult;
}

/** Claude prompt'unun bir sürümü; `version` 0 = koddaki varsayılanlar. */
export interface LabelingPrompt {
  version: number;
  questionInstruction: string;
  optionInstruction: string;
  questionPadding: number;
  optionPadding: number;
  expandQuestionToOptions: boolean;
  createdByName: string | null;
  createdAt: string | null;
}

/** `GET/PUT /api/exam/labeling/prompt`: etkin sürüm, varsayılanlar ve son sürümler (yeniden eskiye). */
export interface LabelingPromptState {
  active: LabelingPrompt;
  defaults: LabelingPrompt;
  history: LabelingPrompt[];
}

export interface LabelingSavePromptRequest {
  questionInstruction: string;
  optionInstruction: string;
  questionPadding: number;
  optionPadding: number;
  expandQuestionToOptions: boolean;
}

export interface LabelingSaveManualRequest {
  imageBase64: string;
  predictions: PredictionList;
  baseSource: 'Model' | 'Ai' | null;
  baseModelName: string | null;
  basePromptVersion: number | null;
  modelSnapshot: PredictionList | null;
}

/**
 * Tuvaldeki kutuların nereden geldiği. `Manual` kayıttan açıldıysa `base` o kaydın başlangıç kaynağıdır
 * (yeniden eğitime gönderilince manuel kaydın başlangıcı korunur).
 */
export interface DetectionSource {
  kind: LabelingSourceName;
  modelName?: string | null;
  /** `Ai` kaynağında sonucu üreten prompt sürümü. */
  promptVersion?: number | null;
  base?: { source: 'Model' | 'Ai'; modelName: string | null; promptVersion?: number | null } | null;
}

export interface LabelingBankBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface LabelingBankQuestion extends LabelingBankBox {
  /** Şıklar okunma sırasıyla (A, B, C, ...); orijinal sayfa pikseli. */
  options: LabelingBankBox[];
  correctOptionIndex: number;
  /** Issue #577: kitaptaki etkinlik etiketi ("Etkinlik 9"), en çok 50 karakter; boşsa gönderilmez. */
  activityLabel?: string;
  /** Issue #577: madde etiketi ("3", "b"), en çok 50 karakter; boşsa gönderilmez. */
  itemLabel?: string;
}

/** `POST /api/exam/labeling/pages/bank`: soruları teste bağlamadan soru bankasına kaydeder. */
export interface LabelingBankSaveRequest {
  imageBase64: string;
  questions: LabelingBankQuestion[];
  /** Sayfanın sınıfı (`GET /api/exam/worksheet/grades` id'si; zorunlu, bilinmeyen sınıf → 400). Sınıflandırıcı yalnız
   * bu sınıfın konularını kullanır (issue #565). */
  gradeId: number;
  /** Sayfa ya da aynı kırpma daha önce kaydedildiyse de yeniden kaydet (sorular ikinci kez oluşur). */
  force: boolean;
  /**
   * Issue #577: kaynak kitap (`/api/exam/labeling/books`) ve görselin dosya sayfası. Sunucu içindekiler varsayılanını
   * (sınıf / ders / konu / alt konu, `ClassificationSource=Book`) uygular ve kaynağı kaydeder. Kitap seçili değilse yok.
   */
  bookId?: number;
  filePage?: number;
}

export interface LabelingBankSaveResponse {
  imageSha256: string;
  bank: LabelingBankInfo;
  /** Issue #577: içindekiler sınıflandırması soruya uygulandı mı (kitapsız kayıtta / sınıf farklıysa false ya da yok). */
  tocApplied?: boolean;
}

/**
 * 409 gövdesi: sayfa daha önce kaydedildi (`existing`) ya da şu an kaydediliyor. Aynı kırpma bankadaysa gövde
 * `LabelingCropConflict`tır (`models/labeling-book.ts`, `errorCode: 'LabelingCropAlreadyInBank'`).
 */
export interface LabelingBankConflict {
  message: string;
  /** `LabelingAnswerKeyPage` (#577): sayfa kitabın cevap anahtarı aralığında; `existing` yok, `force` ile kaydedilir. */
  errorCode: 'LabelingPageAlreadyInBank' | 'LabelingAnswerKeyPage';
  existing: LabelingBankInfo | null;
}

// ---------------- Issue #579: etkinlik yapısı (kırpma → taslak InteractionPlan) ----------------

/** `POST /api/exam/labeling/activity-structure` isteğindeki tek blok (sayfa pikseli). */
export interface LabelingActivityBlock {
  /** İstemcinin blok anahtarı (yanıtta aynen döner); işaretleme ekranında bölge adı. */
  key: string;
  x: number;
  y: number;
  width: number;
  height: number;
  interactionType: BlockInteractionType;
}

/** 1..20 blok; her blok kendi tipinin talimatı ve şemasıyla ayrı çağrıda çıkarılır. */
export interface LabelingActivityStructureRequest {
  imageBase64: string;
  model: string;
  /** true: kayıtlı sonuç yok sayılır, Claude'a yeniden sorulur (ücretli). */
  force: boolean;
  blocks: LabelingActivityBlock[];
}

/**
 * Bloğun taslağı. `plan` o tipin InteractionPlan v1'i (soru tuvali yazarlığının ürettiği şekil; kutular blok kutusuna
 * göre, sayfa pikseli); çözüm taşımaz. `valid=false` iken plan yine gelebilir, `validationErrors` sunucu doğrulamasıdır.
 */
export interface LabelingActivityStructureResult {
  key: string;
  interactionType: BlockInteractionType;
  status: 'ok' | 'error';
  origin: 'live' | 'cached' | null;
  plan: Record<string, unknown> | null;
  /** Kitapta basılı yönerge metni (varsa). */
  instruction: string | null;
  valid: boolean;
  validationErrors: string[];
  promptVersion: number | null;
  inputTokens: number | null;
  outputTokens: number | null;
  estimatedCostUsd: number | null;
  error: string | null;
  /** Issue #580: bloğun kırpma hash'i — kayıtlı çözüm taslağını geri yüklemek için. */
  cropSha256?: string | null;
}

/** Sonuçlar istek sırasıyla. */
export interface LabelingActivityStructureResponse {
  results: LabelingActivityStructureResult[];
}

/** Bir etkileşim tipinin talimat sürümü; `version` 0 = koddaki varsayılan. */
export interface LabelingActivityPrompt {
  version: number;
  instruction: string;
  createdByName: string | null;
  createdAt: string | null;
}

/**
 * Etkinlik talimatlarının anahtarı: #579 etkileşim tipleri (yapı çıkarma) ve #580'in üç çözüm talimatı — `answerKey`
 * (cevap anahtarı sayfasını okuma), `solveFromKey` (görsel cevabı anahtar kırpmasından çözüme çevirme), `solveAi`
 * (anahtarsız maddede çözüm önerme).
 */
export type LabelingSolutionPromptKind = 'answerKey' | 'solveFromKey' | 'solveAi';
export type LabelingPromptKind = BlockInteractionType | LabelingSolutionPromptKind;

/** Tip başına etkin sürüm, varsayılan ve son sürümler (yeniden eskiye). */
export interface LabelingActivityPromptType {
  interactionType: LabelingPromptKind;
  active: LabelingActivityPrompt;
  defaults: LabelingActivityPrompt;
  history: LabelingActivityPrompt[];
}

/** `GET /api/exam/labeling/activity-prompts`. */
export interface LabelingActivityPromptsState {
  types: LabelingActivityPromptType[];
}

/**
 * `PUT /api/exam/labeling/activity-prompts/{type}` gövdesi; yanıt tipin yeni etkin sürümüdür (`LabelingActivityPrompt`; aynı
 * içerik → sürüm açılmaz, etkin sürüm döner).
 */
export interface LabelingSaveActivityPromptRequest {
  instruction: string;
}

// ---------------- Issue #580: cevap anahtarı ve etkinlik çözümü ----------------

/** Cevap anahtarı sayfasından okunan tek satır (`box`: anahtar sayfasında, sayfa pikseli; yalnız görsel cevaplarda). */
export interface LabelingAnswerKeyEntry {
  id: number;
  filePage: number;
  printedPage: number | null;
  activityLabel: string | null;
  itemLabel: string | null;
  answerText: string | null;
  /** Cevap mini görsel (eşleştirme çizgisi, ✓ tablosu, boyalı şekil): çözüm anahtar kırpmasıyla birlikte çıkarılır. */
  visual: boolean;
  box: LabelingBankBox | null;
  /** Öğretmen düzelttiyse son düzelten ve zaman. */
  editedByName?: string | null;
  editedAt?: string | null;
}

/** Sunucu sınırları (`AnswerKeyEntryUpdateRequestDto`). */
export const ANSWER_KEY_LIMITS = { maxLabel: 64, maxAnswer: 1000, maxPrintedPage: 100_000 } as const;

/** `POST /api/exam/labeling/answer-key/read`: sayfa kitabın cevap anahtarı aralığında olmalı (yoksa 400 `LabelingNotAnswerKeyPage`). */
export interface LabelingAnswerKeyReadRequest {
  bookId: number;
  /** Anahtar sayfasının dosya sayfası; toplu yüklemede dosya adından okunamazsa gönderilmez (sunucu sayfayı kendisi bulur). */
  filePage?: number;
  imageBase64: string;
  model: string;
  force: boolean;
  /** `force` okumada elle düzeltilmiş satırlar varsa sunucu 409 `LabelingAnswerKeyHasEdits` döner; true ile üzerine yazılır. */
  confirmOverwrite?: boolean;
}

/**
 * `origin`: `cached` iken maliyet / token ilk (canlı) okumanındır. `stale`: kayıtlı satırlar başka bir talimat / modelle
 * okunmuş (yine de gösterilir; yeniden okuma ücretli).
 */
export interface LabelingAnswerKeyReadResponse {
  origin: 'live' | 'cached';
  promptVersion: number;
  estimatedCostUsd: number;
  entries: LabelingAnswerKeyEntry[];
  inputTokens?: number;
  outputTokens?: number;
  stale?: boolean;
  /** Sayfada elle düzeltilmiş / silinmiş satır var (yeniden okuma `confirmOverwrite` ister). */
  hasEdits?: boolean;
}

/** `GET /api/exam/labeling/books/{bookId}/answer-key?printedPage=&activityLabel=`. */
export interface LabelingAnswerKeyLookupResponse {
  entries: LabelingAnswerKeyEntry[];
  total?: number;
}

/**
 * `POST /api/exam/labeling/books/{bookId}/answer-key/lookup`: sayfadaki blokların anahtar satırları tek istekte. Etkinlikte
 * `label` etkinlik etiketi; test sorusunda soru numarası (bölüm `sectionLabel`, ör. "TEST 01").
 */
export interface LabelingAnswerKeyLookupItem {
  key: string;
  label: string;
  itemLabel?: string;
}

export interface LabelingAnswerKeyLookupRequest {
  /** Blokların bulunduğu (etkinlik) sayfanın dosya sayfası. */
  filePage: number;
  printedPage?: number;
  sectionLabel?: string;
  items: LabelingAnswerKeyLookupItem[];
}

export interface LabelingAnswerKeyLookupResult {
  key: string;
  entries: LabelingAnswerKeyEntry[];
}

export interface LabelingAnswerKeyBatchLookupResponse {
  results: LabelingAnswerKeyLookupResult[];
}

/** `PUT /api/exam/labeling/answer-key/entries/{id}` gövdesi (boş metin null gönderilir). */
export interface LabelingAnswerKeyEntryUpdate {
  printedPage: number | null;
  activityLabel: string | null;
  itemLabel: string | null;
  answerText: string | null;
}

/** Çözümün kaynağı: anahtardan, AI önerisi ya da elle düzeltme. */
export type LabelingSolutionSource = 'Key' | 'AI' | 'Human';
export type LabelingSolutionStatus = 'pending' | 'approved';
/** `key`: kitabın cevap anahtarı satırlarından; `ai`: anahtarsız maddede Claude önerisi (ücretli). */
export type LabelingSolutionMode = 'key' | 'ai';

/** `POST /api/exam/labeling/activity-solution`: etkinlik bloğunun taslak çözümü (blok kutusu sayfa pikseli). */
export interface LabelingActivitySolutionRequest {
  imageBase64: string;
  x: number;
  y: number;
  width: number;
  height: number;
  interactionType: BlockInteractionType;
  /** Bloğun taslak planı (#579); çözüm bu planın id'lerine göre yazılır. */
  plan: Record<string, unknown>;
  model: string;
  force: boolean;
  mode: LabelingSolutionMode;
  key?: { entryIds: number[] };
}

/**
 * Taslak çözüm (`ActivitySolutionDto`). `solution` tipin çözüm şeması (matrixChoice `{"r1":["c2"]}`, slotFill
 * `{s1:{value…}}`, dragPlace `{"t1":["c1"]}`, match `{"l1":"r3"}`). `error` doluysa Claude çözemedi (`draftSolutionId`
 * null, kayıt yok). `origin`: `live` | `cached`; okuma / düzenleme / onay yanıtında null. `valid=false` taslak düzeltilmeden
 * onaylanamaz.
 */
export interface LabelingActivitySolutionResponse {
  draftSolutionId: number | null;
  solution: Record<string, unknown> | null;
  source: LabelingSolutionSource | null;
  status: LabelingSolutionStatus | null;
  promptVersion: number | null;
  origin: 'live' | 'cached' | null;
  valid: boolean;
  validationErrors: string[];
  estimatedCostUsd: number | null;
  error: string | null;
  cropSha256?: string | null;
  interactionType?: BlockInteractionType | null;
  planHash?: string | null;
  /** Çözümün yazıldığı taslak plan. */
  plan?: Record<string, unknown> | null;
  mode?: LabelingSolutionMode | null;
  modelName?: string | null;
  keyEntryIds?: number[] | null;
  inputTokens?: number | null;
  outputTokens?: number | null;
  createdByName?: string | null;
  createdAt?: string | null;
  editedByName?: string | null;
  editedAt?: string | null;
  approvedByName?: string | null;
  approvedAt?: string | null;
}

/** `GET /api/exam/labeling/activity-solution?cropSha256=`: kırpmanın kayıtlı çözümleri, en son güncellenen önce (≤ 10). */
export interface LabelingActivitySolutionList {
  solutions: LabelingActivitySolutionResponse[];
}

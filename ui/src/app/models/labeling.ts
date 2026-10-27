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

/** Tip başına etkin sürüm, varsayılan ve son sürümler (yeniden eskiye). */
export interface LabelingActivityPromptType {
  interactionType: BlockInteractionType;
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

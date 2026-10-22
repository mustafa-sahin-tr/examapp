import { PredictionList } from './prediction';

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

/** `GET /api/exam/labeling/pages/{sha256}`: sayfanın kayıtlı tüm kaynakları (kayıt yoksa 404). */
export interface LabelingPage {
  imageSha256: string;
  width: number;
  height: number;
  results: LabelingResult[];
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

/**
 * Issue #583: açık uçlu cevap (openText, bileşen H) yazarlığının saf kısmı. Kurallar backend `OpenTextGrader.ValidateDefinition`
 * ile aynı: plan `{version:1,type:"openText",minWords,maxWords,lines}` (sözcük sınırları 1..2000 ve min ≤ max, satır 1..30; hepsi
 * isteğe bağlı), çözüm `{sampleAnswer?, rubric?}` (her biri ≤ 4000 karakter düz metin; ikisi de boş olabilir — değerlendirme elle).
 * Rubrik yalnız öğretmene (değerlendirme kuyruğu) gider; öğrenci reveal'da yalnız örnek cevabı görür. Tip yalnız etkinlik
 * sayfasında sunulur (testte puanı yanlış sayardı); Claude yapı çıkarımı (#579) bu tip için yapılmaz, H bloğu varsayılan planla
 * açılır.
 */
import {
  OPEN_TEXT,
  OPEN_TEXT_LIMITS,
  OpenTextPlanV1,
  OpenTextSolution,
  stripOpenTextControlChars,
} from '../../models/open-text';

export interface OpenTextAuthoringState {
  minWords: number | null;
  maxWords: number | null;
  lines: number | null;
  sampleAnswer: string;
  rubric: string;
}

export type OpenTextIssueCode = 'minWordsRange' | 'maxWordsRange' | 'minAboveMax' | 'linesRange' | 'sampleTooLong' | 'rubricTooLong';

export interface OpenTextIssue {
  code: OpenTextIssueCode;
}

export interface OpenTextInspector extends OpenTextAuthoringState {
  limits: typeof OPEN_TEXT_LIMITS;
  issues: OpenTextIssueCode[];
}

/** Varsayılan plan: sözcük sınırı yok, 5 satırlık metin kutusu. */
export function createOpenTextState(): OpenTextAuthoringState {
  return { minWords: null, maxWords: null, lines: OPEN_TEXT_LIMITS.defaultLines, sampleAnswer: '', rubric: '' };
}

/** Sayı alanı: boş → null (sınırsız); tam sayı değilse NaN (sorun olarak gösterilir, kayıt engellenir). */
export function parseLimitInput(text: string): number | null {
  const trimmed = text.trim();
  if (!trimmed) return null;
  return /^\d+$/.test(trimmed) ? Number(trimmed) : Number.NaN;
}

function inRange(value: number | null, min: number, max: number): boolean {
  return value === null || (Number.isInteger(value) && value >= min && value <= max);
}

export function setOpenTextLimit(
  state: OpenTextAuthoringState,
  field: 'minWords' | 'maxWords' | 'lines',
  value: number | null
): OpenTextAuthoringState {
  return { ...state, [field]: value };
}

/** Örnek cevap / rubrik: kontrol karakterleri atılır (sunucu reddeder), metin olduğu gibi saklanır. */
export function setOpenTextSolutionText(
  state: OpenTextAuthoringState,
  field: 'sampleAnswer' | 'rubric',
  text: string
): OpenTextAuthoringState {
  return { ...state, [field]: stripOpenTextControlChars(text) };
}

export function openTextIssues(state: OpenTextAuthoringState): OpenTextIssue[] {
  const limits = OPEN_TEXT_LIMITS;
  const issues: OpenTextIssue[] = [];
  if (!inRange(state.minWords, limits.minWordLimit, limits.maxWordLimit)) issues.push({ code: 'minWordsRange' });
  if (!inRange(state.maxWords, limits.minWordLimit, limits.maxWordLimit)) issues.push({ code: 'maxWordsRange' });
  if (
    state.minWords !== null &&
    state.maxWords !== null &&
    Number.isInteger(state.minWords) &&
    Number.isInteger(state.maxWords) &&
    state.minWords > state.maxWords
  ) {
    issues.push({ code: 'minAboveMax' });
  }
  if (!inRange(state.lines, limits.minLines, limits.maxLines)) issues.push({ code: 'linesRange' });
  if (state.sampleAnswer.trim().length > limits.maxSolutionTextLength) issues.push({ code: 'sampleTooLong' });
  if (state.rubric.trim().length > limits.maxSolutionTextLength) issues.push({ code: 'rubricTooLong' });
  return issues;
}

/** Sunucunun allowlist'ine uygun plan (yalnız version, type, minWords, maxWords, lines). */
export function buildOpenTextPlan(state: OpenTextAuthoringState): OpenTextPlanV1 {
  const clean = (value: number | null) => (value !== null && Number.isInteger(value) ? value : null);
  return { version: 1, type: OPEN_TEXT, minWords: clean(state.minWords), maxWords: clean(state.maxWords), lines: clean(state.lines) };
}

/** Çözüm: yalnız dolu alanlar (kenar boşlukları kırpılır); ikisi de boşsa null (değerlendirme elle). */
export function buildOpenTextSolution(state: OpenTextAuthoringState): Partial<OpenTextSolution> | null {
  const sampleAnswer = state.sampleAnswer.trim();
  const rubric = state.rubric.trim();
  if (!sampleAnswer && !rubric) return null;
  return { ...(sampleAnswer ? { sampleAnswer } : {}), ...(rubric ? { rubric } : {}) };
}

export function buildOpenTextInspector(state: OpenTextAuthoringState): OpenTextInspector {
  return { ...state, limits: OPEN_TEXT_LIMITS, issues: openTextIssues(state).map((issue) => issue.code) };
}

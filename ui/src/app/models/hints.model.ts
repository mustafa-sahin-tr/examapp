/** Issue #1113: adım adım ipucu (Pro, `step-hints`). */
export const STEP_HINTS_FEATURE = 'step-hints';

export interface HintStep {
  step: number;
  text: string;
}

/** `totalSteps` ipucu üretilmeden null; `unavailable` bu soruda ipucu yok. `remaining` aylık haktan kalan (sınırsızda null). */
export interface HintState {
  questionId: number;
  steps: HintStep[];
  totalSteps: number | null;
  unavailable: boolean;
  remaining: number | null;
}

/**
 * Issue #777: bu cihazda tutulan çözüm durumu. Sunucuya gitmez; depolama kapalıysa ekran varsayılanlarla çalışır.
 * Soru kimlikleri `TestInstanceQuestion.id`'dir, sıra indeksi değil.
 */
export interface SolveLocalState {
  questionId: number | null;
  bookmarkedQuestionIds: number[];
  lowTimeNotified: boolean;
}

const AUTO_ADVANCE_KEY = 'testSolve.autoAdvance';
const instanceKey = (instanceId: number) => `testSolve.instance.${instanceId}`;

const EMPTY_STATE: SolveLocalState = { questionId: null, bookmarkedQuestionIds: [], lowTimeNotified: false };

export function readSolveLocalState(instanceId: number): SolveLocalState {
  try {
    const parsed = JSON.parse(localStorage.getItem(instanceKey(instanceId)) ?? 'null');
    if (!parsed || typeof parsed !== 'object') return EMPTY_STATE;
    return {
      questionId: typeof parsed.questionId === 'number' ? parsed.questionId : null,
      bookmarkedQuestionIds: Array.isArray(parsed.bookmarkedQuestionIds)
        ? parsed.bookmarkedQuestionIds.filter((id: unknown) => typeof id === 'number')
        : [],
      lowTimeNotified: parsed.lowTimeNotified === true,
    };
  } catch {
    return EMPTY_STATE;
  }
}

export function writeSolveLocalState(instanceId: number, state: SolveLocalState): void {
  try {
    localStorage.setItem(instanceKey(instanceId), JSON.stringify(state));
  } catch {
    // Depolama kapalı ya da dolu: yalnız bu cihazdaki kolaylık kaybolur.
  }
}

export function clearSolveLocalState(instanceId: number): void {
  try {
    localStorage.removeItem(instanceKey(instanceId));
  } catch {
    // Bkz. writeSolveLocalState.
  }
}

/** Otomatik geçiş varsayılan olarak kapalıdır ve cihaz başına hatırlanır. */
export function readAutoAdvance(): boolean {
  try {
    return localStorage.getItem(AUTO_ADVANCE_KEY) === '1';
  } catch {
    return false;
  }
}

export function writeAutoAdvance(enabled: boolean): void {
  try {
    localStorage.setItem(AUTO_ADVANCE_KEY, enabled ? '1' : '0');
  } catch {
    // Bkz. writeSolveLocalState.
  }
}

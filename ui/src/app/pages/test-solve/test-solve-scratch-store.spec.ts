import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  SCRATCH_MAX_AGE_MS,
  SCRATCH_TEXT_MAX_LENGTH,
  changedDrawings,
  clearScratchNotes,
  collectDrawings,
  markDrawingsSent,
  purgeOldScratchNotes,
  readInkStrokes,
  readScratchNotes,
  writeInkStrokes,
  writeScratchNotes,
} from './test-solve-scratch-store';

describe('test solve scratch store (#782)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('keeps notes per session and question', () => {
    writeScratchNotes(1, { 10: { strokes: ['M1 2L3 4'], text: 'a' } });
    writeScratchNotes(2, { 10: { strokes: [], text: 'b' } });

    expect(readScratchNotes(1)[10]).toEqual({ strokes: ['M1 2L3 4'], text: 'a' });
    expect(readScratchNotes(2)[10].text).toBe('b');
  });

  it('keeps coloured strokes and drops unknown colours (#1241)', () => {
    writeScratchNotes(1, { 10: { strokes: ['red:M1 2L3 4', 'pink:M1 2L3 4', 'M5 6L7 8'], text: '' } });

    expect(readScratchNotes(1)[10].strokes).toEqual(['red:M1 2L3 4', 'M5 6L7 8']);
  });

  it('clearing one session leaves the others', () => {
    writeScratchNotes(1, { 10: { strokes: [], text: 'a' } });
    writeScratchNotes(2, { 10: { strokes: [], text: 'b' } });

    clearScratchNotes(1);

    expect(readScratchNotes(1)).toEqual({});
    expect(readScratchNotes(2)[10].text).toBe('b');
  });

  it('ignores corrupt data and strokes it did not write', () => {
    localStorage.setItem('testSolve.scratch.3', 'not json');
    expect(readScratchNotes(3)).toEqual({});

    localStorage.setItem(
      'testSolve.scratch.4',
      JSON.stringify({ notes: { 10: { strokes: ['M1 2L3 4', 'javascript:alert(1)', 5], text: 'x'.repeat(SCRATCH_TEXT_MAX_LENGTH + 5) } } }),
    );
    const note = readScratchNotes(4)[10];
    expect(note.strokes).toEqual(['M1 2L3 4']);
    expect(note.text.length).toBe(SCRATCH_TEXT_MAX_LENGTH);
  });

  it('reports a failed write instead of throwing', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('full', 'QuotaExceededError');
    });

    expect(writeScratchNotes(1, {})).toBe(false);
  });

  it('removes only notes older than the limit and leaves other keys', () => {
    const now = 1_000_000_000_000;
    writeScratchNotes(1, {}, now - SCRATCH_MAX_AGE_MS - 1);
    writeScratchNotes(2, {}, now - SCRATCH_MAX_AGE_MS + 1);
    localStorage.setItem('testSolve.scratch.3', 'not json');
    localStorage.setItem('testSolve.instance.1', 'keep');

    purgeOldScratchNotes(now);

    expect(localStorage.getItem('testSolve.scratch.1')).toBeNull();
    expect(localStorage.getItem('testSolve.scratch.2')).not.toBeNull();
    expect(localStorage.getItem('testSolve.scratch.3')).toBeNull();
    expect(localStorage.getItem('testSolve.instance.1')).toBe('keep');
  });

  it('keeps ink per question without overwriting other questions, and clears it with the notes (#1175)', () => {
    writeInkStrokes(1, 10, ['M1 2L3 4']);
    writeInkStrokes(1, 11, ['M5 6L7 8', 'bad']);
    expect(readInkStrokes(1, 10)).toEqual(['M1 2L3 4']);
    expect(readInkStrokes(1, 11)).toEqual(['M5 6L7 8']);

    writeInkStrokes(1, 10, []);
    expect(readInkStrokes(1, 10)).toEqual([]);
    expect(readInkStrokes(1, 11)).toEqual(['M5 6L7 8']);

    clearScratchNotes(1);
    expect(readInkStrokes(1, 11)).toEqual([]);
  });

  it('purges old ink like old notes (#1175)', () => {
    writeInkStrokes(1, 10, ['M1 2L3 4'], Date.now() - SCRATCH_MAX_AGE_MS - 1000);
    writeInkStrokes(2, 10, ['M1 2L3 4']);
    purgeOldScratchNotes();
    expect(readInkStrokes(1, 10)).toEqual([]);
    expect(readInkStrokes(2, 10)).toEqual(['M1 2L3 4']);
  });

  it('collects ink and scratch notes per question and sends only what changed (#1310)', () => {
    writeScratchNotes(5, { 10: { strokes: ['red:M1 2L3 4'], text: 'not' }, 11: { strokes: [], text: '  ' } });
    writeInkStrokes(5, 10, ['M5 5L6 6']);
    writeInkStrokes(5, 12, ['blue:M7 7L8 8']);

    const items = collectDrawings(5);
    expect(items).toEqual([
      { instanceQuestionId: 10, ink: ['M5 5L6 6'], strokes: ['red:M1 2L3 4'], text: 'not' },
      { instanceQuestionId: 12, ink: ['blue:M7 7L8 8'], strokes: [], text: null },
    ]);

    const sent = new Map<number, string>();
    expect(changedDrawings(items, sent)).toHaveLength(2);
    markDrawingsSent(items, sent);
    expect(changedDrawings(collectDrawings(5), sent)).toEqual([]);

    // Silinen çizim boş öğe olarak gider (sunucu siler), sonra unutulur.
    writeInkStrokes(5, 12, []);
    const removed = changedDrawings(collectDrawings(5), sent);
    expect(removed).toEqual([{ instanceQuestionId: 12, ink: [], strokes: [], text: null }]);
    markDrawingsSent(removed, sent);
    expect(changedDrawings(collectDrawings(5), sent)).toEqual([]);
  });
});

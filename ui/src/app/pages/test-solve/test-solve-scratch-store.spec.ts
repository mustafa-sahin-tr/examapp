import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  SCRATCH_MAX_AGE_MS,
  SCRATCH_TEXT_MAX_LENGTH,
  clearScratchNotes,
  purgeOldScratchNotes,
  readScratchNotes,
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
});

import { PROGRAM_OPTION_FALLBACK_ICON, resolveProgramOptionIcon } from './program-option-icon.util';

describe('resolveProgramOptionIcon (issue #319)', () => {
  it('validSymbolName_ReturnsSameName', () => {
    expect(resolveProgramOptionIcon('timer')).toBe('timer');
    expect(resolveProgramOptionIcon('hourglass_top')).toBe('hourglass_top');
    expect(resolveProgramOptionIcon('looks_3')).toBe('looks_3');
  });

  it('legacySvgPath_FallsBackToNeutralIcon', () => {
    expect(resolveProgramOptionIcon('icons/timer.svg')).toBe(PROGRAM_OPTION_FALLBACK_ICON);
    expect(resolveProgramOptionIcon('timer.svg')).toBe(PROGRAM_OPTION_FALLBACK_ICON);
  });

  it('invalidOrEmptyValue_FallsBackToNeutralIcon', () => {
    for (const value of [undefined, null, '', '   ', 'Timer', 'quiz<script>', 'a-b', 'x y']) {
      expect(resolveProgramOptionIcon(value)).toBe(PROGRAM_OPTION_FALLBACK_ICON);
    }
  });
});

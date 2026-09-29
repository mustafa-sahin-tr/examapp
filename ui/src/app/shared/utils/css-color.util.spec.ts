import { parseCssColor, readCssToken, UNRESOLVED_CSS_COLOR, withAlpha } from './css-color.util';

describe('css-color.util (#285)', () => {
  describe('withAlpha', () => {
    const cases: [string, string][] = [
      ['#1c43fe', 'rgba(28, 67, 254, 0.3)'],
      ['#1C43FE', 'rgba(28, 67, 254, 0.3)'],
      ['#fff', 'rgba(255, 255, 255, 0.3)'],
      ['#ffff', 'rgba(255, 255, 255, 0.3)'],
      ['#1c43fe80', 'rgba(28, 67, 254, 0.151)'],
      ['  #1c43fe  ', 'rgba(28, 67, 254, 0.3)'],
    ];

    for (const [input, expected] of cases) {
      it(`withAlpha_${input.trim()}_ReturnsRgba`, () => {
        expect(withAlpha(input, 0.3)).toBe(expected);
      });
    }

    it('withAlpha_UnparseableInput_FallsBackToColorMixWithDistinctTones', () => {
      const tones = [0.1, 0.3, 0.55, 0.775].map((a) => withAlpha('currentColor', a));
      expect(tones[0]).toBe('color-mix(in srgb, currentColor 10%, transparent)');
      expect(tones[3]).toBe('color-mix(in srgb, currentColor 77.5%, transparent)');
      expect(new Set(tones).size).toBe(4);
    });

    it('withAlpha_ColorMixToken_WrapsInsteadOfReturningSameValue', () => {
      const token = 'color-mix(in srgb, #1c43fe 16%, transparent)';
      expect(withAlpha(token, 0.3)).toBe(`color-mix(in srgb, ${token} 30%, transparent)`);
      expect(withAlpha(token, 0.3)).not.toBe(withAlpha(token, 0.55));
    });

    it('withAlpha_NonHexFunctionalColor_FallsBackToColorMix', () => {
      expect(withAlpha('rgb(28, 67, 254)', 0.3)).toBe('color-mix(in srgb, rgb(28, 67, 254) 30%, transparent)');
      expect(withAlpha('hsl(230 99% 55%)', 0.55)).toBe('color-mix(in srgb, hsl(230 99% 55%) 55%, transparent)');
    });

    it('withAlpha_EmptyString_UsesCurrentColor', () => {
      expect(withAlpha('', 0.5)).toBe('color-mix(in srgb, currentColor 50%, transparent)');
    });
  });

  describe('parseCssColor', () => {
    it('parseCssColor_InvalidInputs_ReturnsNull', () => {
      for (const input of ['currentColor', '#12', '#12345', '#1234567', 'rgb(1, 2, 3)', 'hsl(0, 100%, 50%)', 'var(--x)', 'red']) {
        expect(parseCssColor(input)).withContext(input).toBeNull();
      }
    });
  });

  describe('readCssToken', () => {
    const name = '--css-color-util-spec-token';

    afterEach(() => document.documentElement.style.removeProperty(name));

    it('readCssToken_DefinedOnRoot_ReturnsTrimmedValue', () => {
      document.documentElement.style.setProperty(name, '#1c43fe');
      expect(readCssToken(name)).toBe('#1c43fe');
    });

    it('readCssToken_NestedVar_ResolvesThroughBrowser', () => {
      document.documentElement.style.setProperty('--css-color-util-spec-base', '#010203');
      document.documentElement.style.setProperty(name, 'var(--css-color-util-spec-base)');
      try {
        expect(parseCssColor(readCssToken(name))).toEqual({ r: 1, g: 2, b: 3, a: 1 });
      } finally {
        document.documentElement.style.removeProperty('--css-color-util-spec-base');
      }
    });

    it('readCssToken_Undefined_ReturnsCurrentColor', () => {
      expect(readCssToken(name)).toBe(UNRESOLVED_CSS_COLOR);
    });
  });
});

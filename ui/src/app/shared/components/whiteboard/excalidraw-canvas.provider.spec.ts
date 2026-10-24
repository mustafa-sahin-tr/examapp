import { installExcalidrawFontFallbackFilter, stripExcalidrawFontFallback } from './excalidraw-canvas.provider';

const SELF = "url(http://localhost:5678/excalidraw-assets/fonts/Excalifont/Excalifont-Regular-a88b.woff2) format('woff2')";
const ESM = "url(https://esm.sh/@excalidraw/excalidraw@0.18.1/dist/prod/fonts/Excalifont/Excalifont-Regular-a88b.woff2) format('woff2')";

describe('Excalidraw font fallback filter', () => {
    it('drops the esm.sh fallback and keeps the self-hosted source', () => {
        expect(stripExcalidrawFontFallback(`${SELF}, ${ESM}`)).toBe(SELF);
    });

    it('keeps non-Excalidraw sources untouched', () => {
        const other = "local('Inter'), url(https://fonts.gstatic.com/s/inter.woff2) format('woff2')";
        expect(stripExcalidrawFontFallback(other)).toBe(other);
    });

    it('leaves a fallback-only list as is rather than producing an empty source', () => {
        expect(stripExcalidrawFontFallback(ESM)).toBe(ESM);
    });

    it('installs a FontFace subclass once; string sources are filtered, binary sources pass through', () => {
        class RecordingFontFace {
            constructor(public family: string, public source: string | BufferSource) {}
        }
        const win = { FontFace: RecordingFontFace } as unknown as Window & typeof globalThis;

        installExcalidrawFontFallbackFilter(win);
        const installed = win.FontFace;
        installExcalidrawFontFallbackFilter(win);
        expect(win.FontFace).toBe(installed);

        const face = new win.FontFace('Excalifont', `${SELF}, ${ESM}`) as unknown as RecordingFontFace;
        expect(face).toBeInstanceOf(RecordingFontFace);
        expect(face.source).toBe(SELF);

        const buffer = new ArrayBuffer(4);
        expect((new win.FontFace('Excalifont', buffer) as unknown as RecordingFontFace).source).toBe(buffer);
    });

    it('works with the real browser FontFace (no CSP-violating source is registered)', async () => {
        const win = { FontFace: window.FontFace } as unknown as Window & typeof globalThis;
        installExcalidrawFontFallbackFilter(win);

        const face = new win.FontFace('ExcalifontTest', `${SELF}, ${ESM}`);
        expect(face).toBeInstanceOf(FontFace);
        expect(face.family).toBe('ExcalifontTest');
    });
});

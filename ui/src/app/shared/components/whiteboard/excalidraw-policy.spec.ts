import { EXCALIDRAW_STYLESHEET_HREF, ensureExcalidrawStylesheet } from './excalidraw-canvas.provider';
import { isBlockedSidebar } from './excalidraw-policy';

describe('excalidraw policy', () => {
  it('IsBlockedSidebar_DefaultSidebarAnyTab_Blocked', () => {
    expect(isBlockedSidebar({ name: 'default', tab: 'library' } as never)).toBeTrue();
    expect(isBlockedSidebar({ name: 'default', tab: 'search' } as never)).toBeTrue();
    expect(isBlockedSidebar({ name: 'default' })).toBeTrue();
    expect(isBlockedSidebar(null)).toBeFalse();
    expect(isBlockedSidebar(undefined)).toBeFalse();
    expect(isBlockedSidebar({ name: 'custom' })).toBeFalse();
  });

  it('EnsureExcalidrawStylesheet_AddsSingleLinkAndResolvesOnLoad', async () => {
    const doc = document.implementation.createHTMLDocument('t');
    let resolved = false;
    const first = ensureExcalidrawStylesheet(doc).then(() => (resolved = true));
    const second = ensureExcalidrawStylesheet(doc);

    const links = doc.head.querySelectorAll('link[rel="stylesheet"]');
    expect(links.length).toBe(1);
    expect(links[0].getAttribute('href')).toBe(EXCALIDRAW_STYLESHEET_HREF);
    await Promise.resolve();
    expect(resolved).toBeFalse();

    links[0].dispatchEvent(new Event('load'));
    await Promise.all([first, second]);
    expect(resolved).toBeTrue();

    // Yüklendikten sonra yeniden çağrı hemen çözülür, yeni link eklenmez.
    await ensureExcalidrawStylesheet(doc);
    expect(doc.head.querySelectorAll('link').length).toBe(1);
  });
});

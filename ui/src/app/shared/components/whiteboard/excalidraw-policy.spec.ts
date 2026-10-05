import { EXCALIDRAW_STYLESHEET_HREF, ensureExcalidrawStylesheet } from './excalidraw-canvas.provider';
import { EXCALIDRAW_HYPERLINK_ANCHOR_SELECTOR, guardHyperlinkAnchors, isBlockedSidebar } from './excalidraw-policy';

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

  describe('hyperlink anchor guard (issue #332)', () => {
    let host: HTMLElement;
    let anchor: HTMLAnchorElement;
    let other: HTMLElement;
    let opened: (string | null)[];
    let remove: () => void;

    beforeEach(() => {
      host = document.createElement('div');
      host.innerHTML =
        '<div class="excalidraw-hyperlinkContainer"><a class="excalidraw-hyperlinkContainer-link" href="https://evil.example.org/"><span>evil</span></a></div><button>x</button>';
      document.body.appendChild(host);
      anchor = host.querySelector(EXCALIDRAW_HYPERLINK_ANCHOR_SELECTOR) as HTMLAnchorElement;
      other = host.querySelector('button') as HTMLElement;
      opened = [];
      remove = guardHyperlinkAnchors(host, (link) => opened.push(link));
    });

    afterEach(() => {
      remove();
      host.remove();
    });

    function fire(target: Element, event: Event): Event {
      target.dispatchEvent(event);
      return event;
    }

    it('MiddleClickOnAnchorOrChild_PreventedAndRoutedToConfirmFlow', () => {
      const span = anchor.querySelector('span') as HTMLElement;
      expect(fire(span, new MouseEvent('auxclick', { button: 1, bubbles: true, cancelable: true })).defaultPrevented).toBeTrue();
      expect(opened).toEqual(['https://evil.example.org/']);
    });

    it('ContextMenuAndDragStartOnAnchor_PreventedWithoutOpening', () => {
      expect(fire(anchor, new MouseEvent('contextmenu', { bubbles: true, cancelable: true })).defaultPrevented).toBeTrue();
      expect(fire(anchor, new DragEvent('dragstart', { bubbles: true, cancelable: true })).defaultPrevented).toBeTrue();
      expect(opened).toEqual([]);
    });

    it('EventsOutsideAnchor_Untouched', () => {
      expect(fire(other, new MouseEvent('auxclick', { button: 1, bubbles: true, cancelable: true })).defaultPrevented).toBeFalse();
      expect(fire(other, new MouseEvent('contextmenu', { bubbles: true, cancelable: true })).defaultPrevented).toBeFalse();
      expect(opened).toEqual([]);
    });

    it('Remove_DetachesListeners', () => {
      remove();
      expect(fire(anchor, new MouseEvent('contextmenu', { bubbles: true, cancelable: true })).defaultPrevented).toBeFalse();
      expect(opened).toEqual([]);
    });
  });
});

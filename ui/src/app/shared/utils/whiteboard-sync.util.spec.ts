import { WhiteboardElement } from '../../models/whiteboard.model';
import {
  WHITEBOARD_MAX_ELEMENTS_PER_CHUNK,
  changedElements,
  chunkOutgoing,
  isSafeHttpUrl,
  isSyncableElement,
  openWhiteboardLink,
  parseHubErrorCode,
  parsePeerPointer,
  parseUtcDate,
  rawStampsOf,
  sanitizeIncomingElement,
  sortByFractionalIndex,
  toOutgoingElement,
} from './whiteboard-sync.util';

function el(id: string, version = 1, versionNonce = 1, extra: Record<string, unknown> = {}): WhiteboardElement {
  return { id, type: 'rectangle', version, versionNonce, isDeleted: false, link: null, ...extra } as WhiteboardElement;
}

describe('whiteboard-sync.util', () => {
  describe('changedElements', () => {
    it('ChangedElements_KnownStamps_ReturnsOnlyNewOrVersionOrNonceChanged', () => {
      const known = new Map([
        ['a', { version: 1, versionNonce: 10 }],
        ['b', { version: 1, versionNonce: 10 }],
        ['c', { version: 2, versionNonce: 10 }],
      ]);
      const result = changedElements([el('a', 1, 10), el('b', 2, 10), el('c', 2, 99), el('d')], known);
      expect(result.map((e) => e.id)).toEqual(['b', 'c', 'd']);
    });
  });

  describe('chunkOutgoing', () => {
    it('ChunkOutgoing_MoreThanMaxCount_SplitsBy500', () => {
      const items = Array.from({ length: 1200 }, (_, i) => ({ item: i, bytes: 10 }));
      const chunks = chunkOutgoing(items);
      expect(chunks.map((c) => c.length)).toEqual([WHITEBOARD_MAX_ELEMENTS_PER_CHUNK, 500, 200]);
      expect(chunks.flat()).toEqual(items.map((i) => i.item));
    });

    it('ChunkOutgoing_ByteBudgetExceeded_StartsNewChunk', () => {
      const items = [40_000, 40_000, 40_000, 1_000].map((bytes, i) => ({ item: i, bytes }));
      expect(chunkOutgoing(items, 500, 100 * 1024)).toEqual([[0, 1], [2, 3]]);
    });

    it('ChunkOutgoing_SingleItemOverBudget_GetsOwnChunk', () => {
      const items = [1, 200_000, 1].map((bytes, i) => ({ item: i, bytes }));
      expect(chunkOutgoing(items, 500, 1000)).toEqual([[0], [1], [2]]);
    });
  });

  describe('link scheme', () => {
    it('IsSafeHttpUrl_OnlyAbsoluteHttpAndHttps', () => {
      expect(isSafeHttpUrl('https://example.org/a')).toBeTrue();
      expect(isSafeHttpUrl('http://example.org')).toBeTrue();
      expect(isSafeHttpUrl('javascript:alert(1)')).toBeFalse();
      expect(isSafeHttpUrl('data:text/html,x')).toBeFalse();
      expect(isSafeHttpUrl('JAVASCRIPT:alert(1)')).toBeFalse();
      expect(isSafeHttpUrl('/relative')).toBeFalse();
      expect(isSafeHttpUrl('')).toBeFalse();
      expect(isSafeHttpUrl(null)).toBeFalse();
      expect(isSafeHttpUrl(`https://e.org/${'a'.repeat(2048)}`)).toBeFalse();
    });

    it('OpenWhiteboardLink_Https_OpensNewTabWithNoopenerNoreferrer', () => {
      const open = jasmine.createSpy('open');
      expect(openWhiteboardLink('https://example.org', open)).toBeTrue();
      expect(open).toHaveBeenCalledOnceWith('https://example.org', '_blank', 'noopener,noreferrer');
    });

    it('OpenWhiteboardLink_JavascriptScheme_BlocksAndDoesNotOpen', () => {
      const open = jasmine.createSpy('open');
      expect(openWhiteboardLink('javascript:alert(1)', open)).toBeFalse();
      expect(openWhiteboardLink('data:text/html;base64,PGI+', open)).toBeFalse();
      expect(open).not.toHaveBeenCalled();
    });
  });

  describe('element sanitation', () => {
    it('ToOutgoingElement_StripsCustomDataAndNullsUnsafeLink', () => {
      const payload = toOutgoingElement(el('a', 1, 1, { customData: { x: 1 }, link: 'javascript:alert(1)' }));
      expect('customData' in payload).toBeFalse();
      expect(payload['link']).toBeNull();
      expect(payload['id']).toBe('a');
    });

    it('ToOutgoingElement_SafeLink_Kept', () => {
      expect(toOutgoingElement(el('a', 1, 1, { link: 'https://e.org' }))['link']).toBe('https://e.org');
    });

    it('SanitizeIncoming_CleanElement_ReturnsSameInstance', () => {
      const element = el('a');
      expect(sanitizeIncomingElement(element)).toBe(element);
    });

    it('SanitizeIncoming_CustomDataAndUnsafeLink_Removed', () => {
      const result = sanitizeIncomingElement(el('a', 1, 1, { customData: { evil: true }, link: 'data:x' }));
      expect(Object.prototype.hasOwnProperty.call(result, 'customData')).toBeFalse();
      expect(result.link).toBeNull();
    });

    it('IsSyncableElement_AllowListAndNoFileId', () => {
      expect(isSyncableElement({ type: 'freedraw' })).toBeTrue();
      expect(isSyncableElement({ type: 'frame', fileId: null })).toBeTrue();
      expect(isSyncableElement({ type: 'image' })).toBeFalse();
      expect(isSyncableElement({ type: 'embeddable' })).toBeFalse();
      expect(isSyncableElement({ type: 'iframe' })).toBeFalse();
      expect(isSyncableElement({ type: 'magicframe' })).toBeFalse();
      expect(isSyncableElement({ type: 'rectangle', fileId: 'f1' })).toBeFalse();
    });
  });

  describe('remote ordering and stamps', () => {
    it('SortByFractionalIndex_StableAndMissingLast', () => {
      const sorted = sortByFractionalIndex([
        { id: 'n1' },
        { id: 'b', index: 'a1' },
        { id: 'a', index: 'a0' },
        'junk',
        { id: 'c', index: 'a1V' },
      ]);
      expect(sorted.map((item) => (item as { id?: string }).id ?? item)).toEqual(['a', 'b', 'c', 'n1', 'junk']);
    });

    it('RawStampsOf_ReadsIdVersionNonceBeforeRestore', () => {
      const stamps = rawStampsOf([
        { id: 'a', version: 3, versionNonce: 7 },
        { id: 'b', version: 2 },
        { id: 'c', version: '9' },
        { version: 1 },
        null,
      ]);
      expect([...stamps.entries()]).toEqual([
        ['a', { version: 3, versionNonce: 7 }],
        ['b', { version: 2, versionNonce: 0 }],
      ]);
    });
  });

  describe('parsing', () => {
    it('ParseHubErrorCode_HubExceptionMessage_ReturnsCode', () => {
      const error = new Error("An unexpected error occurred invoking 'JoinBoard' on the server. HubException: WindowNotOpen");
      expect(parseHubErrorCode(error)).toBe('WindowNotOpen');
      expect(parseHubErrorCode(new Error('RateLimited'))).toBe('RateLimited');
    });

    it('ParseHubErrorCode_TransportOrUnknown_ReturnsNull', () => {
      expect(parseHubErrorCode(new Error('Invocation canceled due to the underlying connection being closed.'))).toBeNull();
      expect(parseHubErrorCode(new Error('HubException: SomethingElse'))).toBeNull();
      expect(parseHubErrorCode(undefined)).toBeNull();
    });

    it('ParsePeerPointer_ValidatesRoleCoordinatesAndTool', () => {
      expect(parsePeerPointer('student', { x: 1, y: 2, tool: 'laser' })).toEqual({ x: 1, y: 2, tool: 'laser', role: 'student' });
      expect(parsePeerPointer('teacher', { x: 1, y: 2, tool: '<b>' })?.tool).toBe('pointer');
      expect(parsePeerPointer('admin', { x: 1, y: 2 })).toBeNull();
      expect(parsePeerPointer('student', { x: Number.NaN, y: 2 })).toBeNull();
      expect(parsePeerPointer('student', { x: 2_000_000, y: 2 })).toBeNull();
      expect(parsePeerPointer('student', null)).toBeNull();
    });

    it('ParseUtcDate_WithAndWithoutZone_TreatedAsUtc', () => {
      expect(parseUtcDate('2026-09-30T10:00:00Z')?.toISOString()).toBe('2026-09-30T10:00:00.000Z');
      expect(parseUtcDate('2026-09-30T10:00:00')?.toISOString()).toBe('2026-09-30T10:00:00.000Z');
      expect(parseUtcDate('2026-09-30T12:00:00+02:00')?.toISOString()).toBe('2026-09-30T10:00:00.000Z');
      expect(parseUtcDate('nope')).toBeNull();
      expect(parseUtcDate(42)).toBeNull();
    });
  });
});

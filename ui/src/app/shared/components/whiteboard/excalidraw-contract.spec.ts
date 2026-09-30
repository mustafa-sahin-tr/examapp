import { TestBed, fakeAsync, flush, flushMicrotasks, tick } from '@angular/core/testing';
import type { AppState } from '@excalidraw/excalidraw/types';

import { WhiteboardElement } from '../../../models/whiteboard.model';
import { AuthService } from '../../../services/auth.service';
import { WHITEBOARD_HUB_CONNECTION_FACTORY, WhiteboardHubConnection } from '../../../services/whiteboard-hub-connection';
import { WHITEBOARD_FLUSH_THROTTLE_MS, WhiteboardSyncService } from '../../../services/whiteboard-sync.service';
import { FakeHubConnection, hubError, joinResult } from '../../testing/whiteboard-testing';
import { reconcileRemoteElements, restoreRemoteElements } from './excalidraw-scene';
import { WhiteboardCanvas, WhiteboardRestoreOptions } from './whiteboard-canvas';

/**
 * Sözleşme testi (issue #98, review T1): GERÇEK Excalidraw `restoreElements` / `reconcileElements` (React render yok)
 * + gerçek `WhiteboardSyncService` × 2 + sunucu uzlaşma kuralını (WhiteboardStore.Wins) taklit eden bir relay.
 *
 * Not: `@excalidraw/excalidraw` kök modülü yüklenirken bağımlılığı browser-fs-access kendi lazy chunk'larını ister;
 * Karma'nın webpack (browser) modu bunları sunamadığı için konsolda "ChunkLoadError" görünür. Test sonucunu
 * etkilemez (`builderMode: application` ile görünmez).
 */

/** Uzlaşmada kullanılan appState alanları: düzenlenen/boyutlanan/yeni eleman yok. */
const IDLE_APP_STATE = { editingTextElement: null, resizingElement: null, newElement: null } as unknown as AppState;

type Json = Record<string, unknown>;

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}

/** Excalidraw'ın `restoreElements`'iyle tam (varsayılanları doldurulmuş) eleman üretir. */
function make(partial: Json): WhiteboardElement {
  const [element] = restoreRemoteElements([{ x: 0, y: 0, width: 40, height: 20, version: 1, versionNonce: 1, ...partial }]);
  if (!element) {
    throw new Error(`restore dropped ${String(partial['id'])}`);
  }
  return element;
}

function bump(element: WhiteboardElement, patch: Json, versionNonce: number): WhiteboardElement {
  return { ...element, ...patch, version: element.version + 1, versionNonce } as WhiteboardElement;
}

/** Excalidraw'ı render etmeden, gerçek saf fonksiyonlarla çalışan tuval. */
class HeadlessExcalidrawCanvas implements WhiteboardCanvas {
  scene: WhiteboardElement[] = [];
  onChange: (() => void) | null = null;

  getElements(): readonly WhiteboardElement[] {
    return this.scene;
  }

  restore(raw: readonly unknown[], options?: WhiteboardRestoreOptions): WhiteboardElement[] {
    return restoreRemoteElements(raw, options);
  }

  reconcile(local: readonly WhiteboardElement[], remote: readonly WhiteboardElement[]): WhiteboardElement[] {
    return reconcileRemoteElements(local, remote, IDLE_APP_STATE);
  }

  applyElements(elements: readonly WhiteboardElement[]): void {
    this.scene = [...elements];
    this.onChange?.();
  }

  /** Kullanıcı çizimi: sahneyi değiştirir ve Excalidraw gibi onChange tetikler. */
  draw(next: WhiteboardElement[]): void {
    this.applyElements(next);
  }

  byId(id: string): Json | undefined {
    return this.scene.find((e) => e.id === id) as unknown as Json | undefined;
  }

  setPeerPointer(): void {}
  setViewMode(): void {}
  setTheme(): void {}
  setLangCode(): void {}
  refresh(): void {}
  destroy(): void {}
}

/** Sunucu: `WhiteboardStore.Wins` (yüksek version; eşitse küçük versionNonce), yayın + corrections. */
class RelayServer {
  readonly scene = new Map<string, Json>();
  readonly rejectIds = new Set<string>();
  readonly hubs: FakeHubConnection[] = [];
  sends = 0;

  attach(hub: FakeHubConnection, role: 'teacher' | 'student'): void {
    this.hubs.push(hub);
    hub.respond = (method, args) => {
      if (method === 'JoinBoard') {
        return Promise.resolve(joinResult({ role, elements: [...this.scene.values()].map(clone) }));
      }
      if (method === 'SendElements') {
        return this.receive(hub, args[1] as Json[]);
      }
      return Promise.resolve(undefined);
    };
  }

  private receive(sender: FakeHubConnection, raw: Json[]): Promise<unknown> {
    this.sends++;
    const elements = clone(raw);
    if (elements.some((e) => this.rejectIds.has(e['id'] as string))) {
      return Promise.reject(hubError('InvalidElement'));
    }
    const accepted: Json[] = [];
    const corrections: Json[] = [];
    for (const candidate of elements) {
      const existing = this.scene.get(candidate['id'] as string);
      if (!existing || wins(candidate, existing)) {
        this.scene.set(candidate['id'] as string, candidate);
        accepted.push(candidate);
      } else if (candidate['version'] !== existing['version'] || candidate['versionNonce'] !== existing['versionNonce']) {
        corrections.push(clone(existing));
      }
    }
    if (accepted.length > 0) {
      for (const hub of this.hubs.filter((h) => h !== sender)) {
        hub.emit('ElementsUpdated', clone(accepted));
      }
    }
    return Promise.resolve({ serverVersion: this.sends, accepted: accepted.length, corrections });
  }
}

function wins(candidate: Json, existing: Json): boolean {
  const cv = candidate['version'] as number;
  const ev = existing['version'] as number;
  return cv > ev || (cv === ev && (candidate['versionNonce'] as number) < (existing['versionNonce'] as number));
}

describe('Excalidraw contract (real restore/reconcile)', () => {
  let server: RelayServer;
  let queue: WhiteboardHubConnection[];
  let a: { service: WhiteboardSyncService; canvas: HeadlessExcalidrawCanvas; hub: FakeHubConnection };
  let b: { service: WhiteboardSyncService; canvas: HeadlessExcalidrawCanvas; hub: FakeHubConnection };

  beforeEach(() => {
    queue = [];
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { getToken: () => 'tkn', isExpiringSoon: () => false } },
        { provide: WHITEBOARD_HUB_CONNECTION_FACTORY, useValue: () => queue.shift() },
      ],
    });
    server = new RelayServer();
  });

  function client(role: 'teacher' | 'student') {
    const hub = new FakeHubConnection();
    server.attach(hub, role);
    queue.push(hub);
    const canvas = new HeadlessExcalidrawCanvas();
    const service = TestBed.runInInjectionContext(() => new WhiteboardSyncService());
    canvas.onChange = () => service.notifyLocalChange();
    service.start(7, canvas);
    flushMicrotasks();
    return { service, canvas, hub };
  }

  /** Kısılmış gönderimleri ve yayınları sessizliğe kadar işletir; kaç tur sürdüğünü döner. */
  function settle(maxRounds = 30): number {
    let quietSince = server.sends;
    for (let round = 1; round <= maxRounds; round++) {
      tick(WHITEBOARD_FLUSH_THROTTLE_MS);
      flushMicrotasks();
      if (server.sends === quietSince) {
        return round;
      }
      quietSince = server.sends;
    }
    throw new Error('did not settle');
  }

  function snapshot(canvas: HeadlessExcalidrawCanvas): string[] {
    return canvas.scene
      .map((e) => {
        const j = e as unknown as Json;
        return `${e.id}:${e.version}:${e.versionNonce}:${String(j['index'])}:${e.isDeleted}`;
      })
      .sort();
  }

  function finish(): void {
    a?.service.destroy();
    b?.service.destroy();
    flush();
  }

  it('(a) BoundTextAndArrowSentAsPart_BindingsPreserved', fakeAsync(() => {
    const box = make({ id: 'box', type: 'rectangle', index: 'a0', boundElements: [{ id: 't', type: 'text' }, { id: 'arr', type: 'arrow' }] });
    const text = make({ id: 't', type: 'text', index: 'a1', text: 'hi', originalText: 'hi', containerId: 'box' });
    const arrow = make({
      id: 'arr',
      type: 'arrow',
      index: 'a2',
      points: [[0, 0], [80, 0]],
      startBinding: { elementId: 'box', focus: 0, gap: 1 },
    });
    for (const e of [box, text, arrow]) {
      server.scene.set(e.id, clone(e) as unknown as Json);
    }
    a = client('teacher');
    b = client('student');
    expect((b.canvas.byId('t') ?? {})['containerId']).toBe('box');

    // Yalnızca metin ve ok değişiyor: kap (box) parçada YOK.
    a.canvas.draw(a.canvas.scene.map((e) =>
      e.id === 't' ? bump(e, { text: 'hello', originalText: 'hello' }, 11) : e.id === 'arr' ? bump(e, { x: 3 }, 12) : e
    ));
    settle();

    expect(server.sends).toBeGreaterThan(0);
    const receivedText = b.canvas.byId('t') as Json;
    expect(receivedText['text']).toBe('hello');
    expect(receivedText['containerId']).toBe('box');
    expect((b.canvas.byId('arr') as Json)['startBinding']).toEqual(jasmine.objectContaining({ elementId: 'box' }));
    expect(((b.canvas.byId('box') as Json)['boundElements'] as Json[]).map((x) => x['id'])).toEqual(['t', 'arr']);

    // K1'in kanıtı: aynı parça bağ onarımıyla restore edilseydi bağlar silinirdi.
    const repaired = restoreRemoteElements(clone([receivedText]), { repairBindings: true });
    expect((repaired[0] as unknown as Json)['containerId']).toBeNull();
    finish();
  }));

  it('(b) BothSidesCreateSameIndex_ConvergeInFiniteRounds', fakeAsync(() => {
    const base = make({ id: 'r0', type: 'rectangle', index: 'a0' });
    server.scene.set('r0', clone(base) as unknown as Json);
    a = client('teacher');
    b = client('student');

    // İki taraf aynı anda aynı fractional index'i üretir (ör. ikisi de sahnenin sonuna ekledi).
    a.canvas.draw([...a.canvas.scene, make({ id: 'x', type: 'ellipse', index: 'a1', versionNonce: 5 })]);
    b.canvas.draw([...b.canvas.scene, make({ id: 'y', type: 'diamond', index: 'a1', versionNonce: 9 })]);
    const rounds = settle();

    expect(rounds).toBeLessThan(15);
    expect(snapshot(a.canvas)).toEqual(snapshot(b.canvas));
    const indices = a.canvas.scene.map((e) => (e as unknown as Json)['index'] as string);
    expect(new Set(indices).size).toBe(indices.length);
    expect([...indices].sort()).toEqual(indices);
    // Sunucu da aynı sahnede.
    expect([...server.scene.keys()].sort()).toEqual(['r0', 'x', 'y']);
    for (const element of a.canvas.scene) {
      expect(server.scene.get(element.id)?.['version']).toBe(element.version);
    }
    finish();
  }));

  it('(c) Tombstone_AppliedOnPeer', fakeAsync(() => {
    const x = make({ id: 'x', type: 'rectangle', index: 'a0' });
    server.scene.set('x', clone(x) as unknown as Json);
    a = client('teacher');
    b = client('student');

    a.canvas.draw(a.canvas.scene.map((e) => (e.id === 'x' ? bump(e, { isDeleted: true }, 3) : e)));
    settle();

    expect(b.canvas.byId('x')?.['isDeleted']).toBeTrue();
    expect(b.canvas.byId('x')?.['version']).toBe(2);
    expect(server.scene.get('x')?.['isDeleted']).toBeTrue();
    finish();
  }));

  it('(d) RejectedElementInPart_OthersDeliveredBadStaysLocalAndIsNotResent', fakeAsync(() => {
    a = client('teacher');
    b = client('student');
    server.rejectIds.add('bad');
    spyOn(console, 'warn');

    a.canvas.draw([
      make({ id: 'e1', type: 'rectangle', index: 'a0' }),
      make({ id: 'bad', type: 'rectangle', index: 'a1' }),
      make({ id: 'e2', type: 'ellipse', index: 'a2' }),
      make({ id: 'e3', type: 'diamond', index: 'a3' }),
    ]);
    settle();
    const sendsAfterFirstSettle = server.sends;

    expect(b.canvas.scene.map((e) => e.id).sort()).toEqual(['e1', 'e2', 'e3']);
    expect(a.canvas.byId('bad')).toBeDefined();
    expect(server.scene.has('bad')).toBeFalse();
    expect(console.warn).toHaveBeenCalledWith(jasmine.stringMatching(/bad \(rectangle\) rejected by server: InvalidElement/));

    // Yeni bir yerel değişiklik reddedilen elemanı yeniden göndermez.
    a.canvas.draw([...a.canvas.scene, make({ id: 'e4', type: 'rectangle', index: 'a4' })]);
    settle();
    expect(server.sends).toBe(sendsAfterFirstSettle + 1);
    expect(b.canvas.byId('e4')).toBeDefined();
    finish();
  }));
});

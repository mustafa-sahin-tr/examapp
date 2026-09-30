import { WhiteboardElement, WhiteboardJoinResult, WhiteboardPeerPointer } from '../../models/whiteboard.model';
import { WhiteboardHubConnection } from '../../services/whiteboard-hub-connection';
import {
  WhiteboardCanvas,
  WhiteboardCanvasCallbacks,
  WhiteboardCanvasMount,
  WhiteboardCanvasOptions,
  WhiteboardLangCode,
  WhiteboardRestoreOptions,
  WhiteboardTheme,
} from '../components/whiteboard/whiteboard-canvas';

/** Issue #98 testleri: SignalR ve Excalidraw'ın sahteleri (gerçek tuval Karma'da render edilmez). */

export interface Invocation {
  method: string;
  args: unknown[];
}

/** `HubException` metnini taklit eden hata. */
export function hubError(code: string): Error {
  return new Error(`An unexpected error occurred invoking 'X' on the server. HubException: ${code}`);
}

export function joinResult(overrides: Partial<WhiteboardJoinResult> = {}): WhiteboardJoinResult {
  return {
    boardId: 'board-7',
    elements: [],
    serverVersion: 0,
    role: 'teacher',
    windowClosesAtUtc: new Date(Date.now() + 60 * 60_000).toISOString(),
    peerOnline: false,
    ...overrides,
  };
}

export function element(id: string, version = 1, extra: Record<string, unknown> = {}): WhiteboardElement {
  return {
    id,
    type: 'rectangle',
    version,
    versionNonce: 100,
    isDeleted: false,
    link: null,
    ...extra,
  } as WhiteboardElement;
}

/** Sahte hub bağlantısı: çağrıları kaydeder, yanıtları `respond` ile belirlenir, olayları `emit` ile tetikler. */
export class FakeHubConnection implements WhiteboardHubConnection {
  readonly invocations: Invocation[] = [];
  readonly sends: Invocation[] = [];
  startCalls = 0;
  stopCalls = 0;
  /** start/stop çağrı sırası. */
  readonly lifecycle: string[] = [];
  /** `true` iken stop() elle çözülene kadar bekler (`releaseStop`). */
  holdStop = false;
  private pendingStop: (() => void) | null = null;
  startError: Error | null = null;
  /** `accessTokenFactory` (fabrika tarafından yazılır). */
  tokenFactory: (() => Promise<string>) | null = null;

  /** Yöntem başına yanıt üreticisi; varsayılan: JoinBoard → joinResult(), SendElements → boş corrections. */
  respond: (method: string, args: unknown[]) => Promise<unknown> = (method, args) => {
    if (method === 'JoinBoard') {
      return Promise.resolve(joinResult());
    }
    if (method === 'SendElements') {
      return Promise.resolve({ serverVersion: 1, accepted: (args[1] as unknown[]).length, corrections: [] });
    }
    return Promise.resolve(undefined);
  };

  private readonly handlers = new Map<string, (...args: unknown[]) => void>();
  private reconnecting: ((error?: Error) => void) | null = null;
  private reconnected: ((id?: string) => void) | null = null;
  private closed: ((error?: Error) => void) | null = null;

  start(): Promise<void> {
    this.startCalls++;
    this.lifecycle.push('start');
    return this.startError ? Promise.reject(this.startError) : Promise.resolve();
  }

  stop(): Promise<void> {
    this.stopCalls++;
    this.lifecycle.push('stop');
    if (this.holdStop) {
      return new Promise((resolve) => (this.pendingStop = resolve));
    }
    return Promise.resolve();
  }

  releaseStop(): void {
    this.lifecycle.push('stopped');
    this.pendingStop?.();
    this.pendingStop = null;
  }

  invoke<T>(method: string, ...args: unknown[]): Promise<T> {
    this.invocations.push({ method, args });
    return this.respond(method, args) as Promise<T>;
  }

  send(method: string, ...args: unknown[]): Promise<void> {
    this.sends.push({ method, args });
    return Promise.resolve();
  }

  on(method: string, handler: (...args: unknown[]) => void): void {
    this.handlers.set(method, handler);
  }

  onreconnecting(callback: (error?: Error) => void): void {
    this.reconnecting = callback;
  }

  onreconnected(callback: (id?: string) => void): void {
    this.reconnected = callback;
  }

  onclose(callback: (error?: Error) => void): void {
    this.closed = callback;
  }

  emit(method: string, ...args: unknown[]): void {
    this.handlers.get(method)?.(...args);
  }

  simulateReconnecting(): void {
    this.reconnecting?.(new Error('lost'));
  }

  simulateReconnected(): void {
    this.reconnected?.('c2');
  }

  simulateClose(): void {
    this.closed?.(new Error('closed'));
  }

  calls(method: string): Invocation[] {
    return this.invocations.filter((i) => i.method === method);
  }

  /** `SendElements` ile gönderilen eleman id'leri (çağrı başına). */
  sentIds(): string[][] {
    return this.calls('SendElements').map((c) => (c.args[1] as { id: string }[]).map((e) => e.id));
  }
}

/**
 * Sahte tuval: `restore` yalnızca id/version taşıyan nesneleri kabul eder, `reconcile` Excalidraw kuralını uygular
 * (yüksek version; eşitse küçük versionNonce). `applyElements` Excalidraw gibi `onChange` tetikler.
 */
export class FakeCanvas implements WhiteboardCanvas {
  elements: WhiteboardElement[] = [];
  readonly applied: WhiteboardElement[][] = [];
  readonly viewModes: boolean[] = [];
  readonly themes: WhiteboardTheme[] = [];
  readonly langCodes: WhiteboardLangCode[] = [];
  /** Her `restore` çağrısının seçenekleri (bağ onarımı yalnızca tam sahnede). */
  readonly restoreOptions: (WhiteboardRestoreOptions | undefined)[] = [];
  refreshCalls = 0;
  /** `true` iken bir sonraki `applyElements` hata fırlatır (atomik uygulama testi). */
  failNextApply = false;
  peer: { pointer: WhiteboardPeerPointer | null; label: string } | null = null;
  destroyed = false;
  restoreCalls = 0;
  /** `restore`'a verilen id sırası (son çağrı). */
  lastRestoreOrder: string[] = [];
  /** Bu id'ler restore'da "onarılır" (Excalidraw syncInvalidIndices gibi version artar). */
  readonly repairOnRestore = new Set<string>();
  /** Bu id'ler yerelde düzenleniyor: reconcile uzak hâli atar (Excalidraw editingTextElement kuralı). */
  readonly editingIds = new Set<string>();
  onChange: (() => void) | null = null;

  getElements(): readonly WhiteboardElement[] {
    return this.elements;
  }

  restore(raw: readonly unknown[], options?: WhiteboardRestoreOptions): WhiteboardElement[] {
    this.restoreCalls++;
    this.restoreOptions.push(options);
    const valid = raw
      .filter((r): r is Record<string, unknown> => !!r && typeof r === 'object')
      .filter((r) => typeof r['id'] === 'string' && typeof r['version'] === 'number');
    this.lastRestoreOrder = valid.map((r) => r['id'] as string);
    return valid.map((r) => {
      const repaired = this.repairOnRestore.has(r['id'] as string);
      return {
        ...r,
        type: typeof r['type'] === 'string' ? r['type'] : 'rectangle',
        version: (r['version'] as number) + (repaired ? 1 : 0),
        versionNonce: repaired ? 424242 : typeof r['versionNonce'] === 'number' ? r['versionNonce'] : 0,
        isDeleted: r['isDeleted'] === true,
      } as unknown as WhiteboardElement;
    });
  }

  reconcile(local: readonly WhiteboardElement[], remote: readonly WhiteboardElement[]): WhiteboardElement[] {
    const result = [...local];
    for (const incoming of remote) {
      const index = result.findIndex((e) => e.id === incoming.id);
      if (index < 0) {
        result.push(incoming);
        continue;
      }
      const current = result[index];
      if (this.editingIds.has(current.id)) {
        continue;
      }
      const wins =
        incoming.version > current.version ||
        (incoming.version === current.version && incoming.versionNonce < current.versionNonce);
      if (wins) {
        result[index] = incoming;
      }
    }
    return result;
  }

  applyElements(elements: readonly WhiteboardElement[]): void {
    if (this.failNextApply) {
      this.failNextApply = false;
      throw new Error('apply failed');
    }
    this.elements = [...elements];
    this.applied.push([...elements]);
    this.onChange?.();
  }

  setPeerPointer(pointer: WhiteboardPeerPointer | null, label: string): void {
    this.peer = { pointer, label };
  }

  setViewMode(readOnly: boolean): void {
    this.viewModes.push(readOnly);
  }

  setTheme(theme: WhiteboardTheme): void {
    this.themes.push(theme);
  }

  setLangCode(langCode: WhiteboardLangCode): void {
    this.langCodes.push(langCode);
  }

  refresh(): void {
    this.refreshCalls++;
  }

  destroy(): void {
    this.destroyed = true;
  }
}

/** `WHITEBOARD_CANVAS_LOADER` sahtesi: mount argümanlarını kaydeder, verilen tuvali döner. */
export class FakeCanvasLoader {
  loadCalls = 0;
  mountCalls: {
    host: HTMLElement;
    options: WhiteboardCanvasOptions;
    callbacks: WhiteboardCanvasCallbacks;
    signal?: AbortSignal;
  }[] = [];
  fail = false;
  /** `true` iken mount hiç hazır olmaz (Excalidraw ilk sahneyi kuramadı); yalnızca iptal ile reddedilir. */
  hang = false;

  constructor(readonly canvas: FakeCanvas) {}

  readonly load = (): Promise<WhiteboardCanvasMount> => {
    this.loadCalls++;
    if (this.fail) {
      return Promise.reject(new Error('chunk load failed'));
    }
    return Promise.resolve((host, options, callbacks, signal) => {
      this.mountCalls.push({ host, options, callbacks, signal });
      if (this.hang) {
        return new Promise<WhiteboardCanvas>((_, reject) =>
          signal?.addEventListener('abort', () => reject(new Error('aborted')), { once: true })
        );
      }
      this.canvas.onChange = () => callbacks.onChange();
      return Promise.resolve(this.canvas);
    });
  };
}

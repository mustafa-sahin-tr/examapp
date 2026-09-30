import { Injectable, computed, inject, signal } from '@angular/core';
import { Subject, firstValueFrom } from 'rxjs';

import {
  WhiteboardCloseReason,
  WhiteboardConnectionStatus,
  WhiteboardElement,
  WhiteboardErrorCode,
  WhiteboardJoinResult,
  WhiteboardPeerPointer,
  WhiteboardPointerTool,
  WhiteboardRole,
  WhiteboardSendResult,
  WhiteboardWarning,
} from '../models/whiteboard.model';
import { WhiteboardCanvas } from '../shared/components/whiteboard/whiteboard-canvas';
import {
  ElementStamp,
  OutgoingItem,
  WHITEBOARD_MAX_ELEMENT_BYTES,
  changedElements,
  chunkOutgoing,
  isCloseReason,
  isCoordinate,
  isSyncableElement,
  isWhiteboardRole,
  jsonByteLength,
  parseHubErrorCode,
  parsePeerPointer,
  parseUtcDate,
  rawStampsOf,
  sameStamp,
  sanitizeIncomingElement,
  sortByFractionalIndex,
  stampOf,
  toOutgoingElement,
} from '../shared/utils/whiteboard-sync.util';
import { AuthService } from './auth.service';
import { WHITEBOARD_HUB_CONNECTION_FACTORY, WhiteboardHubConnection } from './whiteboard-hub-connection';

/** Yerel değişikliklerin gönderim kısması (ms). */
export const WHITEBOARD_FLUSH_THROTTLE_MS = 80;
/** İmleç gönderim kısması (ms) — ~20/sn (sunucu sınırı ile aynı). */
export const WHITEBOARD_POINTER_THROTTLE_MS = 50;
/** `RateLimited` sonrası sessiz yeniden deneme gecikmesi (ms). */
export const WHITEBOARD_RATE_LIMIT_RETRY_MS = 600;
/** Otomatik yeniden bağlanma tükendikten sonraki deneme gecikmeleri (ms); son değer tekrarlanır. */
export const WHITEBOARD_RECONNECT_DELAYS_MS = [2000, 5000, 10000, 30000];
/**
 * Aynı eleman art arda bu kadar kez uygulama sırasında onarılırsa (Excalidraw index onarımı → version artışı) ya da
 * düzenleme yüzünden düzeltmesi reddedilirse yeniden gönderme durdurulur (taraflar arası ping-pong'a devre kesici).
 */
export const WHITEBOARD_MAX_REPAIR_ROUNDS = 5;

/** Sunucunun içerik yüzünden reddettiği parça kodları: parça ikiye bölünüp yeniden denenir. */
const CONTENT_REJECTION_CODES: ReadonlySet<WhiteboardErrorCode> = new Set<WhiteboardErrorCode>([
  'SceneElementLimit',
  'SceneSizeLimit',
  'InvalidElement',
  'ElementTypeNotAllowed',
  'ImagesNotSupported',
  'TooManyElementsInMessage',
]);

/** Uzak elemanın kaynağı: tam sahne (JoinBoard), karşı taraf yayını ya da SendElements düzeltmesi. */
type RemoteSource = 'join' | 'update' | 'correction';

/** Yerel sahneden bir gönderim kalemi: eleman + sunucuya giden temiz kopya. */
interface OutgoingElement {
  readonly element: WhiteboardElement;
  readonly payload: Record<string, unknown>;
}

type SendOutcome = 'ok' | 'abort';

type StampHistory = readonly (readonly [string, ElementStamp | undefined])[];

/**
 * Ortak tahta senkronizasyonu (issue #98): `/hub/whiteboard` bağlantısı, `JoinBoard` ile sahne uzlaşması,
 * değişen elemanların kısılmış/parçalı gönderimi, uzak güncellemelerin (ve `corrections`'ın) Excalidraw
 * `restoreElements` + `reconcileElements` ile uygulanması, imleç ve çevrimiçi durumu.
 *
 * Echo yok: son gönderilen/alınan her elemanın `version`/`versionNonce` damgası `known`'da tutulur; uzaktan gelip
 * sahneye giren eleman damgalanır ve bir sonraki `onChange`'te "değişmiş" sayılmaz.
 *
 * Yeniden bağlanma: her başarılı (yeniden) bağlanmada `JoinBoard` çağrılır, `known` sunucu sahnesinden yeniden
 * kurulur. Böylece kopukken çizilenler (ya da API yeniden başlayıp sahne boşaldıysa tüm yerel sahne) bir sonraki
 * gönderimde parçalara bölünerek yeniden gönderilir.
 *
 * Komponent düzeyinde sağlanır (`providers: [WhiteboardSyncService]`) — her tahta kendi örneğini alır.
 */
@Injectable()
export class WhiteboardSyncService {
  private readonly auth = inject(AuthService);
  private readonly createConnection = inject(WHITEBOARD_HUB_CONNECTION_FACTORY);

  private readonly statusState = signal<WhiteboardConnectionStatus>('connecting');
  private readonly roleState = signal<WhiteboardRole | null>(null);
  private readonly peerOnlineState = signal(false);
  private readonly windowClosesAtState = signal<Date | null>(null);
  private readonly closedReasonState = signal<WhiteboardCloseReason | null>(null);
  private readonly errorState = signal<WhiteboardErrorCode | null>(null);
  private readonly peerPointerState = signal<WhiteboardPeerPointer | null>(null);

  readonly status = this.statusState.asReadonly();
  /** Çağıranın rolü (`JoinBoard` yanıtı). */
  readonly role = this.roleState.asReadonly();
  readonly peerOnline = this.peerOnlineState.asReadonly();
  /** Tahtanın en geç kapanacağı an (UTC). */
  readonly windowClosesAt = this.windowClosesAtState.asReadonly();
  /** `BoardClosed(reason)` nedeni; tanınmayan/koddan gelen kapanışta `null` (genel mesaj). */
  readonly closedReason = this.closedReasonState.asReadonly();
  /** Katılımı engelleyen hata (ör. `WindowNotOpen`); otomatik yeniden deneme yapılmaz, `retry()` beklenir. */
  readonly error = this.errorState.asReadonly();
  readonly peerPointer = this.peerPointerState.asReadonly();
  /** Tahta kapandı → salt okunur, yeniden bağlanılmaz. */
  readonly readOnly = computed(() => this.statusState() === 'closed');
  /** Karşı tarafın rolü. */
  readonly peerRole = computed<WhiteboardRole | null>(() => {
    const role = this.roleState();
    return role === null ? null : role === 'teacher' ? 'student' : 'teacher';
  });

  private readonly warningsSubject = new Subject<WhiteboardWarning>();
  /** Akışı durdurmayan kullanıcı uyarıları (snackbar). */
  readonly warnings$ = this.warningsSubject.asObservable();

  private connection: WhiteboardHubConnection | null = null;
  private canvas: WhiteboardCanvas | null = null;
  private bookingId = 0;
  private joined = false;
  private everJoined = false;
  private destroyed = false;
  private joinGeneration = 0;
  private reconnectAttempt = 0;

  /** Son gönderilen ya da alınan eleman damgaları. */
  private known = new Map<string, ElementStamp>();
  private sending = false;
  private flushRequested = false;
  private readonly oversizeWarned = new Set<string>();
  /** Bir gönderim turunda (flush) zaten gösterilen uyarılar — parça bölmede aynı uyarı tekrarlanmasın. */
  private readonly warnedThisFlush = new Set<WhiteboardWarning>();
  /** id → art arda onarım/red turu (devre kesici). */
  private readonly repairStreak = new Map<string, number>();
  /** Uzak güncelleme uygulanamadı ve tam senkron (yeniden JoinBoard) istendi; döngüyü önler. */
  private resyncRequested = false;
  /** `retry()` bağlantıyı durdururken gelen onclose yeniden bağlanma planlamasın. */
  private restarting = false;

  private flushTimer: ReturnType<typeof setTimeout> | null = null;
  private sendRetryTimer: ReturnType<typeof setTimeout> | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private pointerTimer: ReturnType<typeof setTimeout> | null = null;
  private pendingPointer: { x: number; y: number; tool: WhiteboardPointerTool } | null = null;

  /** Bağlanır ve tahtaya katılır. Tuval hazır olduktan sonra bir kez çağrılır. */
  start(bookingId: number, canvas: WhiteboardCanvas): void {
    if (this.connection || this.destroyed) {
      return;
    }
    this.bookingId = bookingId;
    this.canvas = canvas;

    const connection = this.createConnection(() => this.accessToken());
    this.connection = connection;

    connection.on('ElementsUpdated', (elements) => this.onRemoteElements(elements));
    connection.on('PointerUpdated', (role, pointer) => this.onPeerPointer(role, pointer));
    connection.on('BoardClosed', (reason) => this.closeBoard(isCloseReason(reason) ? reason : null));
    connection.on('PeerPresenceChanged', (role, online) => this.onPresence(role, online));
    connection.onreconnecting(() => {
      this.joined = false;
      if (!this.isTerminal()) {
        this.statusState.set('reconnecting');
      }
    });
    connection.onreconnected(() => void this.join());
    connection.onclose(() => this.onConnectionClosed());

    void this.connect();
  }

  /**
   * Engelleyici hatadan ya da bağlantı kopukken elle yeniden dener. Kapanmış tahtada etkisizdir. Önce bağlantının
   * durması beklenir (yarım kalan stop ile start çakışmasın).
   */
  async retry(): Promise<void> {
    const connection = this.connection;
    if (this.destroyed || this.statusState() === 'closed' || !connection) {
      return;
    }
    this.clearTimer('retryTimer');
    this.reconnectAttempt = 0;
    this.restarting = true;
    try {
      await connection.stop();
    } catch {
      // Zaten durmuş olabilir.
    } finally {
      this.restarting = false;
    }
    if (this.destroyed || this.statusState() === 'closed' || this.connection !== connection) {
      return;
    }
    this.errorState.set(null);
    await this.connect();
  }

  /** Tuval değişti (Excalidraw `onChange`) — kısılmış gönderim planlanır. */
  notifyLocalChange(): void {
    if (this.flushTimer !== null || this.destroyed || this.statusState() === 'closed') {
      return;
    }
    this.flushTimer = setTimeout(() => {
      this.flushTimer = null;
      void this.flush();
    }, WHITEBOARD_FLUSH_THROTTLE_MS);
  }

  /** Yerel imleç (sahne koordinatı) — ~20/sn ile en güncel konum gönderilir. */
  notifyLocalPointer(pointer: { x: number; y: number; tool: WhiteboardPointerTool }): void {
    if (!this.joined || this.statusState() === 'closed') {
      return;
    }
    this.pendingPointer = pointer;
    if (this.pointerTimer !== null) {
      return;
    }
    this.pointerTimer = setTimeout(() => {
      this.pointerTimer = null;
      this.sendPointer();
    }, WHITEBOARD_POINTER_THROTTLE_MS);
  }

  /** Bağlantıyı kapatır (komponent yıkılırken). Sahne sunucuda pencere boyunca korunur. */
  destroy(): void {
    if (this.destroyed) {
      return;
    }
    this.destroyed = true;
    this.joined = false;
    this.clearAllTimers();
    const connection = this.connection;
    this.connection = null;
    this.canvas = null;
    connection?.stop().catch(() => undefined);
  }

  // ---------------------------------------------------------------- bağlantı

  private async connect(): Promise<void> {
    const connection = this.connection;
    if (!connection || this.destroyed || this.isTerminal()) {
      return;
    }
    this.statusState.set(this.everJoined ? 'reconnecting' : 'connecting');
    try {
      await connection.start();
    } catch {
      if (!this.destroyed && !this.isTerminal()) {
        this.scheduleReconnect();
      }
      return;
    }
    await this.join();
  }

  private scheduleReconnect(): void {
    this.statusState.set('disconnected');
    const delays = WHITEBOARD_RECONNECT_DELAYS_MS;
    const delay = delays[Math.min(this.reconnectAttempt, delays.length - 1)];
    this.reconnectAttempt++;
    this.clearTimer('retryTimer');
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      void this.connect();
    }, delay);
  }

  private onConnectionClosed(): void {
    this.joined = false;
    if (this.restarting) {
      return;
    }
    // Kapanmış tahta, engelleyici hata ya da yıkım: yeniden bağlanılmaz. Aksi halde (ör. token süresi doldu ve sunucu
    // CloseOnAuthenticationExpiration ile kapattı) taze token ile yeniden bağlanılır.
    if (this.destroyed || this.isTerminal()) {
      return;
    }
    this.scheduleReconnect();
  }

  private async join(): Promise<void> {
    const connection = this.connection;
    if (!connection || this.destroyed || this.isTerminal()) {
      return;
    }
    const generation = ++this.joinGeneration;
    this.joined = false;

    let result: WhiteboardJoinResult;
    try {
      result = await connection.invoke<WhiteboardJoinResult>('JoinBoard', this.bookingId);
    } catch (error) {
      if (generation === this.joinGeneration && !this.destroyed && !this.isTerminal()) {
        this.onJoinFailed(parseHubErrorCode(error));
      }
      return;
    }
    if (generation !== this.joinGeneration || this.destroyed || this.isTerminal() || !this.canvas) {
      return;
    }

    this.roleState.set(isWhiteboardRole(result?.role) ? result.role : null);
    this.peerOnlineState.set(result?.peerOnline === true);
    this.windowClosesAtState.set(parseUtcDate(result?.windowClosesAtUtc));

    // Sunucu sahnesi yeni referans: sunucuda olmayan ya da sunucudakinden yeni her yerel eleman "değişmiş" sayılır.
    this.known = new Map();
    this.repairStreak.clear();
    if (this.applyRemote(Array.isArray(result?.elements) ? result.elements : [], 'join')) {
      this.resyncRequested = false;
    }

    this.joined = true;
    this.everJoined = true;
    this.reconnectAttempt = 0;
    this.statusState.set('connected');
    void this.flush();
  }

  private onJoinFailed(code: WhiteboardErrorCode | null): void {
    switch (code) {
      case 'RateLimited':
        // Sessiz yeniden deneme.
        this.clearTimer('retryTimer');
        this.retryTimer = setTimeout(() => {
          this.retryTimer = null;
          void this.join();
        }, WHITEBOARD_RATE_LIMIT_RETRY_MS);
        return;
      case 'WindowClosed':
        this.closeBoard('WindowClosed');
        return;
      // Issue #298: öğretmen askıda/onaysız — tahta salt okunur kapanır, otomatik yeniden denenmez.
      case 'TeacherUnavailable':
        this.closeBoard('TeacherUnavailable');
        return;
      case 'BoardClosed':
        this.closeBoard(null);
        return;
      case null:
        // Taşıma hatası (bağlantı düşüyor): onreconnecting/onclose akışı yeniden katılır.
        return;
      default:
        // WindowNotOpen, NotParticipant, BookingNotApproved ...: kullanıcıya gösterilir, otomatik denenmez.
        this.errorState.set(code);
        this.statusState.set('disconnected');
        this.connection?.stop().catch(() => undefined);
    }
  }

  private closeBoard(reason: WhiteboardCloseReason | null): void {
    if (this.statusState() === 'closed' || this.destroyed) {
      return;
    }
    this.closedReasonState.set(reason);
    this.statusState.set('closed');
    this.joined = false;
    this.peerPointerState.set(null);
    this.clearAllTimers();
    this.connection?.stop().catch(() => undefined);
  }

  /** Kapanmış tahta ya da engelleyici hata: otomatik yeniden bağlanma yok. */
  private isTerminal(): boolean {
    return this.statusState() === 'closed' || this.errorState() !== null;
  }

  private async accessToken(): Promise<string> {
    const token = this.auth.getToken();
    if (!token) {
      return '';
    }
    if (!this.auth.isExpiringSoon(token)) {
      return token;
    }
    try {
      // CloseOnAuthenticationExpiration: süresi dolmak üzere olan token ile bağlanılmaz (interceptor ile aynı akış).
      const fresh = await firstValueFrom(this.auth.refreshToken());
      if (fresh) {
        localStorage.setItem('auth_token', fresh);
        return fresh;
      }
    } catch {
      // refreshToken oturumu kendisi temizler ve yönlendirir.
    }
    return token;
  }

  // ---------------------------------------------------------------- uzak → yerel

  private onRemoteElements(raw: unknown): void {
    if (!Array.isArray(raw) || this.statusState() === 'closed') {
      return;
    }
    this.applyRemote(raw, 'update');
  }

  /**
   * Uzak elemanları (ElementsUpdated, corrections, JoinBoard sahnesi) uygular. Başarılıysa `true`.
   * 1. fractional index'e göre sıralanır (restore gereksiz yere index onarıp version artırmasın),
   * 2. her eleman `restoreElements`'ten geçer — bağ onarımı YALNIZCA tam sahnede (parçada kabı olmayan bağlı metin/ok
   *    bağını kaybederdi); izinsiz tipler düşer, customData/güvensiz link temizlenir,
   * 3. yerel sahneyle `reconcileElements` ile birleştirilip uygulanır. 1-3 tek `try` içinde: hata olursa hiçbir damga
   *    yazılmaz ve tam senkron (yeniden JoinBoard) istenir,
   * 4. damgalama (bkz. `stampAfterApply`).
   */
  private applyRemote(raw: readonly unknown[], source: RemoteSource): boolean {
    const canvas = this.canvas;
    if (!canvas || raw.length === 0) {
      return true;
    }
    const ordered = sortByFractionalIndex(raw);
    let restored: WhiteboardElement[];
    let merged: WhiteboardElement[];
    // Uygulama öncesi yerel damgalar (reconcile'ın index onarımı yerel nesneleri yerinde değiştirebilir).
    const before = new Map(canvas.getElements().map((element) => [element.id, stampOf(element)]));
    try {
      restored = canvas
        .restore(ordered, { repairBindings: source === 'join' })
        .filter(isSyncableElement)
        .map(sanitizeIncomingElement);
      if (restored.length === 0) {
        return true;
      }
      merged = canvas.reconcile(canvas.getElements(), restored);
      canvas.applyElements(merged);
    } catch {
      // Eleman içeriği loglanmaz.
      console.warn(`[whiteboard] remote ${source} could not be applied; requesting full resync`);
      this.requestFullResync(source);
      return false;
    }

    this.stampAfterApply(ordered, restored, merged, before, source);
    return true;
  }

  /**
   * - Sahneye uzaktaki damgasıyla AYNEN giren eleman `known`'a yazılır → geri gönderilmez (echo yok).
   * - Uygulama sırasında onarılan (ne eski yerel ne uzak damgayla eşleşen) eleman damgalanmaz → geri gönderilir,
   *   taraflar yakınsar; aynı id art arda `WHITEBOARD_MAX_REPAIR_ROUNDS`'u aşarsa damgalanır (devre kesici).
   * - Düzeltme (correction) yerel düzenleme yüzünden reddedildiyse iyimser damga silinir: yerel hâl yeniden gönderilir
   *   ve sunucu yeniden karar verir (aynı devre kesici).
   */
  private stampAfterApply(
    ordered: readonly unknown[],
    restored: readonly WhiteboardElement[],
    merged: readonly WhiteboardElement[],
    before: ReadonlyMap<string, ElementStamp>,
    source: RemoteSource
  ): void {
    const remoteStamps = rawStampsOf(ordered);
    const restoredIds = new Set(restored.map((element) => element.id));

    for (const element of merged) {
      const scene = stampOf(element);
      const remote = restoredIds.has(element.id) ? remoteStamps.get(element.id) : undefined;
      const local = before.get(element.id);
      if (remote && sameStamp(remote, scene)) {
        this.known.set(element.id, remote);
        this.repairStreak.delete(element.id);
        continue;
      }
      const repaired =
        (remote !== undefined || local !== undefined) && !sameStamp(local, scene) && !sameStamp(remote, scene);
      const rejectedCorrection = source === 'correction' && remote !== undefined && sameStamp(local, scene);
      if (!repaired && !rejectedCorrection) {
        continue;
      }
      const rounds = (this.repairStreak.get(element.id) ?? 0) + 1;
      this.repairStreak.set(element.id, rounds);
      if (rounds > WHITEBOARD_MAX_REPAIR_ROUNDS) {
        // Onarım ping-pong'u: onarılmış hâl damgalanır, bir daha gönderilmez. Reddedilen düzeltmede damgaya dokunulmaz
        // (yerel hâl henüz gönderilmemiş olabilir); yalnızca silme döngüsü durur.
        console.warn(`[whiteboard] element ${element.id} repaired ${rounds} times in a row; stopped re-sending`);
        if (repaired) {
          this.known.set(element.id, scene);
        }
      } else if (rejectedCorrection) {
        this.known.delete(element.id);
      }
    }
  }

  /** Uzak güncelleme uygulanamadı: tek seferlik yeniden JoinBoard (tam sahne); join'in kendisi başarısızsa döngü yok. */
  private requestFullResync(source: RemoteSource): void {
    if (source === 'join' || this.resyncRequested || !this.joined) {
      return;
    }
    this.resyncRequested = true;
    void this.join();
  }

  private onPeerPointer(role: unknown, pointer: unknown): void {
    const parsed = parsePeerPointer(role, pointer);
    if (!parsed || parsed.role === this.roleState() || this.statusState() === 'closed') {
      return;
    }
    this.peerPointerState.set(parsed);
  }

  private onPresence(role: unknown, online: unknown): void {
    if (!isWhiteboardRole(role) || role === this.roleState() || typeof online !== 'boolean') {
      return;
    }
    this.peerOnlineState.set(online);
    if (!online) {
      this.peerPointerState.set(null);
    }
  }

  // ---------------------------------------------------------------- yerel → uzak

  private async flush(): Promise<void> {
    const canvas = this.canvas;
    if (!canvas || !this.joined || this.destroyed || this.statusState() === 'closed') {
      return;
    }
    if (this.sending) {
      this.flushRequested = true;
      return;
    }

    this.warnedThisFlush.clear();
    const all = canvas.getElements();
    const syncable = all.filter(isSyncableElement);
    if (syncable.length !== all.length) {
      // Görsel/gömülü içerik (sürükle-bırak, yapıştırma) sunucuda reddedilir: yerel sahneden de kaldırılır.
      canvas.applyElements(syncable);
      if (all.some((element) => !element.isDeleted && !isSyncableElement(element))) {
        this.warningsSubject.next('unsupportedContent');
      }
    }

    const outgoing: OutgoingItem<OutgoingElement>[] = [];
    for (const element of changedElements(syncable, this.known)) {
      const payload = toOutgoingElement(element);
      const bytes = jsonByteLength(payload);
      if (bytes > WHITEBOARD_MAX_ELEMENT_BYTES) {
        // Çok uzun serbest çizim: gönderilmez (sunucu mesaj sınırı), kullanıcı bir kez uyarılır.
        if (!this.oversizeWarned.has(element.id)) {
          this.oversizeWarned.add(element.id);
          this.warningsSubject.next('elementTooLarge');
        }
        continue;
      }
      outgoing.push({ item: { element, payload }, bytes });
    }
    if (outgoing.length === 0) {
      return;
    }

    this.sending = true;
    try {
      for (const chunk of chunkOutgoing(outgoing)) {
        if ((await this.sendChunk(chunk)) === 'abort') {
          break;
        }
      }
    } finally {
      this.sending = false;
      if (this.flushRequested) {
        this.flushRequested = false;
        this.notifyLocalChange();
      }
    }
  }

  private async sendChunk(chunk: readonly OutgoingElement[]): Promise<SendOutcome> {
    const connection = this.connection;
    if (!connection || !this.joined || chunk.length === 0) {
      return chunk.length === 0 ? 'ok' : 'abort';
    }
    // İyimser damgalama: gönderim sürerken gelen onChange aynı elemanları yeniden göndermesin.
    const previous: StampHistory = chunk.map(({ element }) => [element.id, this.known.get(element.id)] as const);
    for (const { element } of chunk) {
      this.known.set(element.id, stampOf(element));
    }

    try {
      const result = await connection.invoke<WhiteboardSendResult>(
        'SendElements',
        this.bookingId,
        chunk.map(({ payload }) => payload)
      );
      const corrections = Array.isArray(result?.corrections) ? result.corrections : [];
      if (corrections.length > 0 && this.statusState() !== 'closed') {
        this.applyRemote(corrections, 'correction');
      }
      return 'ok';
    } catch (error) {
      const code = parseHubErrorCode(error);
      if (code !== null && CONTENT_REJECTION_CODES.has(code)) {
        return this.onContentRejected(code, chunk, previous);
      }
      return this.onSendFailed(code, chunk, previous);
    }
  }

  /**
   * İçerik kaynaklı red: sunucu parçayı bütünüyle reddeder. Parça ikiye bölünüp yeniden denenir (önce damgalar eski
   * hâline döner); tek elemana inilince o eleman damgalı bırakılır (aynı içerikle döngü olmasın) ve loglanır.
   */
  private async onContentRejected(
    code: WhiteboardErrorCode,
    chunk: readonly OutgoingElement[],
    previous: StampHistory
  ): Promise<SendOutcome> {
    if (chunk.length > 1) {
      this.revertStamps(chunk, previous);
      const middle = Math.ceil(chunk.length / 2);
      for (const half of [chunk.slice(0, middle), chunk.slice(middle)]) {
        if ((await this.sendChunk(half)) === 'abort') {
          return 'abort';
        }
      }
      return 'ok';
    }
    const { element } = chunk[0];
    // İçerik loglanmaz: yalnızca id, tip ve kod.
    console.warn(`[whiteboard] element ${element.id} (${element.type}) rejected by server: ${code}`);
    this.warnOncePerFlush(code === 'SceneElementLimit' || code === 'SceneSizeLimit' ? 'sceneLimit' : 'rejected');
    return 'ok';
  }

  private onSendFailed(code: WhiteboardErrorCode | null, chunk: readonly OutgoingElement[], previous: StampHistory): SendOutcome {
    // Geçici hata: damgalar geri alınır, elemanlar yeniden gönderilecek.
    this.revertStamps(chunk, previous);

    switch (code) {
      case 'RateLimited':
        this.clearTimer('sendRetryTimer');
        this.sendRetryTimer = setTimeout(() => {
          this.sendRetryTimer = null;
          void this.flush();
        }, WHITEBOARD_RATE_LIMIT_RETRY_MS);
        return 'abort';
      case 'NotJoined':
        void this.join();
        return 'abort';
      case 'WindowClosed':
        this.closeBoard('WindowClosed');
        return 'abort';
      // Issue #298: gönderimdeki yeniden doğrulama öğretmeni müsait bulmadı — kapanır, gönderim döngüsü durur.
      case 'TeacherUnavailable':
        this.closeBoard('TeacherUnavailable');
        return 'abort';
      case 'BoardClosed':
        this.closeBoard(null);
        return 'abort';
      default:
        // Taşıma/yetki hatası: yeniden bağlanma + JoinBoard sahneyi eşitler ve kalanları yeniden gönderir.
        return 'abort';
    }
  }

  /** İyimser damgaları geri alır — yalnızca hâlâ bu gönderimin yazdığı damga duruyorsa (sonradan gelen korunur). */
  private revertStamps(chunk: readonly OutgoingElement[], previous: StampHistory): void {
    chunk.forEach(({ element }, index) => {
      if (sameStamp(this.known.get(element.id), stampOf(element))) {
        const prior = previous[index][1];
        if (prior) {
          this.known.set(element.id, prior);
        } else {
          this.known.delete(element.id);
        }
      }
    });
  }

  private warnOncePerFlush(warning: WhiteboardWarning): void {
    if (!this.warnedThisFlush.has(warning)) {
      this.warnedThisFlush.add(warning);
      this.warningsSubject.next(warning);
    }
  }

  private sendPointer(): void {
    const pointer = this.pendingPointer;
    this.pendingPointer = null;
    const connection = this.connection;
    if (!pointer || !connection || !this.joined || this.statusState() === 'closed') {
      return;
    }
    if (!isCoordinate(pointer.x) || !isCoordinate(pointer.y)) {
      return;
    }
    connection
      .send('SendPointer', this.bookingId, { x: pointer.x, y: pointer.y, tool: pointer.tool })
      .catch(() => undefined);
  }

  // ---------------------------------------------------------------- zamanlayıcılar

  private clearTimer(name: 'flushTimer' | 'sendRetryTimer' | 'retryTimer' | 'pointerTimer'): void {
    const timer = this[name];
    if (timer !== null) {
      clearTimeout(timer);
      this[name] = null;
    }
  }

  private clearAllTimers(): void {
    this.clearTimer('flushTimer');
    this.clearTimer('sendRetryTimer');
    this.clearTimer('retryTimer');
    this.clearTimer('pointerTimer');
    this.pendingPointer = null;
  }
}

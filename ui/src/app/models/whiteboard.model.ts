/**
 * Ortak çizim tahtası (issue #98) — `/hub/whiteboard` SignalR sözleşmesi.
 *
 * Kaynaklar (tahmin değil, birebir):
 * - `api/ExamApp.Api/Hubs/WhiteboardHub.cs` (metotlar ve istemci olayları)
 * - `api/ExamApp.Api/Models/Dtos/Whiteboard/WhiteboardDtos.cs` (DTO'lar; SignalR JSON protokolü camelCase)
 * - `api/ExamApp.Api/Services/Whiteboard/WhiteboardErrors.cs` (hata kodları ve kapanış nedenleri)
 */

/** Hub yolu — gateway de aynı yolu `exam-dotnet-api`'ye geçirir (yalnızca WebSocket). */
export const WHITEBOARD_HUB_PATH = '/hub/whiteboard';

/** Çağıranın tahtadaki rolü (`WhiteboardJoinResultDto.Role`, `PointerUpdated`/`PeerPresenceChanged` rolü). */
export type WhiteboardRole = 'teacher' | 'student';

/**
 * `JoinBoard(bookingId)` yanıtı. `elements` sunucu için opak JSON'dur; istemci her birini `restoreElements`'ten geçirir.
 * `serverVersion` bir SIRA NUMARASI DEĞİLDİR (yalnızca teşhis ipucu) — uzlaşma eleman bazında version/versionNonce ile.
 * `windowClosesAtUtc`: .NET `DateTime` (UTC) ISO metni.
 */
export interface WhiteboardJoinResult {
  boardId: string;
  elements: unknown[];
  serverVersion: number;
  role: string;
  windowClosesAtUtc: string;
  peerOnline: boolean;
}

/** `SendElements` yanıtı. `corrections`: gönderenin kaybettiği id'ler için sunucudaki kazanan elemanlar. */
export interface WhiteboardSendResult {
  serverVersion: number;
  accepted: number;
  corrections: unknown[];
}

/** `SendPointer` girdisi / `PointerUpdated` çıktısı. `tool`: en fazla 16 harf/rakam/tire (Excalidraw: pointer | laser). */
export interface WhiteboardPointer {
  x: number;
  y: number;
  tool?: string | null;
}

/** Excalidraw'ın collaborator imleci için kabul ettiği araçlar. */
export type WhiteboardPointerTool = 'pointer' | 'laser';

/** Karşı tarafın (doğrulanmış) imleci. */
export interface WhiteboardPeerPointer {
  x: number;
  y: number;
  tool: WhiteboardPointerTool;
  role: WhiteboardRole;
}

/** `HubException.Message` değerleri — `WhiteboardErrorCodes`. */
export const WHITEBOARD_ERROR_CODES = [
  'UserNotResolved',
  'BookingNotFound',
  'NotParticipant',
  /** Eski (#98/#289): #298'den beri sunucu yerine `TeacherUnavailable` döner; eski yanıt uyumu için duruyor. */
  'TeacherNotApproved',
  /** Issue #298: randevunun öğretmeni askıda/onaysız — iki tarafa da aynı nötr kod. */
  'TeacherUnavailable',
  'BookingNotApproved',
  'WindowNotOpen',
  'WindowClosed',
  'NotJoined',
  'BoardClosed',
  'TooManyElementsInMessage',
  'SceneElementLimit',
  'SceneSizeLimit',
  'InvalidElement',
  'ElementTypeNotAllowed',
  'ImagesNotSupported',
  'InvalidPointer',
  'RateLimited',
] as const;

export type WhiteboardErrorCode = (typeof WHITEBOARD_ERROR_CODES)[number];

/**
 * `BoardClosed(reason)` değerleri — `WhiteboardCloseReasons`. `TeacherNotApproved` #298'den beri sunucudan gelmez
 * (yerine `TeacherUnavailable`); eski istemci/sunucu uyumu için listede kalır.
 */
export const WHITEBOARD_CLOSE_REASONS = [
  'WindowClosed',
  'BookingCancelled',
  'TeacherNotApproved',
  'TeacherUnavailable',
  'AccessRevoked',
] as const;

export type WhiteboardCloseReason = (typeof WHITEBOARD_CLOSE_REASONS)[number];

/**
 * Sunucunun kabul ettiği eleman tipleri (`WhiteboardStore.AllowedTypes`). image/embeddable/iframe/magicframe ve
 * bilinmeyen tipler reddedilir — istemci bunları hiç göndermez, yerel sahneden de kaldırır.
 */
export const WHITEBOARD_ALLOWED_ELEMENT_TYPES: ReadonlySet<string> = new Set([
  'rectangle',
  'ellipse',
  'diamond',
  'line',
  'arrow',
  'freedraw',
  'text',
  'frame',
]);

/**
 * Senkronizasyon katmanının bir Excalidraw elemanından okuduğu alanlar. Excalidraw'ın zengin tipleri yalnızca
 * tembel yüklenen tuval modülünde kullanılır; geri kalan kod bu dar görünümle çalışır.
 */
export interface WhiteboardElement {
  readonly id: string;
  readonly type: string;
  readonly version: number;
  readonly versionNonce: number;
  readonly isDeleted: boolean;
  readonly link?: string | null;
  readonly fileId?: string | null;
}

/** Bağlantı durumu (durum çubuğu). */
export type WhiteboardConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected' | 'closed';

/** Kullanıcıya snackbar ile gösterilen, akışı durdurmayan uyarılar. */
export type WhiteboardWarning =
  | 'elementTooLarge'
  | 'unsupportedContent'
  | 'sceneLimit'
  | 'rejected'
  | 'linkBlocked'
  /** Uzak sahne istemci sınırını aştı; uygulanmadı (issue #332). */
  | 'remoteSceneLimit';

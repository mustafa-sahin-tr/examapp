import { InjectionToken } from '@angular/core';

import { WhiteboardElement, WhiteboardPeerPointer, WhiteboardPointerTool } from '../../../models/whiteboard.model';

/**
 * Çizim tuvali arayüzü (issue #98). Gerçek uygulaması tembel yüklenen `excalidraw-canvas.ts` (React + Excalidraw);
 * senkronizasyon servisi ve Angular komponenti yalnızca bu arayüzü bilir, testler sahtesini kullanır.
 */
export interface WhiteboardCanvas {
  /** Sahnedeki tüm elemanlar — silinmişler (tombstone) dahil; silme de bir değişikliktir. */
  getElements(): readonly WhiteboardElement[];
  /**
   * Güvenilmez uzak JSON'u Excalidraw `restoreElements` ile geçerli elemanlara çevirir. `repairBindings` yalnızca TAM
   * sahnede (JoinBoard) açılır: parça halinde gelen bir bağlı metin/ok, kabı parçada olmadığı için bağını kaybederdi.
   */
  restore(raw: readonly unknown[], options?: WhiteboardRestoreOptions): WhiteboardElement[];
  /** Excalidraw `reconcileElements`: yerel + uzak → birleşik sahne (version/versionNonce kuralı). */
  reconcile(local: readonly WhiteboardElement[], remote: readonly WhiteboardElement[]): WhiteboardElement[];
  /** Sahneyi değiştirir; geri alma geçmişine yazılmaz (uzak güncelleme ya da yerel temizlik). */
  applyElements(elements: readonly WhiteboardElement[]): void;
  /** Karşı tarafın imleci (`null` = gizle). `label` imlecin yanında gösterilen ad. */
  setPeerPointer(pointer: WhiteboardPeerPointer | null, label: string): void;
  setViewMode(readOnly: boolean): void;
  setTheme(theme: WhiteboardTheme): void;
  setLangCode(langCode: WhiteboardLangCode): void;
  /** Görünür olunca (ör. gizli sekmeden) ölçüleri tazeler. */
  refresh(): void;
  /** React kökünü kaldırır. `<html lang>` geri yüklemesi çağıranın işidir. */
  destroy(): void;
}

export type WhiteboardTheme = 'light' | 'dark';

export interface WhiteboardRestoreOptions {
  /** Tam sahne (JoinBoard) — bağ/çerçeve onarımı yapılır. Parçalarda `false`. */
  repairBindings: boolean;
}

/** Excalidraw `langCode` değerleri (uygulama dilleri: tr, en). */
export type WhiteboardLangCode = 'tr-TR' | 'en';

export interface WhiteboardCanvasOptions {
  theme: WhiteboardTheme;
  langCode: WhiteboardLangCode;
  readOnly: boolean;
}

export interface WhiteboardCanvasCallbacks {
  /** Sahne ya da görünüm değişti (sık tetiklenir; tüketici kısar). */
  onChange(): void;
  /** Yerel imleç sahne koordinatları. */
  onPointer(pointer: { x: number; y: number; tool: WhiteboardPointerTool }): void;
  /** http(s) olmayan bir bağlantı açılmak istendi ve engellendi. */
  onLinkBlocked(): void;
}

/**
 * Tuvali bir host element'e bağlar; Excalidraw ilk sahnesini kurunca çözülür. `signal` iptal edilirse (zaman aşımı,
 * yıkım) React kökü kaldırılır ve promise reddedilir.
 */
export type WhiteboardCanvasMount = (
  host: HTMLElement,
  options: WhiteboardCanvasOptions,
  callbacks: WhiteboardCanvasCallbacks,
  signal?: AbortSignal
) => Promise<WhiteboardCanvas>;

/**
 * Tuval modülünü tembel yükleyen fonksiyon. Varsayılan sağlayıcı `provideExcalidrawCanvas()`
 * (`excalidraw-canvas.provider.ts`) — Excalidraw/React yalnızca bu fonksiyon çağrılınca ayrı bir chunk olarak iner.
 * Token'ın varsayılanı yoktur: böylece bu dosyayı import eden testler Excalidraw'ı derlemez.
 */
export const WHITEBOARD_CANVAS_LOADER = new InjectionToken<() => Promise<WhiteboardCanvasMount>>(
  'WHITEBOARD_CANVAS_LOADER'
);

export function langCodeFor(locale: string): WhiteboardLangCode {
  return locale === 'tr' ? 'tr-TR' : 'en';
}

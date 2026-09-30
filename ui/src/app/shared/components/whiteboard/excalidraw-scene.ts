import { reconcileElements, restoreElements } from '@excalidraw/excalidraw';
import type { RemoteExcalidrawElement } from '@excalidraw/excalidraw/data/reconcile';
import type { OrderedExcalidrawElement } from '@excalidraw/excalidraw/element/types';
import type { AppState } from '@excalidraw/excalidraw/types';

import { WhiteboardElement } from '../../../models/whiteboard.model';
import { WhiteboardRestoreOptions } from './whiteboard-canvas';

/**
 * Tuvalin sahne uzlaşması — Excalidraw'ın saf fonksiyonları, React'siz (issue #98). `excalidraw-canvas.ts` bunları
 * kullanır; sözleşme testi (`excalidraw-contract.spec.ts`) aynı fonksiyonları gerçek Excalidraw ile sınar.
 * Bu dosya da yalnızca tembel chunk'tan (ya da testten) yüklenir.
 */

/**
 * Parça (ElementsUpdated/corrections): Excalidraw collab ile aynı çağrı — `restoreElements(remote, null)`. Bağ onarımı
 * yalnızca TAM sahnede: `repairBindings` parçada olmayan kap/ok hedefini "yok" sayar ve `containerId`,
 * `boundElements`, `startBinding`/`endBinding`, `frameId` alanlarını siler.
 */
export function restoreRemoteElements(raw: readonly unknown[], options?: WhiteboardRestoreOptions): WhiteboardElement[] {
  const input = raw as unknown as OrderedExcalidrawElement[];
  const restored = options?.repairBindings
    ? restoreElements(input, null, { repairBindings: true })
    : restoreElements(input, null);
  return restored as unknown as WhiteboardElement[];
}

/** Excalidraw `reconcileElements` (yüksek version; eşitse küçük versionNonce; düzenlenen yerel eleman korunur). */
export function reconcileRemoteElements(
  local: readonly WhiteboardElement[],
  remote: readonly WhiteboardElement[],
  appState: AppState
): WhiteboardElement[] {
  return reconcileElements(
    local as unknown as readonly OrderedExcalidrawElement[],
    remote as unknown as readonly RemoteExcalidrawElement[],
    appState
  ) as unknown as WhiteboardElement[];
}

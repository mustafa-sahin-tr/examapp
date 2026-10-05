import { createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { CaptureUpdateAction, Excalidraw } from '@excalidraw/excalidraw';
import type { OrderedExcalidrawElement } from '@excalidraw/excalidraw/element/types';
import type {
  Collaborator,
  ExcalidrawImperativeAPI,
  ExcalidrawProps,
  SocketId,
} from '@excalidraw/excalidraw/types';

import { WhiteboardElement, WhiteboardPeerPointer } from '../../../models/whiteboard.model';
import { guardHyperlinkAnchors, isBlockedSidebar } from './excalidraw-policy';
import { reconcileRemoteElements, restoreRemoteElements } from './excalidraw-scene';
import { WhiteboardCanvas, WhiteboardCanvasMount } from './whiteboard-canvas';

/**
 * Excalidraw'ı (React) bir Angular host element'ine bağlayan gerçek tuval (issue #98). Bu dosya YALNIZCA
 * `loadExcalidrawCanvas()` içindeki dinamik `import()` ile yüklenir; React/Excalidraw ayrı bir chunk'ta kalır.
 * JSX yok: `React.createElement` kullanılır (tsconfig'e JSX ayarı gerekmez).
 */

/** Karşı tarafın imleci için sabit collaborator anahtarı (1:1 ders). Renk verilmez: Excalidraw id'den türetir. */
const PEER_SOCKET_ID = 'peer' as SocketId;

export const mountExcalidrawCanvas: WhiteboardCanvasMount = (host, options, callbacks, signal) =>
  new Promise<WhiteboardCanvas>((resolve, reject) => {
    if (signal?.aborted) {
      reject(new Error('Whiteboard mount aborted'));
      return;
    }
    const root = createRoot(host);
    // Bağlantı balonundaki anchor'ın orta tık / bağlam menüsü / sürükle yolları da onay akışına bağlanır (issue #332).
    const removeAnchorGuard = guardHyperlinkAnchors(host, (link) => callbacks.onLinkOpen(link));
    let api: ExcalidrawImperativeAPI | null = null;
    let ready = false;
    let destroyed = false;

    let props: ExcalidrawProps = {
      excalidrawAPI: (instance) => {
        if (!api && !destroyed) {
          api = instance;
        }
      },
      // Excalidraw ilk sahneyi (initialData) API verildikten SONRA async kurar ve o an sahneyi değiştirir; ondan önce
      // uygulanan uzak sahne ezilirdi. `onChange` yalnızca `isLoading` bittikten sonra tetiklenir: ilk çağrı = hazır.
      onChange: (_elements, appState) => {
        if (!api || destroyed) {
          return;
        }
        // Kütüphane paneli (Browse/Publish dış sitelere gider) hangi yoldan açılırsa açılsın kapatılır.
        if (isBlockedSidebar(appState.openSidebar)) {
          api.updateScene({ appState: { openSidebar: null }, captureUpdate: CaptureUpdateAction.NEVER });
        }
        if (!ready) {
          if (!appState.isLoading) {
            ready = true;
            resolve(canvas);
          }
          return;
        }
        callbacks.onChange();
      },
      onPointerUpdate: ({ pointer }) => callbacks.onPointer({ x: pointer.x, y: pointer.y, tool: pointer.tool }),
      // Excalidraw'ın kendi açma davranışı (tuvaldeki bağlantı ikonu ve bağlantı balonu) her zaman engellenir; karar
      // Angular tarafında verilir: dış origin onay dialogu, aynı origin doğrudan, http(s) dışı engel (issue #332).
      onLinkOpen: (element, event) => {
        event.preventDefault();
        callbacks.onLinkOpen(element.link);
      },
      // Gömülü içerik (iframe/embeddable) hiçbir adres için doğrulanmaz → render edilmez; sunucu da reddeder.
      validateEmbeddable: false,
      // AI/magic frame araçları kapalı.
      aiEnabled: false,
      isCollaborating: true,
      theme: options.theme,
      langCode: options.langCode,
      viewModeEnabled: options.readOnly,
      autoFocus: false,
      handleKeyboardGlobally: false,
      detectScroll: true,
      // Kalıcı saklama/dışa aktarım kapsam dışı; görsel aracı kapalı; tema uygulamadan gelir.
      UIOptions: {
        tools: { image: false },
        canvasActions: {
          loadScene: false,
          saveToActiveFile: false,
          export: false,
          saveAsImage: false,
          toggleTheme: null,
          changeViewBackgroundColor: false,
        },
      },
      // Sağ üste özel içerik eklenmez. Kütüphane tetikleyicisi bu prop'tan bağımsız render edilir: CSS ile gizlenir ve
      // panel onChange'te kapatılır (bkz. excalidraw-policy.ts).
      renderTopRightUI: () => null,
    };

    const render = (): void => {
      if (!destroyed) {
        root.render(createElement(Excalidraw, props));
      }
    };

    const update = (patch: Partial<ExcalidrawProps>): void => {
      props = { ...props, ...patch };
      render();
    };

    const canvas: WhiteboardCanvas = {
      getElements: () => (api ? (api.getSceneElementsIncludingDeleted() as readonly WhiteboardElement[]) : []),

      restore: (raw, restoreOptions) => restoreRemoteElements(raw, restoreOptions),

      reconcile: (local, remote) => (api ? reconcileRemoteElements(local, remote, api.getAppState()) : [...local]),

      applyElements: (elements) => {
        api?.updateScene({
          elements: elements as unknown as readonly OrderedExcalidrawElement[],
          captureUpdate: CaptureUpdateAction.NEVER,
        });
      },

      setPeerPointer: (pointer: WhiteboardPeerPointer | null, label: string) => {
        const collaborators = new Map<SocketId, Collaborator>();
        if (pointer) {
          collaborators.set(PEER_SOCKET_ID, {
            id: PEER_SOCKET_ID,
            socketId: PEER_SOCKET_ID,
            username: label,
            pointer: { x: pointer.x, y: pointer.y, tool: pointer.tool },
          });
        }
        api?.updateScene({ collaborators, captureUpdate: CaptureUpdateAction.NEVER });
      },

      setViewMode: (readOnly) => update({ viewModeEnabled: readOnly }),

      setTheme: (theme) => update({ theme }),

      setLangCode: (langCode) => update({ langCode }),

      refresh: () => api?.refresh(),

      destroy: () => {
        if (destroyed) {
          return;
        }
        destroyed = true;
        api = null;
        removeAnchorGuard();
        root.unmount();
      },
    };

    // Hazır olmadan iptal (zaman aşımı/yıkım): yarım kök kaldırılır, host yeniden denemeye temiz kalır.
    signal?.addEventListener(
      'abort',
      () => {
        if (!ready) {
          canvas.destroy();
          reject(new Error('Whiteboard mount aborted'));
        }
      },
      { once: true }
    );

    render();
  });

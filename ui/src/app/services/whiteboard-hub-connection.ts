import { InjectionToken } from '@angular/core';
import * as signalR from '@microsoft/signalr';

import { WHITEBOARD_HUB_PATH } from '../models/whiteboard.model';

/**
 * `WhiteboardSyncService`'in kullandığı SignalR bağlantı yüzeyi (issue #98). `signalR.HubConnection` bunu yapısal
 * olarak karşılar; testler sahte bir uygulama verir.
 */
export interface WhiteboardHubConnection {
  start(): Promise<void>;
  stop(): Promise<void>;
  invoke<T = unknown>(methodName: string, ...args: unknown[]): Promise<T>;
  send(methodName: string, ...args: unknown[]): Promise<void>;
  on(methodName: string, handler: (...args: unknown[]) => void): void;
  onreconnecting(callback: (error?: Error) => void): void;
  onreconnected(callback: (connectionId?: string) => void): void;
  onclose(callback: (error?: Error) => void): void;
}

/** Otomatik yeniden bağlanma gecikmeleri; tükenince servis kendi (sınırsız) yeniden deneme döngüsüne geçer. */
export const WHITEBOARD_AUTO_RECONNECT_DELAYS_MS = [0, 2000, 5000, 10000];

export type WhiteboardHubConnectionFactory = (accessTokenFactory: () => Promise<string>) => WhiteboardHubConnection;

/**
 * Varsayılan fabrika: gateway üzerinden (göreli yol; BadgeService hub'ı ile aynı desen) yalnızca WebSocket,
 * negotiate atlanır (gateway `/negotiate`'i eşlemez). Token her (yeniden) bağlanmada fabrikadan taze alınır.
 */
export const WHITEBOARD_HUB_CONNECTION_FACTORY = new InjectionToken<WhiteboardHubConnectionFactory>(
  'WHITEBOARD_HUB_CONNECTION_FACTORY',
  {
    providedIn: 'root',
    factory: () => (accessTokenFactory) =>
      new signalR.HubConnectionBuilder()
        .withUrl(WHITEBOARD_HUB_PATH, {
          accessTokenFactory,
          transport: signalR.HttpTransportType.WebSockets,
          skipNegotiation: true,
        })
        .withAutomaticReconnect(WHITEBOARD_AUTO_RECONNECT_DELAYS_MS)
        .configureLogging(signalR.LogLevel.Warning)
        .build(),
  }
);

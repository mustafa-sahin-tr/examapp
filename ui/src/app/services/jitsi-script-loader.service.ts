import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject } from '@angular/core';

/** `new JitsiMeetExternalAPI(domain, options)` seçenekleri — sadece kullandığımız alanlar. */
export interface JitsiMeetExternalApiOptions {
  roomName: string;
  jwt?: string;
  parentNode?: HTMLElement;
  width?: string | number;
  height?: string | number;
  userInfo?: { displayName?: string; email?: string };
  configOverwrite?: Record<string, unknown>;
  interfaceConfigOverwrite?: Record<string, unknown>;
}

/** IFrame API örneğinin kullandığımız yüzeyi. */
export interface JitsiMeetExternalApi {
  dispose(): void;
  addListener(event: string, listener: (payload: unknown) => void): void;
  removeListener(event: string, listener: (payload: unknown) => void): void;
  executeCommand(command: string, ...args: unknown[]): void;
}

export type JitsiMeetExternalApiConstructor = new (
  domain: string,
  options: JitsiMeetExternalApiOptions
) => JitsiMeetExternalApi;

declare global {
  interface Window {
    JitsiMeetExternalAPI?: JitsiMeetExternalApiConstructor;
  }
}

/**
 * Jitsi `external_api.js` betiğini çalışma anında yükler (issue #97).
 *
 * Adres derleme zamanında bilinmez — backend `VideoSession.baseUrl` ile gelir, bu yüzden
 * `index.html`'e sabit `<script>` koyulmaz. Aynı adres için yükleme bir kez yapılır;
 * eşzamanlı çağrılar aynı Promise'i paylaşır.
 */
@Injectable({ providedIn: 'root' })
export class JitsiScriptLoaderService {
  private readonly document = inject(DOCUMENT);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  /**
   * `window.JitsiMeetExternalAPI` tek bir global olduğu için cache anahtarı yüklenen origin'dir:
   * farklı bir origin istenirse sessizce yanlış sunucunun kurucusu dönmesin diye hata veririz.
   */
  private loadedOrigin: string | null = null;
  private pending: Promise<JitsiMeetExternalApiConstructor> | null = null;

  /** `baseUrl` ör. "https://meet.example.com" — betik `<baseUrl>/external_api.js` adresinden yüklenir. */
  load(baseUrl: string): Promise<JitsiMeetExternalApiConstructor> {
    if (!this.isBrowser) {
      return Promise.reject(new Error('Jitsi yalnızca tarayıcıda yüklenebilir.'));
    }

    let origin: string;
    try {
      origin = new URL(baseUrl).origin;
    } catch {
      return Promise.reject(new Error('Geçersiz Jitsi adresi.'));
    }

    if (this.loadedOrigin && this.loadedOrigin !== origin) {
      return Promise.reject(
        new Error('Farklı bir Jitsi sunucusu zaten yüklü; sayfanın yenilenmesi gerekiyor.')
      );
    }

    if (this.pending) {
      return this.pending;
    }

    const src = `${origin}/external_api.js`;

    const pending = new Promise<JitsiMeetExternalApiConstructor>((resolve, reject) => {
      const script = this.document.createElement('script');
      script.src = src;
      script.async = true;
      script.onload = () => {
        const ctor = window.JitsiMeetExternalAPI;
        if (ctor) {
          this.loadedOrigin = origin;
          resolve(ctor);
        } else {
          this.pending = null;
          reject(new Error('Jitsi betiği yüklendi ancak JitsiMeetExternalAPI bulunamadı.'));
        }
      };
      script.onerror = () => {
        // Başarısız yükleme cache'te kalmasın; kullanıcı "tekrar dene" diyebilmeli.
        this.pending = null;
        script.remove();
        reject(new Error('Jitsi betiği yüklenemedi.'));
      };
      this.document.body.appendChild(script);
    });

    this.pending = pending;
    return pending;
  }
}

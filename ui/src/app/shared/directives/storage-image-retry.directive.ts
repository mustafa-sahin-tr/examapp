import { DestroyRef, Directive, ElementRef, Renderer2, inject, input } from '@angular/core';
import { Subscription, take } from 'rxjs';
import { isCrossOriginUrl, isStorageImageUrl } from '../utils/request-url.util';
import { StorageImageResolver } from '../utils/storage-image-refresh.util';
import { storageImageKey } from '../utils/storage-image-url.util';

/**
 * MinIO görseli yüklenemezse (imza süresi doldu ya da 403) sahip DTO'yu yeniden çekip taze imzalı URL ile BİR KEZ
 * yeniden dener (issue #365 S3).
 *
 * ```html
 * <img [src]="question.imageUrl" [appStorageImageRetry]="imageRefresh" />
 * ```
 *
 * Döngü koruması: aynı görsel (imzasız yol) için tek deneme; deneme de hata verirse görsel kırık kalır. Deneme
 * başarıyla yüklenirse (`load`) sayaç sıfırlanır — saatler sonra imza yeniden dolarsa yine bir kez denenir; arada
 * başarılı yükleme olmadan ikinci deneme yapılmaz. Yalnız `/img/...` adresleri ele alınır (data:/blob: önizlemeler
 * ve dış adresler dokunulmaz). Resolver aynı URL'yi ya da hiçbir şey dönmezse src değişmez.
 */
@Directive({
  selector: 'img[appStorageImageRetry]',
  standalone: true,
  host: {
    '(error)': 'onError()',
    '(load)': 'onLoad()',
  },
})
export class StorageImageRetryDirective {
  /** Taze URL'yi bulan fonksiyon; null/undefined iken direktif hiçbir şey yapmaz. */
  readonly appStorageImageRetry = input<StorageImageResolver | null | undefined>(null);

  private readonly host = inject<ElementRef<HTMLImageElement>>(ElementRef);
  private readonly renderer = inject(Renderer2);
  private retriedKey: string | null = null;
  private pending: Subscription | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.pending?.unsubscribe());
  }

  onLoad(): void {
    this.retriedKey = null;
  }

  onError(): void {
    const resolver = this.appStorageImageRetry();
    const img = this.host.nativeElement;
    const failedUrl = img.getAttribute('src') || img.src;
    if (!resolver || !failedUrl || !isStorageImageUrl(failedUrl) || isCrossOriginUrl(failedUrl)) {
      return;
    }

    const key = storageImageKey(failedUrl);
    if (this.retriedKey === key) {
      return; // bu görsel zaten bir kez yeniden denendi
    }
    this.retriedKey = key;

    this.pending?.unsubscribe();
    this.pending = resolver(failedUrl)
      .pipe(take(1))
      .subscribe({
        next: (fresh) => {
          // Bu arada bağlama başka bir görsele geçtiyse eski cevabı uygulama.
          const current = img.getAttribute('src') || img.src;
          if (!fresh || fresh === failedUrl || current !== failedUrl) {
            return;
          }
          this.renderer.setProperty(img, 'src', fresh);
        },
        error: () => undefined,
      });
  }
}

import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  NgZone,
  PLATFORM_ID,
  ViewEncapsulation,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';

import { WhiteboardConnectionStatus, WhiteboardRole, WhiteboardWarning } from '../../../models/whiteboard.model';
import { ColorSchemeService } from '../../../services/color-scheme.service';
import { LocaleService } from '../../../services/locale.service';
import { WhiteboardSyncService } from '../../../services/whiteboard-sync.service';
import { OpenWindowFn, classifyWhiteboardLink, openInNewTab } from '../../utils/whiteboard-link.util';
import { WHITEBOARD_CANVAS_LOADER, WhiteboardCanvas, langCodeFor } from './whiteboard-canvas';
import { openWhiteboardLinkDialog } from './whiteboard-link-dialog/whiteboard-link-dialog.component';

/** Kendi sözlüğü: `public/i18n/whiteboard/<lang>.json`. */
const SCOPE = 'whiteboard';

/** "Ders bitiyor" uyarısının başladığı kalan süre. */
export const WHITEBOARD_ENDING_SOON_MS = 5 * 60_000;
/** Kalan süre saatinin tazelenme aralığı. */
export const WHITEBOARD_CLOCK_TICK_MS = 15_000;
/** Tuval (chunk + Excalidraw ilk sahne kurulumu) bu sürede hazır olmazsa "yüklenemedi" + tekrar dene. */
export const WHITEBOARD_MOUNT_TIMEOUT_MS = 20_000;

const STATUS_ICONS: Record<WhiteboardConnectionStatus, string> = {
  connecting: 'sync',
  connected: 'cloud_done',
  reconnecting: 'sync_problem',
  disconnected: 'cloud_off',
  closed: 'lock',
};

/**
 * Ders oturumu ortak çizim tahtası (issue #98). Excalidraw tuvali tembel yüklenir (`WHITEBOARD_CANVAS_LOADER`,
 * yalnızca tarayıcıda ve tuval host'u DOM'a girdikten sonra); senkronizasyon `WhiteboardSyncService`'tedir.
 *
 * `ViewEncapsulation.None`: Excalidraw'ın CSS'i (React DOM'u Angular öznitelikleri taşımaz) yalnızca bu komponentle
 * yüklenir; komponentin kendi kuralları `.wb` kökü altında kapsüllenir.
 */
@Component({
  selector: 'app-whiteboard',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope(SCOPE), WhiteboardSyncService],
  templateUrl: './whiteboard.component.html',
  styleUrl: './whiteboard.component.scss',
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WhiteboardComponent {
  readonly bookingId = input.required<number>();

  protected readonly sync = inject(WhiteboardSyncService);
  private readonly loadCanvas = inject(WHITEBOARD_CANVAS_LOADER);
  private readonly colorScheme = inject(ColorSchemeService);
  private readonly locale = inject(LocaleService);
  private readonly transloco = inject(TranslocoService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);
  private readonly zone = inject(NgZone);
  private readonly destroyRef = inject(DestroyRef);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly document = inject(DOCUMENT);

  private readonly canvasHost = viewChild<ElementRef<HTMLDivElement>>('canvasHost');
  private initStarted = false;
  private readonly canvas = signal<WhiteboardCanvas | null>(null);
  private destroyed = false;
  /** Bağlantı onay dialogu açık (issue #332): ikinci bir dialog açılmaz. */
  private linkDialogOpen = false;
  private clock: ReturnType<typeof setInterval> | null = null;
  /** Devam eden mount'u iptal eder (zaman aşımı/yıkım): yarım React kökü host'ta kalmasın. */
  private mountAbort: AbortController | null = null;
  private resizeObserver: ResizeObserver | null = null;

  /** Tuval modülünün yüklenme durumu. */
  protected readonly canvasState = signal<'loading' | 'ready' | 'failed'>('loading');
  protected readonly now = signal(Date.now());

  protected readonly statusIcon = computed(() => STATUS_ICONS[this.sync.status()]);

  private readonly msLeft = computed(() => {
    const closesAt = this.sync.windowClosesAt();
    return closesAt ? closesAt.getTime() - this.now() : null;
  });

  /** Pencere kapanışına 5 dk (ya da daha az) kaldı ve tahta hâlâ açık. */
  protected readonly endingSoon = computed(() => {
    const left = this.msLeft();
    return !this.sync.readOnly() && left !== null && left > 0 && left <= WHITEBOARD_ENDING_SOON_MS;
  });

  protected readonly minutesLeft = computed(() => Math.max(1, Math.ceil((this.msLeft() ?? 0) / 60_000)));

  /** Kapanış mesajı anahtarı (scope'a göreli). */
  protected readonly closedMessageKey = computed(() =>
    this.sync.readOnly() ? `closed.${this.sync.closedReason() ?? 'generic'}` : null
  );

  /** Katılımı engelleyen hata anahtarı (scope'a göreli). Kapanış mesajı varsa o önceliklidir. */
  protected readonly errorMessageKey = computed(() => {
    const code = this.sync.error();
    return code && !this.sync.readOnly() ? `errors.${code}` : null;
  });

  constructor() {
    // Yalnızca tarayıcıda (SSR/prerender'da çalışmaz) ve host element render edildikten sonra: tuval chunk'ı burada iner.
    effect(() => {
      const host = this.canvasHost()?.nativeElement;
      if (!host || !this.isBrowser || this.initStarted) {
        return;
      }
      this.initStarted = true;
      untracked(() => {
        void this.initCanvas(host);
        this.zone.runOutsideAngular(() => {
          this.clock = setInterval(() => this.now.set(Date.now()), WHITEBOARD_CLOCK_TICK_MS);
        });
      });
    });

    effect(() => this.canvas()?.setViewMode(this.sync.readOnly()));
    effect(() => this.canvas()?.setTheme(this.colorScheme.colorScheme()));
    effect(() => this.canvas()?.setLangCode(langCodeFor(this.locale.locale())));
    effect(() => {
      const canvas = this.canvas();
      const pointer = this.sync.peerPointer();
      canvas?.setPeerPointer(pointer, pointer ? this.roleLabel(pointer.role) : '');
    });

    this.sync.warnings$.pipe(takeUntilDestroyed()).subscribe((warning) => this.showWarning(warning));

    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      if (this.clock !== null) {
        clearInterval(this.clock);
      }
      this.mountAbort?.abort();
      this.resizeObserver?.disconnect();
      this.sync.destroy();
      const canvas = this.canvas();
      if (canvas) {
        canvas.destroy();
        // Excalidraw `langCode` ile <html lang>'ı değiştirir; uygulamanın aktif dili geri yazılır.
        this.document.documentElement.lang = this.locale.locale();
      }
    });
  }

  /** Tuval yüklenemediyse yeniden dener. */
  protected retryCanvas(): void {
    const host = this.canvasHost()?.nativeElement;
    if (this.canvasState() === 'failed' && host) {
      void this.initCanvas(host);
    }
  }

  protected retryConnection(): void {
    void this.sync.retry();
  }

  private async initCanvas(host: HTMLElement): Promise<void> {
    this.canvasState.set('loading');
    const abort = new AbortController();
    this.mountAbort = abort;
    const timeout = setTimeout(() => abort.abort(), WHITEBOARD_MOUNT_TIMEOUT_MS);
    try {
      const mount = await this.loadCanvas();
      if (this.destroyed || abort.signal.aborted) {
        throw new Error('aborted');
      }
      // React olayları (her fare hareketi) Angular zone'u dışında: gereksiz değişiklik algılaması tetiklenmez;
      // signal güncellemeleri kendi planlamasını yapar.
      const canvas = await this.zone.runOutsideAngular(() =>
        mount(
          host,
          {
            theme: this.colorScheme.colorScheme(),
            langCode: langCodeFor(this.locale.locale()),
            readOnly: this.sync.readOnly(),
          },
          {
            onChange: () => this.sync.notifyLocalChange(),
            onPointer: (pointer) => this.sync.notifyLocalPointer(pointer),
            onLinkOpen: (link) => this.zone.run(() => this.openLink(link)),
          },
          abort.signal
        )
      );
      if (this.destroyed) {
        canvas.destroy();
        return;
      }
      this.canvas.set(canvas);
      this.canvasState.set('ready');
      this.observeVisibility(host, canvas);
      this.sync.start(this.bookingId(), canvas);
    } catch {
      if (!this.destroyed) {
        this.canvasState.set('failed');
      }
    } finally {
      clearTimeout(timeout);
      if (this.mountAbort === abort) {
        this.mountAbort = null;
      }
    }
  }

  /**
   * Dar ekranda tahta gizli sekmedeyken (display:none, 0×0) yüklenebilir. Görünür olduğu an Excalidraw ölçüleri
   * tazelenir; aksi halde imleç/çizim koordinatları kayık kalabilir.
   */
  private observeVisibility(host: HTMLElement, canvas: WhiteboardCanvas): void {
    if (typeof ResizeObserver === 'undefined') {
      return;
    }
    let hidden = host.clientWidth === 0 || host.clientHeight === 0;
    this.resizeObserver = new ResizeObserver((entries) => {
      const rect = entries[entries.length - 1]?.contentRect;
      const nowHidden = !rect || rect.width === 0 || rect.height === 0;
      if (hidden && !nowHidden) {
        canvas.refresh();
      }
      hidden = nowHidden;
    });
    this.zone.runOutsideAngular(() => this.resizeObserver?.observe(host));
  }

  /**
   * Tahtadaki bağlantı (issue #332): http(s) dışı şema sessizce engellenir (kısa uyarı), aynı origin doğrudan, dış
   * origin onaydan sonra açılır. Açılış her zaman yeni sekmede ve `noopener,noreferrer` ile.
   */
  private openLink(link: string | null | undefined): void {
    const target = classifyWhiteboardLink(link, this.document.location.origin);
    const open: OpenWindowFn = (url, name, features) => this.document.defaultView?.open(url, name, features);
    switch (target.kind) {
      case 'blocked':
        this.showWarning('linkBlocked');
        return;
      case 'sameOrigin':
        openInNewTab(target.url, open);
        return;
      case 'external':
        // Aynı anda tek onay dialogu: art arda tıklamalar (ya da karşı tarafın tetiklediği) üst üste dialog açmaz.
        if (this.linkDialogOpen) {
          return;
        }
        this.linkDialogOpen = true;
        openWhiteboardLinkDialog(this.dialog, { url: target.url, host: target.host })
          .afterClosed()
          .subscribe((proceed) => {
            this.linkDialogOpen = false;
            if (proceed === true && !this.destroyed) {
              openInNewTab(target.url, open);
            }
          });
    }
  }

  private roleLabel(role: WhiteboardRole): string {
    return this.transloco.translate<string>(`${SCOPE}.roles.${role}`) ?? role;
  }

  private showWarning(warning: WhiteboardWarning): void {
    this.zone.run(() =>
      this.snackBar.open(
        this.transloco.translate<string>(`${SCOPE}.warnings.${warning}`),
        this.transloco.translate<string>(`${SCOPE}.dismiss`),
        { duration: 5000 }
      )
    );
  }
}

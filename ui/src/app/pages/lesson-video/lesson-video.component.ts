import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';
import { VideoSession } from '../../models/booking.model';
import { AuthService } from '../../services/auth.service';
import { BookingService } from '../../services/booking.service';
import {
  JitsiMeetExternalApi,
  JitsiScriptLoaderService,
} from '../../services/jitsi-script-loader.service';

/**
 * Odanın nasıl gömüldüğü:
 * - `iframeApi`: Jitsi IFrame API (`external_api.js`) — olay/`dispose()` kontrolü verir.
 * - `direct`: backend'in verdiği `joinUrl` ile düz `<iframe>`. IFrame API iframe adresini
 *   her zaman `https://` ile kurduğu için http self-host (yerel geliştirme) bu yolu kullanır.
 */
type EmbedMode = 'iframeApi' | 'direct';

/** İki adres aynı origin'de mi (geçersiz adres = hayır). */
function sameOrigin(a: string, b: string): boolean {
  try {
    return new URL(a).origin === new URL(b).origin;
  } catch {
    return false;
  }
}

@Component({
  selector: 'app-lesson-video',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './lesson-video.component.html',
  styleUrls: ['./lesson-video.component.scss'],
})
export class LessonVideoComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly bookingService = inject(BookingService);
  private readonly scriptLoader = inject(JitsiScriptLoaderService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly authService = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly container = viewChild<ElementRef<HTMLDivElement>>('jitsiContainer');
  private api: JitsiMeetExternalApi | null = null;

  protected readonly bookingId = signal<number | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly session = signal<VideoSession | null>(null);
  protected readonly mode = signal<EmbedMode>('direct');

  protected readonly title = computed(() => {
    const id = this.bookingId();
    return id === null ? 'Ders' : `Ders #${id}`;
  });

  protected readonly roleLabel = computed(() =>
    this.session()?.isModerator ? 'Moderatör (öğretmen)' : 'Katılımcı'
  );

  /** Düz iframe modunda kullanılan adres — yalnızca backend'den gelen `joinUrl`. */
  protected readonly joinUrl = computed<SafeResourceUrl | null>(() => {
    const session = this.session();
    if (!session || this.mode() !== 'direct') {
      return null;
    }
    return this.sanitizer.bypassSecurityTrustResourceUrl(session.joinUrl);
  });

  constructor() {
    // Oturum ve konteyner hazır olduğunda odayı IFrame API ile göm.
    effect(() => {
      const session = this.session();
      const host = this.container()?.nativeElement;
      if (!session || !host || this.mode() !== 'iframeApi' || this.api) {
        return;
      }
      void this.embedWithIframeApi(session, host);
    });

    // Oda kaynağı sayfa yıkılırken bırakılır (ayrı bir ngOnDestroy tutmaya gerek yok).
    this.destroyRef.onDestroy(() => this.disposeApi());
  }

  ngOnInit(): void {
    const raw = this.route.snapshot.paramMap.get('bookingId');
    const id = Number(raw);
    if (!raw || !Number.isInteger(id) || id <= 0) {
      this.error.set('Geçersiz ders adresi.');
      return;
    }
    this.bookingId.set(id);
    this.load();
  }

  protected load(): void {
    const id = this.bookingId();
    if (id === null || this.loading()) {
      return;
    }
    this.disposeApi();
    this.session.set(null);
    this.loading.set(true);
    this.error.set(null);

    this.bookingService
      .getVideoSession(id)
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => {
          if (!res?.success || !res.session) {
            this.error.set(res?.message || 'Görüşme odası açılamadı.');
            return;
          }
          // joinUrl yalnızca backend'in bildirdiği Jitsi sunucusuna işaret etmeli —
          // sanitizer bypass'ı öncesi tek doğrulama noktası burası.
          if (!sameOrigin(res.session.joinUrl, res.session.baseUrl)) {
            this.error.set('Görüşme adresi doğrulanamadı.');
            return;
          }
          // IFrame API iframe src'sini `https://<domain>` olarak kurar; http self-host'ta
          // çalışmaz, o durumda backend'in verdiği joinUrl ile düz iframe kullanılır.
          this.mode.set(res.session.baseUrl.startsWith('https://') ? 'iframeApi' : 'direct');
          this.session.set(res.session);
        },
        error: (err: HttpErrorResponse) =>
          this.error.set(this.bookingService.extractError(err, 'Görüşme odası açılamadı.')),
      });
  }

  /** "Dersten ayrıl" — odayı kapatıp rolün randevu listesine döner. */
  protected leave(): void {
    this.disposeApi();
    const target = this.authService.getUserRole() === 'Teacher' ? '/booking-requests' : '/my-bookings';
    void this.router.navigateByUrl(target);
  }

  private async embedWithIframeApi(session: VideoSession, host: HTMLElement): Promise<void> {
    try {
      const JitsiMeetExternalApiCtor = await this.scriptLoader.load(session.baseUrl);
      if (this.session() !== session) {
        return; // Bu arada sayfa yenilendi/terk edildi.
      }
      this.api = new JitsiMeetExternalApiCtor(session.domain, {
        roomName: session.roomName,
        jwt: session.token,
        parentNode: host,
        width: '100%',
        height: '100%',
        configOverwrite: {
          prejoinConfig: { enabled: true },
          disableDeepLinking: true,
        },
        interfaceConfigOverwrite: {
          SHOW_JITSI_WATERMARK: false,
          SHOW_BRAND_WATERMARK: false,
          MOBILE_APP_PROMO: false,
          TOOLBAR_BUTTONS: [
            'microphone',
            'camera',
            'desktop',
            'chat',
            'raisehand',
            'tileview',
            'settings',
            'hangup',
          ],
        },
      });
      this.api.addListener('readyToClose', () => this.leave());
    } catch {
      // Betik yüklenemediyse düz iframe'e düş — oda yine de açılabilsin.
      this.mode.set('direct');
    }
  }

  private disposeApi(): void {
    this.api?.dispose();
    this.api = null;
  }
}

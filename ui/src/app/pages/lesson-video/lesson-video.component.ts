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
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { finalize, map, take } from 'rxjs';
import {
  TEACHER_UNAVAILABLE_ERROR_CODE,
  VideoSession,
  isTeacherUnavailableError,
} from '../../models/booking.model';
import { AuthService } from '../../services/auth.service';
import { BookingService } from '../../services/booking.service';
import {
  JitsiMeetExternalApi,
  JitsiScriptLoaderService,
} from '../../services/jitsi-script-loader.service';
import { WhiteboardComponent } from '../../shared/components/whiteboard/whiteboard.component';
import { provideExcalidrawCanvas } from '../../shared/components/whiteboard/excalidraw-canvas.provider';

/** Sayfanın Transloco scope'u: `public/i18n/lesson-video/<lang>.json` (issue #183). */
const SCOPE = 'lesson-video';

/**
 * Odanın nasıl gömüldüğü:
 * - `iframeApi`: Jitsi IFrame API (`external_api.js`) — olay/`dispose()` kontrolü verir.
 * - `direct`: backend'in verdiği `joinUrl` ile düz `<iframe>`. IFrame API iframe adresini
 *   her zaman `https://` ile kurduğu için http self-host (yerel geliştirme) bu yolu kullanır.
 */
type EmbedMode = 'iframeApi' | 'direct';

/** Dar ekranda (sekmeli yerleşim) görünen bölme — issue #98. Geniş ekranda ikisi birlikte görünür. */
export type LessonPane = 'video' | 'board';

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
  imports: [
    MatButtonModule,
    MatButtonToggleModule,
    MatIconModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
    WhiteboardComponent,
  ],
  // Issue #98: ortak tahta tuvali (Excalidraw) `@defer` bloğunda ve kendi dinamik chunk'ında yüklenir.
  providers: [provideTranslocoScope(SCOPE), provideExcalidrawCanvas()],
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
  private readonly transloco = inject(TranslocoService);

  private readonly container = viewChild<ElementRef<HTMLDivElement>>('jitsiContainer');
  private api: JitsiMeetExternalApi | null = null;

  protected readonly bookingId = signal<number | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly session = signal<VideoSession | null>(null);
  protected readonly mode = signal<EmbedMode>('direct');
  /** Dar ekranda seçili bölme (Video / Tahta). */
  protected readonly pane = signal<LessonPane>('video');
  /**
   * Issue #298: backend 409 `errorCode: "TeacherUnavailable"` döndü (öğretmen askıda). Durum kalıcıdır;
   * "Tekrar dene" gizlenir, yalnızca geri dönüş sunulur.
   */
  protected readonly teacherUnavailable = signal(false);

  /**
   * Scope sözlüğü yüklendiğinde `true` olur. Başlık şablon dışında (iframe `title` özniteliği ve
   * `<h1>`) üretildiği için senkron `translate()` ancak bu bayraktan sonra güvenlidir.
   */
  private readonly scopeReady = toSignal(
    this.transloco.selectTranslate<string>('titleFallback', {}, SCOPE).pipe(map(() => true)),
    { initialValue: false }
  );

  protected readonly title = computed(() => {
    if (!this.scopeReady()) {
      return '';
    }
    const id = this.bookingId();
    return id === null
      ? (this.transloco.translate<string>(`${SCOPE}.titleFallback`) ?? '')
      : (this.transloco.translate<string>(`${SCOPE}.title`, { id }) ?? '');
  });

  /** Şablonda çevrilen rol anahtarı (scope'a göreli). */
  protected readonly roleLabelKey = computed(() =>
    this.session()?.isModerator ? 'role.moderator' : 'role.participant'
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
      this.setError('error.invalidRoute');
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
    this.teacherUnavailable.set(false);

    this.bookingService
      .getVideoSession(id)
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => {
          if (res?.errorCode === TEACHER_UNAVAILABLE_ERROR_CODE) {
            this.showTeacherUnavailable(res.message);
            return;
          }
          if (!res?.success || !res.session) {
            this.setError('error.roomUnavailable', res?.message);
            return;
          }
          // joinUrl yalnızca backend'in bildirdiği Jitsi sunucusuna işaret etmeli —
          // sanitizer bypass'ı öncesi tek doğrulama noktası burası.
          if (!sameOrigin(res.session.joinUrl, res.session.baseUrl)) {
            this.setError('error.addressUnverified');
            return;
          }
          // IFrame API iframe src'sini `https://<domain>` olarak kurar; http self-host'ta
          // çalışmaz, o durumda backend'in verdiği joinUrl ile düz iframe kullanılır.
          this.mode.set(res.session.baseUrl.startsWith('https://') ? 'iframeApi' : 'direct');
          this.session.set(res.session);
        },
        // Yedek mesaj sözlükten gelir; scope henüz yüklenmemiş olabileceği için `selectTranslate`.
        error: (err: HttpErrorResponse) => {
          if (isTeacherUnavailableError(err.status, err.error)) {
            this.showTeacherUnavailable((err.error as { message?: unknown }).message);
            return;
          }
          this.transloco
            .selectTranslate<string>('error.roomUnavailable', {}, SCOPE)
            .pipe(take(1), takeUntilDestroyed(this.destroyRef))
            .subscribe((fallback) => this.error.set(this.bookingService.extractError(err, fallback)));
        },
      });
  }

  /**
   * Issue #298: öğretmen geçici olarak müsait değil. Backend'in yerelleştirilmiş `message`'ı (metin olarak
   * interpolasyonla gösterilir) varsa o, yoksa sözlükteki karşılığı. Otomatik yeniden deneme yapılmaz.
   */
  private showTeacherUnavailable(serverMessage: unknown): void {
    this.teacherUnavailable.set(true);
    const message = typeof serverMessage === 'string' ? serverMessage.trim() : '';
    this.setError('error.teacherUnavailable', message || null);
  }

  /**
   * Hata metnini sözlükten okur. Backend bir mesaj döndüyse o gösterilir; aksi halde çeviri.
   * Scope henüz yüklenmemiş olabileceği için `selectTranslate` kullanılır.
   */
  private setError(messageKey: string, serverMessage?: string | null): void {
    if (serverMessage) {
      this.error.set(serverMessage);
      return;
    }
    this.transloco
      .selectTranslate<string>(messageKey, {}, SCOPE)
      .pipe(take(1), takeUntilDestroyed(this.destroyRef))
      .subscribe((message) => this.error.set(message));
  }

  /** "Dersten ayrıl" — odayı kapatıp rolün randevu listesine döner. */
  protected leave(): void {
    this.disposeApi();
    // Issue #418: `/booking-requests` bağımsız öğretmen sayfası — okula bağlı olduğu bilinen öğretmen takvimine döner.
    const target =
      this.authService.getUserRole() !== 'Teacher'
        ? '/my-bookings'
        : AuthService.isIndependentTutorOf(this.authService.user()) !== false
          ? '/booking-requests'
          : '/my-calendar';
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

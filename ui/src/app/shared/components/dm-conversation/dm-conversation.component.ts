import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, take } from 'rxjs';
import {
  ConversationMessages,
  DIRECT_MESSAGES_SCOPE,
  DIRECT_MESSAGE_BODY_MAX_LENGTH,
  DirectMessage,
  DirectMessageSenderRole,
  SendDirectMessageResult,
} from '../../../models/direct-message.model';
import { parseUtcDate } from '../../../pages/notifications/notification-format';
import { DirectMessageService } from '../../../services/direct-message.service';
import { activeIntlLocale } from '../../utils/active-locale.util';
import { directMessageError } from '../../utils/direct-message-error.util';
import { openDmBlockDialog } from '../dm-block-dialog/dm-block-dialog.component';
import { openDmReportDialog } from '../dm-report-dialog/dm-report-dialog.component';

interface ConversationHeader {
  name: string;
  avatar: string;
  canSend: boolean;
  isBlocked: boolean;
}

export interface DmMessageSentEvent {
  conversationId: number;
  message: DirectMessage | null;
  conversationCreated: boolean;
}

export interface DmMarkedReadEvent {
  conversationId: number;
  /** Bu çağrıda okundu yapılan mesaj sayısı (zaten okunmuşsa 0). */
  markedCount: number;
}

export interface DmBlockedChangeEvent {
  conversationId: number;
  isBlocked: boolean;
}

/**
 * Issue #106 — 1:1 konuşma paneli (öğrenci ve öğretmen sayfaları ortak; maket 8b / 9 sağ panel).
 *
 * - Balon görünüm: kendi mesajın sağda (`--ms-action-background`), karşı taraf solda (`--ms-surface-medium`).
 *   Okundu tiki / yazıyor göstergesi kasıtlı olarak yok.
 * - Okundu: GET işaretlemez; ilk sayfa görününce `POST .../read { upToMessageId }` (eski sayfa yüklemek değil).
 * - Mesaj kabı `role="log" aria-live="polite"`; gövde yalnız interpolasyonla (düz metin) render edilir.
 * - `conversationId` yoksa (öğrencinin yeni konuşması) `teacherId`'ye ilk mesaj gönderilir; dönen Id ile panel
 *   konuşmaya bağlanır ve `conversationCreated` yayılır.
 * - `canSend=false`: öğrencide nötr bilgi (engel/ilişki sızdırılmaz), öğretmende "ilişki sona erdi" bandı; giriş pasif.
 */
@Component({
  selector: 'app-dm-conversation',
  standalone: true,
  imports: [
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(DIRECT_MESSAGES_SCOPE)],
  templateUrl: './dm-conversation.component.html',
  styleUrls: ['./dm-conversation.component.scss'],
})
export class DmConversationComponent {
  private readonly api = inject(DirectMessageService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly injector = inject(Injector);

  /** Paneli görüntüleyenin rolü. */
  readonly viewerRole = input.required<DirectMessageSenderRole>();
  /** Açılacak konuşma; yoksa `teacherId` ile yeni konuşma (yalnız öğrenci). */
  readonly conversationId = input<number | null>(null);
  readonly teacherId = input<number | null>(null);
  /** Yükleme bitmeden başlıkta gösterilecek karşı taraf bilgisi (listeden). */
  readonly counterpartName = input('');
  readonly counterpartAvatar = input('');
  /**
   * Listeden bilinen okunmamış sayısı. `0` ise açılışta okundu isteği atılmaz; `null` (bilinmiyor, ör. derin link)
   * ya da > 0 ise konuşma görünür olunca en yeni yüklü mesaj Id'siyle `POST .../read` çağrılır.
   */
  readonly unreadCount = input<number | null>(null);

  readonly back = output<void>();
  readonly loaded = output<ConversationMessages>();
  /** `POST .../read` başarılı oldu — liste satırı sıfırlanır; rozet yalnız `markedCount > 0` ise azalır. */
  readonly markedRead = output<DmMarkedReadEvent>();
  readonly messageSent = output<DmMessageSentEvent>();
  readonly blockedChange = output<DmBlockedChangeEvent>();

  protected readonly maxLength = DIRECT_MESSAGE_BODY_MAX_LENGTH;
  private readonly logRef = viewChild<ElementRef<HTMLElement>>('messageLog');

  protected readonly activeId = signal<number | null>(null);
  protected readonly header = signal<ConversationHeader>({ name: '', avatar: '', canSend: true, isBlocked: false });
  protected readonly messages = signal<DirectMessage[]>([]);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly hasMore = signal(false);
  private readonly nextBeforeId = signal<number | null>(null);
  protected readonly loadingOlder = signal(false);
  protected readonly olderError = signal<string | null>(null);

  protected readonly draft = signal('');
  protected readonly sending = signal(false);
  protected readonly sendError = signal<string | null>(null);
  protected readonly blockBusy = signal(false);

  protected readonly isTeacher = computed(() => this.viewerRole() === 'Teacher');
  protected readonly isNew = computed(() => this.activeId() === null);
  protected readonly draftLength = computed(() => this.draft().length);
  protected readonly tooLong = computed(() => this.draftLength() > this.maxLength);
  protected readonly canSend = computed(() => this.header().canSend && !this.loading() && !this.loadError());
  protected readonly canSubmit = computed(
    () => this.canSend() && !this.sending() && !this.tooLong() && this.draft().trim().length > 0,
  );
  /** Gönderim kapalıyken gösterilecek bilgi anahtarı (rol bazlı; öğrencide nötr). */
  protected readonly cannotSendKey = computed(() =>
    this.header().canSend || this.loading() || this.loadError()
      ? null
      : this.isTeacher()
        ? 'relationshipEnded'
        : 'cannotSend',
  );

  private loadSub?: Subscription;
  private olderSub?: Subscription;
  private sendSub?: Subscription;
  private readSub?: Subscription;

  constructor() {
    // Hata metinleri senkron `translate()` ile okunur; scope baştan yüklensin.
    this.transloco.load(`${DIRECT_MESSAGES_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
    effect(() => {
      const conversationId = this.conversationId();
      const teacherId = this.teacherId();
      untracked(() => this.open(conversationId, teacherId));
    });
    inject(DestroyRef).onDestroy(() => this.cancelRequests());
  }

  // ---- Yükleme ----

  protected retry(): void {
    this.open(this.activeId(), this.teacherId());
  }

  private open(conversationId: number | null, teacherId: number | null): void {
    // Panel zaten bu konuşmada (ör. ilk mesajla yeni açılan konuşmanın Id'si input'a geri yazıldı) ve hata yoksa
    // yeniden yükleme yapılmaz; aksi hâlde gönderilen mesaj ve okuma konumu kaybolurdu.
    if (conversationId !== null && conversationId === this.activeId() && !this.loadError()) {
      return;
    }
    this.cancelRequests();
    this.activeId.set(conversationId);
    this.header.set({
      name: this.counterpartName(),
      avatar: this.counterpartAvatar(),
      canSend: true,
      isBlocked: false,
    });
    this.messages.set([]);
    this.hasMore.set(false);
    this.nextBeforeId.set(null);
    this.loadError.set(null);
    this.olderError.set(null);
    this.sendError.set(null);
    this.sending.set(false);
    this.loadingOlder.set(false);
    this.draft.set('');

    if (conversationId === null) {
      // Yeni konuşma: ilk mesaj gönderilene kadar geçmiş yok. Öğretmenin cevap izni yine sunucuda denetlenir.
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.loadSub = this.api.getMessages(conversationId).subscribe({
      next: (page) => {
        this.loading.set(false);
        this.header.set({
          name: page.counterpartName || this.counterpartName(),
          avatar: page.counterpartAvatar ?? '',
          canSend: page.canSend === true,
          isBlocked: page.isBlocked === true,
        });
        this.messages.set(page.items ?? []);
        this.hasMore.set(page.hasMore === true);
        this.nextBeforeId.set(page.nextBeforeId ?? null);
        this.scrollToBottom();
        this.loaded.emit(page);
        this.markLatestRead(conversationId, page.items ?? []);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.loadError.set(directMessageError(err, this.t, 'conversation.loadError').message);
      },
    });
  }

  /**
   * Konuşma görünür olunca (ilk sayfa yüklendi) en yeni yüklü mesaja kadar okundu işaretler. Eski sayfa
   * (`beforeId`) yüklemek okundu yapmaz. Karşı taraftan mesaj yoksa ya da listede okunmamış yoksa istek atılmaz.
   */
  private markLatestRead(conversationId: number, items: DirectMessage[]): void {
    const unread = this.unreadCount();
    if (unread === 0 || !items.some((m) => !m.isMine)) {
      return;
    }
    const upToMessageId = items[items.length - 1].id;
    this.readSub = this.api.markRead(conversationId, upToMessageId).subscribe({
      next: (result) =>
        this.markedRead.emit({ conversationId, markedCount: Math.max(0, Number(result?.markedCount) || 0) }),
      // Okundu işaretlenemezse sessiz: rozet bir sonraki tazelemede düzelir.
      error: () => {},
    });
  }

  protected loadOlder(): void {
    const id = this.activeId();
    const beforeId = this.nextBeforeId();
    if (id === null || beforeId === null || this.loadingOlder()) {
      return;
    }
    const log = this.logRef()?.nativeElement;
    const distanceFromBottom = log ? log.scrollHeight - log.scrollTop : 0;
    this.loadingOlder.set(true);
    this.olderError.set(null);
    this.olderSub = this.api.getMessages(id, beforeId).subscribe({
      next: (page) => {
        this.loadingOlder.set(false);
        const known = new Set(this.messages().map((m) => m.id));
        const older = (page.items ?? []).filter((m) => !known.has(m.id));
        this.messages.update((list) => [...older, ...list]);
        this.hasMore.set(page.hasMore === true);
        this.nextBeforeId.set(page.nextBeforeId ?? null);
        // Okuma konumu korunur: eklenen eski mesajlar görünümü aşağı itmesin.
        afterNextRender(
          () => {
            const el = this.logRef()?.nativeElement;
            if (el) {
              el.scrollTop = el.scrollHeight - distanceFromBottom;
              if (!this.hasMore()) {
                el.focus();
              }
            }
          },
          { injector: this.injector },
        );
      },
      error: (err: unknown) => {
        this.loadingOlder.set(false);
        this.olderError.set(directMessageError(err, this.t, 'conversation.loadOlderError').message);
      },
    });
  }

  // ---- Gönderim ----

  protected onDraft(event: Event): void {
    this.draft.set((event.target as HTMLTextAreaElement).value);
  }

  /** Ctrl/Cmd+Enter gönderir; düz Enter satır sonu ekler. */
  protected onComposerKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      this.send();
    }
  }

  protected send(): void {
    if (!this.canSubmit()) {
      return;
    }
    const body = this.draft().trim();
    const conversationId = this.activeId();
    const teacherId = this.teacherId();
    let request$;
    if (conversationId !== null) {
      request$ = this.api.sendToConversation(conversationId, body);
    } else if (teacherId !== null && !this.isTeacher()) {
      request$ = this.api.sendToTeacher(teacherId, body);
    } else {
      return;
    }

    this.sending.set(true);
    this.sendError.set(null);
    this.sendSub = request$.subscribe({
      next: (result) => this.onSent(result),
      error: (err: unknown) => {
        this.sending.set(false);
        const info = directMessageError(err, this.t);
        if (info.code === 'CannotMessageTeacher' || info.code === 'RelationshipEnded') {
          // Gönderim kapandı: bilgi bandı gösterilir, taslak korunur (kopyalanabilir), ayrı hata kutusu yok.
          this.header.update((h) => ({ ...h, canSend: false }));
          return;
        }
        this.sendError.set(info.message);
      },
    });
  }

  private onSent(result: SendDirectMessageResult): void {
    this.sending.set(false);
    this.draft.set('');
    const conversationId = result?.conversationId ?? this.activeId();
    if (conversationId == null) {
      return;
    }
    const created = this.activeId() === null;
    this.activeId.set(conversationId);
    const message = result.directMessage ?? null;
    if (message) {
      this.messages.update((list) => [...list, message]);
    }
    this.scrollToBottom();
    this.messageSent.emit({ conversationId, message, conversationCreated: created || result.conversationCreated === true });
  }

  // ---- Eylemler ----

  protected reportConversation(): void {
    this.report();
  }

  protected reportMessage(message: DirectMessage): void {
    this.report(message.id);
  }

  private report(messageId?: number): void {
    const conversationId = this.activeId();
    if (conversationId === null) {
      return;
    }
    openDmReportDialog(this.dialog, { conversationId, messageId })
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.snackBar.open(
            this.t(result.alreadyReported ? 'conversation.alreadyReported' : 'conversation.reported'),
            this.t('conversation.close'),
            { duration: 5000 },
          );
        }
      });
  }

  protected toggleBlock(): void {
    const conversationId = this.activeId();
    if (conversationId === null || !this.isTeacher() || this.blockBusy()) {
      return;
    }
    const block = !this.header().isBlocked;
    openDmBlockDialog(this.dialog, { block, studentName: this.header().name })
      .afterClosed()
      .subscribe((confirmed) => {
        if (confirmed !== true) {
          return;
        }
        this.blockBusy.set(true);
        (block ? this.api.block(conversationId) : this.api.unblock(conversationId)).subscribe({
          next: (result) => {
            this.blockBusy.set(false);
            const isBlocked = result?.isBlocked ?? block;
            this.header.update((h) => ({ ...h, isBlocked }));
            this.blockedChange.emit({ conversationId, isBlocked });
            this.snackBar.open(this.t(isBlocked ? 'conversation.blockedDone' : 'conversation.unblockedDone'), this.t('conversation.close'), {
              duration: 4000,
            });
          },
          error: (err: unknown) => {
            this.blockBusy.set(false);
            this.snackBar.open(directMessageError(err, this.t, 'conversation.blockError').message, this.t('conversation.close'), {
              duration: 6000,
            });
          },
        });
      });
  }

  // ---- Görünüm yardımcıları ----

  protected initial(name: string): string {
    const first = Array.from(name.trim())[0];
    return first ? first.toLocaleUpperCase(activeIntlLocale()) : '?';
  }

  /**
   * Güvenlik (#106 D1): yalnız kök göreli (`/...`, aynı köken) avatar yolu `<img>` olarak gösterilir; harici URL'ler
   * (izleme pikseli / IP sızıntısı riski), `//` protokol-göreli ve `data:`/`javascript:` şemaları baş harfe düşer.
   * Yapılandırmada güvenilir bir depolama kökeni yok; eklenirse burada izin listesine alınır.
   */
  protected avatarSrc(avatar: string | null | undefined): string | null {
    const value = avatar?.trim() ?? '';
    return /^\/(?![/\\])/.test(value) ? value : null;
  }

  protected timeText(iso: string): string {
    const date = parseUtcDate(iso);
    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleString(activeIntlLocale(), { dateStyle: 'short', timeStyle: 'short' });
  }

  protected isoOf(iso: string): string {
    const date = parseUtcDate(iso);
    return Number.isNaN(date.getTime()) ? '' : date.toISOString();
  }

  private scrollToBottom(): void {
    afterNextRender(
      () => {
        const el = this.logRef()?.nativeElement;
        if (el) {
          el.scrollTop = el.scrollHeight;
        }
      },
      { injector: this.injector },
    );
  }

  private cancelRequests(): void {
    this.loadSub?.unsubscribe();
    this.olderSub?.unsubscribe();
    this.sendSub?.unsubscribe();
    this.readSub?.unsubscribe();
  }

  private readonly t = (key: string, params?: Record<string, unknown>): string =>
    this.transloco.translate<string>(`${DIRECT_MESSAGES_SCOPE}.${key}`, params) ?? key;
}

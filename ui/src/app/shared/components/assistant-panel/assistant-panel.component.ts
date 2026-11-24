import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { DecimalPipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import {
  ASSISTANT_SCOPE,
  AssistantCard,
  ConfirmState,
  FEEDBACK_REASONS,
  FeedbackReason,
  PanelMessage,
  SuggestionChip,
  TopicRow,
} from '../../../models/assistant';
import { ASSISTANT_MAX_MESSAGE_LENGTH, AssistantChatStore } from '../../../services/assistant-chat.store';
import { AssistantPanelService } from '../../../services/assistant-panel.service';
import { AuthService } from '../../../services/auth.service';

/** Sunucu hata kodları; i18n'de karşılığı olmayan kod `generic` olur. */
const KNOWN_ERRORS = new Set([
  'AssistantBudgetExceeded',
  'AssistantDisabledByParent',
  'ParentRequired',
  'AssistantClosedDuringTest',
  'AssistantDeclined',
  'AssistantUnavailable',
  'AssistantDisabled',
  'AssistantRoleNotEnabled',
  'ConversationNotFound',
  'InvalidMessage',
  'Unauthenticated',
]);

const STARTERS = ['weakTopics', 'dailySet', 'practice', 'help'] as const;
/** #1336: velinin önerileri (çocuğun haftası, geciken ödev, zorlandığı konu, kılavuz). */
const PARENT_STARTERS = ['childWeek', 'overdue', 'childWeakTopics', 'childPassword'] as const;

/** `**kalın**` dışında biçim yok; metin olduğu gibi (innerHTML yok). */
export interface TextSegment {
  text: string;
  bold: boolean;
}

export function segmentsOf(text: string): TextSegment[] {
  const parts = text.split('**');
  // Kapanmamış `**` (akış sürerken): son parça düz kalır.
  const closed = parts.length % 2 === 1 ? parts.length : parts.length - 1;
  return parts
    .map((part, i) => ({ text: i >= closed ? `**${part}` : part, bold: i % 2 === 1 && i < closed }))
    .filter((s) => s.text.length > 0);
}

/**
 * Issue #507: uygulama içi asistan paneli. CDK Overlay içinde açılır (launcher), masaüstünde 440px sağ panel, mobilde
 * tam ekran. Mesaj görünümü `dm-conversation`'dan türetildi; kartlar sunucu verisinden (kpi, topicTable, confirm).
 */
@Component({
  selector: 'app-assistant-panel',
  imports: [
    DecimalPipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(ASSISTANT_SCOPE)],
  templateUrl: './assistant-panel.component.html',
  styleUrl: './assistant-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'dialog', 'aria-modal': 'false', 'aria-labelledby': 'assistant-panel-title' },
})
export class AssistantPanelComponent implements AfterViewInit {
  readonly store = inject(AssistantChatStore);
  private readonly panel = inject(AssistantPanelService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Mobilde tam ekran; gezinme eylemiyle panel kapanır. */
  readonly mobile = input(false);
  readonly closed = output<void>();

  readonly maxLength = ASSISTANT_MAX_MESSAGE_LENGTH;

  readonly feedbackReasons = FEEDBACK_REASONS;
  readonly draft = signal('');
  readonly contextDismissed = signal(false);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects)
    ),
    { initialValue: this.router.url }
  );
  readonly contextRoute = computed(() => this.url().split(/[?#]/)[0]);
  readonly roleKey = computed(() => {
    for (const role of ['Student', 'Parent', 'Teacher', 'Admin']) {
      if (this.auth.hasRealmRole(role)) return role.toLowerCase();
    }
    return 'student';
  });
  /** Öneri anahtarları ve boş durum metni role göre (`starters.*`, `empty.*` ya da `parentStarters.*`, `parentEmpty.*`). */
  readonly isParent = computed(() => this.roleKey() === 'parent');
  readonly starters = computed<readonly string[]>(() => (this.isParent() ? PARENT_STARTERS : STARTERS));
  readonly starterPrefix = computed(() => (this.isParent() ? 'parentStarters.' : 'starters.'));
  readonly emptyPrefix = computed(() => (this.isParent() ? 'parentEmpty.' : 'empty.'));

  readonly tooLong = computed(() => this.draft().length > this.maxLength);
  readonly canSend = computed(() => !!this.draft().trim() && !this.tooLong() && !this.store.sending());
  readonly remainingMessages = computed(() => {
    const u = this.store.usage();
    return u ? Math.max(0, u.messagesPerHour - u.messagesThisHour) : null;
  });

  private readonly input = viewChild<ElementRef<HTMLTextAreaElement>>('composer');
  private readonly log = viewChild<ElementRef<HTMLElement>>('log');

  constructor() {
    this.store.loadUsage();

    // Yeni içerik gelince en alta kaydır.
    effect(() => {
      this.store.messages();
      this.store.suggestions();
      untracked(() => queueMicrotask(() => this.scrollToEnd()));
    });

    // Gezinme eylemi (ui_open_screen, onaydan sonra oturum ekranı): mobilde panel ekranı kapatır.
    let seen = this.store.navigated();
    effect(() => {
      const n = this.store.navigated();
      if (n !== seen) {
        seen = n;
        if (untracked(() => this.mobile())) this.closed.emit();
      }
    });

    // Velinin kapattığı sohbet sırasında öğrenildi: giriş noktaları gizlenir.
    effect(() => {
      if (this.store.messages().some((m) => m.errorCode === 'AssistantDisabledByParent')) {
        untracked(() => this.panel.markBlockedByParent());
      }
    });

    this.router.events
      .pipe(filter((e) => e instanceof NavigationEnd), takeUntilDestroyed())
      .subscribe(() => this.contextDismissed.set(false));
  }

  ngAfterViewInit(): void {
    this.input()?.nativeElement.focus();
  }

  onDraft(event: Event): void {
    this.draft.set((event.target as HTMLTextAreaElement).value);
  }

  onComposerKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      this.submit();
    }
  }

  submit(): void {
    if (!this.canSend()) return;
    const text = this.draft();
    this.draft.set('');
    void this.store.send(text);
  }

  sendChip(chip: SuggestionChip): void {
    void this.store.send(chip.prompt);
  }

  sendStarter(prompt: string): void {
    void this.store.send(prompt);
  }

  newConversation(): void {
    this.store.newConversation();
    this.draft.set('');
    this.input()?.nativeElement.focus();
  }

  close(): void {
    this.closed.emit();
  }

  errorKey(code: string): string {
    return KNOWN_ERRORS.has(code) ? code : 'generic';
  }

  confirmState(message: PanelMessage, card: AssistantCard): ConfirmState {
    return card.kind === 'confirm' ? (message.confirmStates[card.pendingActionId] ?? 'pending') : 'pending';
  }

  /** Onay kartı başlığı: bilinen eylem ya da genel metin. */
  actionKey(card: AssistantCard): string {
    if (card.kind !== 'confirm') return 'generic';
    const action = card.preview?.action;
    return action === 'startDailyPractice' || action === 'startPracticeSession' ? action : 'generic';
  }

  rowName(row: TopicRow): string {
    return row.subTopicName || row.topicName || row.subjectName || '';
  }

  rowDetail(row: TopicRow): string {
    if (row.subTopicName) return [row.subjectName, row.topicName].filter(Boolean).join(' · ');
    if (row.topicName) return row.subjectName ?? '';
    return '';
  }

  rowAnswered(row: TopicRow): number | null {
    return row.total ?? row.answered ?? null;
  }

  segments(text: string): TextSegment[] {
    return segmentsOf(text);
  }

  rate(message: PanelMessage, rating: 'up' | 'down', reason: FeedbackReason | null = null): void {
    this.store.rate(message.id, rating, reason);
  }

  trackCard(index: number, card: AssistantCard): string {
    return card.kind === 'confirm' ? card.pendingActionId : `${card.kind}-${index}`;
  }

  private scrollToEnd(): void {
    const el = this.log()?.nativeElement;
    if (el) el.scrollTop = el.scrollHeight;
  }
}

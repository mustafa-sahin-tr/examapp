import { Component, DestroyRef, ElementRef, Injector, OnInit, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioChange, MatRadioModule } from '@angular/material/radio';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Subject, Subscription, debounceTime, distinctUntilChanged, map, skip, startWith, take } from 'rxjs';
import {
  ConversationSummary,
  DIRECT_MESSAGES_SCOPE,
  DIRECT_MESSAGE_PAGE_SIZE,
  MessageableTeacher,
  TEACHER_SEARCH_DEBOUNCE_MS,
  TEACHER_SEARCH_MIN_LENGTH,
} from '../../models/direct-message.model';
import { DirectMessageService } from '../../services/direct-message.service';
import {
  DmConversationComponent,
  DmMarkedReadEvent,
  DmMessageSentEvent,
} from '../../shared/components/dm-conversation/dm-conversation.component';
import {
  DmConversationListComponent,
  DmConversationSelectEvent,
} from '../../shared/components/dm-conversation-list/dm-conversation-list.component';
import { directMessageError } from '../../shared/utils/direct-message-error.util';
import { provideTranslatedPaginatorIntl } from '../../shared/utils/paginator-intl.util';

/** Panelde açık olan konuşma ya da yeni konuşma hedefi. */
export interface StudentDmSelection {
  conversationId: number | null;
  teacherId: number | null;
  name: string;
  avatar: string;
  unreadCount: number | null;
}

const TAB_CONVERSATIONS = 0;
const TAB_TEACHERS = 1;
const PREVIEW_LENGTH = 120;

/**
 * Issue #106 dilim (c) — öğrencinin "Öğretmenime Yaz" sayfası (maket 8a/8b).
 * Sol: "Konuşmalarım" ve "Öğretmenler" (arama ≥2 karakter, 300 ms debounce, sayfalı, ilişki chip'i) sekmeleri;
 * sağ: konuşma paneli. < 900px'te panel tam ekran açılır, geri butonu listeye döner.
 * Engel öğrenciye hiçbir yerde belli edilmez (nötr metinler sözlükte).
 */
@Component({
  selector: 'app-teacher-messages',
  standalone: true,
  imports: [
    MatButtonModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    MatRadioModule,
    MatTabsModule,
    TranslocoDirective,
    DmConversationComponent,
    DmConversationListComponent,
  ],
  providers: [
    provideTranslocoScope(DIRECT_MESSAGES_SCOPE),
    provideTranslatedPaginatorIntl(DIRECT_MESSAGES_SCOPE, `${DIRECT_MESSAGES_SCOPE}.paginator`),
  ],
  templateUrl: './teacher-messages.component.html',
  styleUrls: ['./teacher-messages.component.scss'],
})
export class TeacherMessagesComponent implements OnInit {
  private readonly api = inject(DirectMessageService);
  private readonly transloco = inject(TranslocoService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  protected readonly pageSize = DIRECT_MESSAGE_PAGE_SIZE;
  protected readonly minSearch = TEACHER_SEARCH_MIN_LENGTH;
  protected readonly selectedTab = signal(TAB_CONVERSATIONS);

  // ---- Konuşmalarım ----
  protected readonly conversations = signal<ConversationSummary[]>([]);
  protected readonly conversationsLoading = signal(false);
  protected readonly conversationsError = signal<string | null>(null);
  protected readonly conversationsLoaded = signal(false);
  protected readonly conversationsPage = signal(1);
  protected readonly conversationsTotal = signal(0);

  // ---- Öğretmenler ----
  protected readonly searchInput = signal('');
  /** Sunucuya giden arama terimi (≥2 karakter, yoksa ''). */
  protected readonly searchTerm = signal('');
  protected readonly searchTooShort = computed(() => {
    const length = this.searchInput().trim().length;
    return length > 0 && length < TEACHER_SEARCH_MIN_LENGTH;
  });
  protected readonly teachers = signal<MessageableTeacher[]>([]);
  protected readonly teachersLoading = signal(false);
  protected readonly teachersError = signal<string | null>(null);
  protected readonly teachersLoaded = signal(false);
  protected readonly teachersPage = signal(1);
  protected readonly teachersTotal = signal(0);
  protected readonly teachersTruncated = signal(false);
  protected readonly chosenTeacherId = signal<number | null>(null);
  protected readonly chosenTeacher = computed(
    () => this.teachers().find((teacher) => teacher.teacherId === this.chosenTeacherId()) ?? null,
  );

  // ---- Panel ----
  protected readonly selection = signal<StudentDmSelection | null>(null);
  protected readonly selectedConversationId = computed(() => this.selection()?.conversationId ?? null);

  private readonly searchInputRef = viewChild<ElementRef<HTMLInputElement>>('teacherSearch');
  private readonly search$ = new Subject<string>();
  private conversationsSub?: Subscription;
  private teachersSub?: Subscription;
  private lastTrigger: HTMLElement | null = null;
  private teachersRequested = false;

  constructor() {
    this.transloco.load(`${DIRECT_MESSAGES_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
    this.search$
      .pipe(
        debounceTime(TEACHER_SEARCH_DEBOUNCE_MS),
        map((value) => {
          const term = value.trim();
          return term.length >= TEACHER_SEARCH_MIN_LENGTH ? term : '';
        }),
        // İlk liste '' ile zaten yüklenir: 1 karakterlik giriş (→ '') yeniden istek atmasın.
        startWith(''),
        distinctUntilChanged(),
        skip(1),
        takeUntilDestroyed(),
      )
      .subscribe((term) => {
        this.searchTerm.set(term);
        this.teachersPage.set(1);
        this.loadTeachers();
      });
    this.destroyRef.onDestroy(() => {
      this.conversationsSub?.unsubscribe();
      this.teachersSub?.unsubscribe();
    });
  }

  ngOnInit(): void {
    this.loadConversations();
    // Derin link (dilim b bildirimleri için kanca): /teacher-messages?conversation=<id>
    const id = Number(this.route.snapshot.queryParamMap.get('conversation'));
    if (Number.isInteger(id) && id > 0) {
      this.selection.set({ conversationId: id, teacherId: null, name: '', avatar: '', unreadCount: null });
    }
  }

  // ---- Sekmeler ----

  protected onTabChange(index: number): void {
    this.selectedTab.set(index);
    if (index === TAB_TEACHERS && !this.teachersRequested) {
      this.loadTeachers();
    }
  }

  // ---- Konuşmalar ----

  loadConversations(): void {
    this.conversationsSub?.unsubscribe();
    this.conversationsLoading.set(true);
    this.conversationsError.set(null);
    this.conversationsSub = this.api.getStudentConversations(this.conversationsPage(), this.pageSize).subscribe({
      next: (page) => {
        this.conversationsLoading.set(false);
        const firstLoad = !this.conversationsLoaded();
        this.conversationsLoaded.set(true);
        this.conversations.set(page?.items ?? []);
        this.conversationsTotal.set(page?.totalCount ?? 0);
        // Hiç konuşma yoksa ilk açılışta doğrudan öğretmen listesine geç.
        if (firstLoad && this.conversationsTotal() === 0 && this.selection() === null) {
          this.onTabChange(TAB_TEACHERS);
        }
        this.refreshBadge();
      },
      error: (err: unknown) => {
        this.conversationsLoading.set(false);
        this.conversationsError.set(directMessageError(err, this.t, 'student.conversationsError').message);
      },
    });
  }

  protected onConversationsPage(event: PageEvent): void {
    this.conversationsPage.set(event.pageIndex + 1);
    this.loadConversations();
  }

  protected openConversation(event: DmConversationSelectEvent): void {
    this.lastTrigger = event.trigger;
    const item = event.item;
    this.selection.set({
      conversationId: item.conversationId,
      teacherId: item.teacherId ?? null,
      name: item.counterpartName,
      avatar: item.counterpartAvatar,
      unreadCount: item.unreadCount,
    });
  }

  // ---- Öğretmenler ----

  protected onSearchInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.searchInput.set(value);
    this.search$.next(value);
  }

  protected clearSearch(): void {
    this.searchInput.set('');
    this.search$.next('');
    // Temizle düğmesi kaybolur; odak arama girdisine döner.
    this.searchInputRef()?.nativeElement.focus();
  }

  loadTeachers(): void {
    this.teachersRequested = true;
    this.teachersSub?.unsubscribe();
    this.teachersLoading.set(true);
    this.teachersError.set(null);
    this.teachersSub = this.api.getMessageableTeachers(this.searchTerm(), this.teachersPage(), this.pageSize).subscribe({
      next: (page) => {
        this.teachersLoading.set(false);
        this.teachersLoaded.set(true);
        this.teachers.set(page?.items ?? []);
        this.teachersTotal.set(page?.totalCount ?? 0);
        this.teachersTruncated.set(page?.truncated === true);
        if (!this.teachers().some((teacher) => teacher.teacherId === this.chosenTeacherId())) {
          this.chosenTeacherId.set(null);
        }
      },
      error: (err: unknown) => {
        this.teachersLoading.set(false);
        this.teachers.set([]);
        this.teachersTruncated.set(false);
        // 503 NameLookupUnavailable dahil: tanınan kod yerel metne çevrilir, "Tekrar dene" gösterilir.
        this.teachersError.set(directMessageError(err, this.t, 'student.teachersError').message);
      },
    });
  }

  protected onTeachersPage(event: PageEvent): void {
    this.teachersPage.set(event.pageIndex + 1);
    this.loadTeachers();
  }

  protected onTeacherChosen(event: MatRadioChange): void {
    this.chosenTeacherId.set(event.value as number);
  }

  protected writeToChosen(event: Event): void {
    const teacher = this.chosenTeacher();
    if (!teacher) {
      return;
    }
    this.lastTrigger = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
    const existing = this.conversations().find((c) => c.conversationId === teacher.conversationId);
    this.selection.set({
      conversationId: teacher.conversationId ?? null,
      teacherId: teacher.teacherId,
      name: teacher.fullName,
      avatar: teacher.avatar,
      unreadCount: existing ? existing.unreadCount : null,
    });
  }

  // ---- Panel olayları ----

  protected closePanel(): void {
    this.selection.set(null);
    const trigger = this.lastTrigger;
    afterNextRender(
      () => {
        if (trigger?.isConnected) {
          trigger.focus();
        }
      },
      { injector: this.injector },
    );
  }

  protected onMarkedRead(event: DmMarkedReadEvent): void {
    const conversationId = event.conversationId;
    // Rozet yalnız sunucu gerçekten mesaj okundu yaptıysa azalır (idempotent tekrar → markedCount 0).
    if (event.markedCount > 0) {
      this.api.markConversationRead();
    }
    this.conversations.update((list) => list.map((c) => (c.conversationId === conversationId ? { ...c, unreadCount: 0 } : c)));
  }

  protected onMessageSent(event: DmMessageSentEvent): void {
    if (event.conversationCreated) {
      // Yeni konuşma: öğretmen satırına Id yazılır, liste baştan çekilir. Panel aynı konuşmada kalır (yeniden yükler).
      this.teachers.update((list) =>
        list.map((t) => (t.teacherId === this.selection()?.teacherId ? { ...t, conversationId: event.conversationId } : t)),
      );
      this.selection.update((s) => (s ? { ...s, conversationId: event.conversationId, unreadCount: 0 } : s));
      this.conversationsPage.set(1);
      this.loadConversations();
      return;
    }
    const message = event.message;
    if (!message) {
      return;
    }
    this.conversations.update((list) => {
      const row = list.find((c) => c.conversationId === event.conversationId);
      if (!row) {
        return list;
      }
      const updated: ConversationSummary = {
        ...row,
        lastMessageAt: message.sentAt,
        lastMessagePreview: message.body.slice(0, PREVIEW_LENGTH),
        lastMessageIsMine: true,
      };
      return [updated, ...list.filter((c) => c.conversationId !== event.conversationId)];
    });
  }

  /** Liste yüklenince rozet tazelenir (polling yok; bkz. `DirectMessageService.unreadCount`). */
  private refreshBadge(): void {
    this.api
      .refreshUnreadCount('Student')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ error: () => {} });
  }

  private readonly t = (key: string, params?: Record<string, unknown>): string =>
    this.transloco.translate<string>(`${DIRECT_MESSAGES_SCOPE}.${key}`, params) ?? key;
}

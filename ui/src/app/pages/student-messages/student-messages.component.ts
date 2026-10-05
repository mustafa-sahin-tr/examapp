import { Component, DestroyRef, Injector, OnInit, afterNextRender, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleChange, MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, take } from 'rxjs';
import {
  ConversationSummary,
  DIRECT_MESSAGES_SCOPE,
  DIRECT_MESSAGE_PAGE_SIZE,
  TeacherInboxFilter,
} from '../../models/direct-message.model';
import { DirectMessageService } from '../../services/direct-message.service';
import {
  DmBlockedChangeEvent,
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

export interface TeacherDmSelection {
  conversationId: number;
  name: string;
  avatar: string;
  unreadCount: number | null;
}

const PREVIEW_LENGTH = 120;

/**
 * Issue #106 dilim (c) — öğretmenin öğrenci mesajları gelen kutusu (maket 9): split-view; sol liste
 * (Tümü / Okunmamış / Engellendi, okunmamış nokta), sağ konuşma paneli (cevap, Engelle / Engeli kaldır, Şikayet et).
 * < 900px'te panel tam ekran açılır. Öğretmen yeni konuşma başlatamaz (kapsam dışı).
 */
@Component({
  selector: 'app-student-messages',
  standalone: true,
  imports: [
    MatButtonModule,
    MatButtonToggleModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
    DmConversationComponent,
    DmConversationListComponent,
  ],
  providers: [
    provideTranslocoScope(DIRECT_MESSAGES_SCOPE),
    provideTranslatedPaginatorIntl(DIRECT_MESSAGES_SCOPE, `${DIRECT_MESSAGES_SCOPE}.paginator`),
  ],
  templateUrl: './student-messages.component.html',
  styleUrls: ['./student-messages.component.scss'],
})
export class StudentMessagesComponent implements OnInit {
  private readonly api = inject(DirectMessageService);
  private readonly transloco = inject(TranslocoService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  protected readonly pageSize = DIRECT_MESSAGE_PAGE_SIZE;
  protected readonly filters: readonly TeacherInboxFilter[] = ['all', 'unread', 'blocked'];

  protected readonly filter = signal<TeacherInboxFilter>('all');
  protected readonly items = signal<ConversationSummary[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly loaded = signal(false);
  protected readonly page = signal(1);
  protected readonly total = signal(0);
  protected readonly isEmpty = computed(() => this.loaded() && !this.loading() && !this.error() && this.items().length === 0);

  protected readonly selection = signal<TeacherDmSelection | null>(null);
  protected readonly selectedId = computed(() => this.selection()?.conversationId ?? null);

  private listSub?: Subscription;
  private lastTrigger: HTMLElement | null = null;

  constructor() {
    this.transloco.load(`${DIRECT_MESSAGES_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
    this.destroyRef.onDestroy(() => this.listSub?.unsubscribe());
  }

  ngOnInit(): void {
    this.load();
    // Derin link (dilim b bildirimleri için kanca): /student-messages?conversation=<id>
    const id = Number(this.route.snapshot.queryParamMap.get('conversation'));
    if (Number.isInteger(id) && id > 0) {
      this.selection.set({ conversationId: id, name: '', avatar: '', unreadCount: null });
    }
  }

  load(): void {
    this.listSub?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.listSub = this.api.getInbox(this.filter(), this.page(), this.pageSize).subscribe({
      next: (result) => {
        this.loading.set(false);
        this.loaded.set(true);
        this.items.set(result?.items ?? []);
        this.total.set(result?.totalCount ?? 0);
        // "Okunmamış" 1. sayfanın totalCount'u rozet sayısının ta kendisi: ikinci istek atılmaz.
        if (this.filter() === 'unread' && this.page() === 1) {
          this.api.setUnreadCount(this.total());
        } else {
          this.refreshBadge();
        }
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.items.set([]);
        this.error.set(directMessageError(err, this.t, 'teacher.error').message);
      },
    });
  }

  protected onFilter(event: MatButtonToggleChange): void {
    const value = event.value as TeacherInboxFilter;
    if (value === this.filter()) {
      return;
    }
    this.filter.set(value);
    this.page.set(1);
    this.load();
  }

  protected onPage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.load();
  }

  protected open(event: DmConversationSelectEvent): void {
    this.lastTrigger = event.trigger;
    const item = event.item;
    this.selection.set({
      conversationId: item.conversationId,
      name: item.counterpartName,
      avatar: item.counterpartAvatar,
      unreadCount: item.unreadCount,
    });
  }

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
    // "Okunmamış" filtresinde satır açıkken listeden düşmez; bir sonraki yüklemede çıkar.
    this.items.update((list) => list.map((c) => (c.conversationId === conversationId ? { ...c, unreadCount: 0 } : c)));
  }

  protected onBlockedChange(event: DmBlockedChangeEvent): void {
    this.items.update((list) =>
      list.map((c) => (c.conversationId === event.conversationId ? { ...c, isBlocked: event.isBlocked } : c)),
    );
  }

  protected onMessageSent(event: DmMessageSentEvent): void {
    const message = event.message;
    if (!message) {
      return;
    }
    this.items.update((list) => {
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

  /** Gelen kutusu yüklenince rozet tazelenir (polling yok). */
  private refreshBadge(): void {
    this.api
      .refreshUnreadCount('Teacher')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ error: () => {} });
  }

  private readonly t = (key: string, params?: Record<string, unknown>): string =>
    this.transloco.translate<string>(`${DIRECT_MESSAGES_SCOPE}.${key}`, params) ?? key;
}

import { isPlatformBrowser } from '@angular/common';
import { Component, DestroyRef, OnInit, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { finalize, interval } from 'rxjs';
import { AppNotification } from '../../models/notification.model';
import { AppRouteLink } from '../../models/worksheet-comment.model';
import { NotificationService } from '../../services/notification.service';
import { LocaleService } from '../../services/locale.service';
import { describeNotification, formatRelativeTime } from './notification-format';

export { BADGE_PROGRESS_ROUTE } from './notification-format';

export type NotificationFilter = 'unread' | 'all';

/** Göreli zaman metinlerinin sayfa açıkken tazelenme aralığı. */
export const RELATIVE_TIME_TICK_MS = 60_000;

export interface NotificationRow {
  notification: AppNotification;
  /** Rozet ikonunun URL'i; yoksa Material ikonu (`icon`) gösterilir. */
  iconUrl: string | null;
  icon: string;
  /** Tıklanınca gidilecek hedef; `null` = yalnızca okundu işaretlenir. */
  route: AppRouteLink | null;
  relativeTime: string;
}

const SCOPE = 'notifications';

/**
 * Issue #146 — kullanıcının kalıcı bildirim listesi. Başlık/gövde sunucuda yerelleştirilir.
 * Yalnızca bilinen türlerin `data`'sı yorumlanır (`BadgeEarned` → rozet sayfası; issue #105
 * `WorksheetCommentCreated`/`WorksheetCommentReplied` → worksheet-detail derin linki); diğerleri genel görünümdedir.
 */
@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [MatButtonModule, MatButtonToggleModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './notifications.component.html',
  styleUrls: ['./notifications.component.scss'],
})
export class NotificationsComponent implements OnInit {
  private readonly notificationService = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly localeService = inject(LocaleService);

  protected readonly loading = signal(false);
  protected readonly error = signal(false);
  protected readonly filter = signal<NotificationFilter>('all');
  protected readonly notifications = signal<AppNotification[]>([]);
  /** Göreli zaman ("3 dakika önce") için dakikalık tick. */
  private readonly now = signal(Date.now());

  protected readonly rows = computed<NotificationRow[]>(() => {
    const now = this.now();
    const locale = this.localeService.localeDefinition().angularLocale;
    return this.notifications().map((n) => this.toRow(n, now, locale));
  });

  protected readonly isEmpty = computed(() => !this.loading() && !this.error() && this.rows().length === 0);

  constructor() {
    // Sunucuda periyodik timer uygulamayı "stable" olmaktan alıkoyar — yalnızca tarayıcıda.
    if (isPlatformBrowser(inject(PLATFORM_ID))) {
      interval(RELATIVE_TIME_TICK_MS)
        .pipe(takeUntilDestroyed())
        .subscribe(() => this.now.set(Date.now()));
    }
  }

  ngOnInit(): void {
    this.load();
  }

  protected setFilter(value: NotificationFilter): void {
    if (value === this.filter()) {
      return;
    }
    this.filter.set(value);
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.notificationService
      .list(this.filter() === 'unread')
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (items) => this.notifications.set(items ?? []),
        error: () => {
          this.notifications.set([]);
          this.error.set(true);
        },
      });
  }

  /**
   * Okunmamışsa okundu işaretler; rozet bildiriminde rozet sayfasına gider.
   * `markRead` aboneliği bilinçli olarak komponent ömrüne bağlanmaz: navigasyon sayfayı yok etse bile istek
   * (async auth interceptor'dan geçip) tamamlanmalı ve sayaç düşmeli. Navigasyon isteğin sonucuna bağlıdır.
   */
  protected open(row: NotificationRow): void {
    const { notification, route } = row;
    const navigate = () => {
      if (!route) {
        return;
      }
      if (route.queryParams) {
        void this.router.navigate(route.commands, { queryParams: route.queryParams });
      } else {
        void this.router.navigate(route.commands);
      }
    };
    if (notification.isRead) {
      navigate();
      return;
    }
    // İyimser güncelleme: vurgu (ve serviste sayaç) hemen düşer; istek başarısızsa geri alınır.
    this.setRead(notification.id, true);
    this.notificationService
      .markRead(notification.id)
      .pipe(finalize(navigate))
      .subscribe({ error: () => this.setRead(notification.id, false) });
  }

  private setRead(id: number, isRead: boolean): void {
    this.notifications.update((items) => items.map((n) => (n.id === id ? { ...n, isRead } : n)));
  }

  private toRow(notification: AppNotification, now: number, locale: string): NotificationRow {
    const { icon, iconUrl, route } = describeNotification(notification);
    return { notification, iconUrl, icon, route, relativeTime: formatRelativeTime(notification.createdAt, now, locale) };
  }
}

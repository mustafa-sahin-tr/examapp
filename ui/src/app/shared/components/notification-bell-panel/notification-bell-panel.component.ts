import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { finalize } from 'rxjs';
import { AppNotification } from '../../../models/notification.model';
import { AuthService } from '../../../services/auth.service';
import { LocaleService } from '../../../services/locale.service';
import { NotificationService } from '../../../services/notification.service';
import { describeNotification, formatRelativeTime, navigateToNotification } from '../../../pages/notifications/notification-format';

/** Zil panelinde gösterilen son bildirim sayısı. */
export const BELL_PANEL_TAKE = 5;

/**
 * #914: zil sayfaya gitmek yerine son bildirimleri küçük bir panelde açar. Menü içeriği her açılışta yeniden kurulur,
 * bu yüzden liste her seferinde tazedir. Satıra basmak okundu işaretler ve hedefe gider; "Tümünü gör" sayfaya gider.
 */
@Component({
  selector: 'app-notification-bell-panel',
  standalone: true,
  imports: [MatIconModule, MatMenuModule, MatProgressSpinnerModule, RouterLink, TranslocoDirective],
  providers: [provideTranslocoScope('notifications')],
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./notification-bell-panel.component.scss'],
  template: `
    <div class="nbp" *transloco="let t; prefix: 'notifications'" (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()">
      <div class="nbp__head">{{ t('panel.title') }}</div>
      @if (loading()) {
        <div class="nbp__state"><mat-spinner diameter="24" /></div>
      } @else if (failed()) {
        <div class="nbp__state" role="alert">{{ t('panel.loadError') }}</div>
      } @else if (rows().length === 0) {
        <div class="nbp__state" data-testid="bell-empty">{{ t('panel.empty') }}</div>
      }
    </div>
    <ng-container *transloco="let t; prefix: 'notifications'">
      @for (row of rows(); track row.notification.id) {
        <button mat-menu-item type="button" class="nbp__item" [class.is-unread]="!row.notification.isRead" data-testid="bell-item" (click)="open(row.notification)">
          <mat-icon aria-hidden="true">{{ row.icon }}</mat-icon>
          <span class="nbp__text">
            <span class="nbp__title"><bdi dir="auto">{{ row.notification.title }}</bdi></span>
            @if (row.relativeTime) {
              <span class="nbp__time">{{ row.relativeTime }}</span>
            }
          </span>
          @if (!row.notification.isRead) {
            <span class="nbp__dot" aria-hidden="true"></span>
          }
        </button>
      }
      <a mat-menu-item class="nbp__all" routerLink="/notifications" data-testid="bell-see-all">{{ t('panel.seeAll') }}</a>
    </ng-container>
  `,
})
export class NotificationBellPanelComponent implements OnInit {
  private readonly service = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly localeService = inject(LocaleService);
  private readonly viewerRole = inject(AuthService).getUserRole();

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  private readonly notifications = signal<AppNotification[]>([]);

  protected readonly rows = computed(() => {
    const now = Date.now();
    const locale = this.localeService.localeDefinition().angularLocale;
    return this.notifications().map((notification) => {
      const { icon, badge } = describeNotification(notification, { role: this.viewerRole });
      return { notification, icon: badge ? badge.icon || 'military_tech' : icon, relativeTime: formatRelativeTime(notification.createdAt, now, locale) };
    });
  });

  ngOnInit(): void {
    this.service
      .list(false, BELL_PANEL_TAKE)
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({ next: (items) => this.notifications.set(items ?? []), error: () => this.failed.set(true) });
  }

  /** Sayfadaki gibi: okunmamışsa önce okundu işaretlenir, gidiş isteğin sonucunu bekler (menü kapansa da istek sürer). */
  protected open(notification: AppNotification): void {
    const { route } = describeNotification(notification, { role: this.viewerRole });
    if (notification.isRead) {
      navigateToNotification(this.router, route);
      return;
    }
    this.service
      .markRead(notification.id)
      .pipe(finalize(() => navigateToNotification(this.router, route)))
      .subscribe({ error: () => undefined });
  }
}

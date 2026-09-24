import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, filter, take } from 'rxjs';
import {
  BADGE_DEFINITION_DEFAULT_PAGE_SIZE,
  BADGE_DEFINITION_MAX_PAGE_SIZE,
  BadgeDefinitionAdmin,
} from '../../../models/badge-definition-admin.model';
import { BadgeDefinitionAdminService } from '../../../services/badge-definition-admin.service';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  BadgeDefinitionDialogComponent,
  BadgeDefinitionDialogData,
} from './badge-definition-dialog/badge-definition-dialog.component';
import { RuleSummary, ruleSummary, serverFieldErrors } from './badge-rule.util';
import { provideTranslatedPaginatorIntl } from '../../../shared/utils/paginator-intl.util';

const ADMIN_SCOPE = 'admin';

export type BadgeStatusFilter = 'active' | 'all';

/** Şablonun doğrudan bastığı satır modeli. */
export interface BadgeDefinitionRow {
  id: string;
  code: string;
  name: string;
  category: string;
  /** Kök-göreli ikon yolu; ikon yoksa null. */
  iconSrc: string | null;
  rule: RuleSummary;
  isActive: boolean;
  /** Son değiştiren (yoksa oluşturan). */
  changedBy: string | null;
  changedAtUtc: string;
  source: BadgeDefinitionAdmin;
}

/**
 * Issue #148 — rozet tanımları admin ekranı: aktif / tümü filtresi, oluştur / düzenle dialog'u,
 * aktifleştir / devre dışı bırak (silme yok; kazanılmış rozetler kalır).
 */
@Component({
  selector: 'app-badge-definitions',
  standalone: true,
  imports: [
    DatePipe,
    MatButtonModule,
    MatButtonToggleModule,
    MatDialogModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTableModule,
    MatTooltipModule,
    TranslocoDirective,
    TranslocoPipe,
  ],
  providers: [
    provideTranslocoScope(ADMIN_SCOPE),
    provideTranslatedPaginatorIntl(ADMIN_SCOPE, `${ADMIN_SCOPE}.paginator`),
  ],
  templateUrl: './badge-definitions.component.html',
  styleUrls: ['./badge-definitions.component.scss'],
})
export class BadgeDefinitionsComponent {
  private readonly service = inject(BadgeDefinitionAdminService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  readonly displayedColumns = ['icon', 'name', 'code', 'category', 'rule', 'status', 'updated', 'actions'];

  readonly statusFilter = signal<BadgeStatusFilter>('active');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly items = signal<BadgeDefinitionAdmin[]>([]);
  readonly totalCount = signal(0);
  /** 0-tabanlı (MatPaginator); backend'e `skip = pageIndex * pageSize` gider. */
  readonly pageIndex = signal(0);
  readonly pageSize = signal(BADGE_DEFINITION_DEFAULT_PAGE_SIZE);
  readonly pageSizeOptions = [25, BADGE_DEFINITION_DEFAULT_PAGE_SIZE, 100, BADGE_DEFINITION_MAX_PAGE_SIZE];
  /** Aktiflik isteği süren satırın id'si (çift tıklamaya karşı). */
  readonly busyId = signal<string | null>(null);
  readonly dialogOpen = signal(false);

  readonly rows = computed<BadgeDefinitionRow[]>(() => this.items().map(toRow));
  readonly isEmpty = computed(() => !this.loading() && !this.error() && this.items().length === 0);
  /** İlk yüklemede spinner; yenilemede tablo yerinde kalır, üstte ilerleme çubuğu. */
  readonly showSpinner = computed(() => this.loading() && this.items().length === 0);

  private loadSub: Subscription | null = null;

  constructor() {
    this.transloco.load(`${ADMIN_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
    inject(DestroyRef).onDestroy(() => this.loadSub?.unsubscribe());
    this.load();
  }

  load(): void {
    this.loadSub?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    const size = this.pageSize();
    this.loadSub = this.service.list(this.statusFilter() === 'all', this.pageIndex() * size, size).subscribe({
      next: (page) => {
        // Son sayfadaki tek satır pasifleşip filtreden düştüyse boş sayfada kalınmasın.
        if (page.items.length === 0 && page.totalCount > 0 && this.pageIndex() > 0) {
          this.pageIndex.set(Math.max(0, Math.ceil(page.totalCount / size) - 1));
          this.load();
          return;
        }
        this.items.set(page.items);
        this.totalCount.set(page.totalCount);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.items.set([]);
        this.totalCount.set(0);
        this.loading.set(false);
        this.error.set(this.text(err instanceof HttpErrorResponse && err.status === 403 ? 'errors.forbidden' : 'errors.loadFailed'));
      },
    });
  }

  onFilterChange(value: BadgeStatusFilter): void {
    if (value === this.statusFilter()) return;
    this.statusFilter.set(value);
    this.pageIndex.set(0);
    this.load();
  }

  onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  openCreate(): void {
    this.openDialog({ categories: this.categories() });
  }

  openEdit(row: BadgeDefinitionRow): void {
    this.openDialog({ definition: row.source, categories: this.categories() });
  }

  /** Aktif → onaylı devre dışı bırakma; pasif → doğrudan aktifleştirme. */
  toggleActive(row: BadgeDefinitionRow): void {
    if (this.busyId()) return;
    if (!row.isActive) {
      this.setActive(row, true);
      return;
    }
    const data: ConfirmDialogData = {
      title: this.text('deactivateDialog.title'),
      message: this.text('deactivateDialog.message', { name: row.name }),
      confirmText: this.text('deactivateDialog.confirm'),
      icon: 'block',
      confirmColor: 'warn',
    };
    this.dialog
      .open(ConfirmDialogComponent, { data })
      .afterClosed()
      .pipe(filter((ok) => ok === true))
      .subscribe(() => this.setActive(row, false));
  }

  private setActive(row: BadgeDefinitionRow, active: boolean): void {
    this.busyId.set(row.id);
    const request$ = active ? this.service.activate(row.id) : this.service.deactivate(row.id);
    request$.subscribe({
      next: (saved) => {
        this.busyId.set(null);
        this.upsert(saved);
        this.snack.open(this.text(active ? 'messages.activated' : 'messages.deactivated', { name: saved.name }), this.close, {
          duration: 3000,
        });
      },
      error: (err: unknown) => {
        this.busyId.set(null);
        const status = err instanceof HttpErrorResponse ? err.status : 0;
        // 409: aktif rozet üst sınırı (`errors.isActive`) — sunucu metni, yoksa çeviri.
        const serverMessage = status === 409 ? Object.values(serverFieldErrors(err) ?? {}).flat().join(' ') : '';
        const message =
          serverMessage ||
          this.text(status === 404 ? 'errors.notFound' : status === 409 ? 'errors.activeLimit' : 'errors.actionFailed');
        this.snack.open(message, this.close, { duration: 5000 });
        if (status === 404) this.load();
      },
    });
  }

  private openDialog(data: BadgeDefinitionDialogData): void {
    if (this.dialogOpen()) return;
    this.dialogOpen.set(true);
    const ref = this.dialog.open<BadgeDefinitionDialogComponent, BadgeDefinitionDialogData, BadgeDefinitionAdmin>(
      BadgeDefinitionDialogComponent,
      { data, width: '720px', maxWidth: '95vw', autoFocus: 'first-tabbable' }
    );
    ref.afterClosed().subscribe((saved) => {
      this.dialogOpen.set(false);
      if (!saved) return;
      this.upsert(saved);
      this.snack.open(this.text(data.definition ? 'messages.updated' : 'messages.created', { name: saved.name }), this.close, {
        duration: 3000,
      });
    });
  }

  /** Sunucunun döndürdüğü DTO'yu listeye yazar; "aktif" filtresinde pasifleşen satır listeden çıkar. */
  private upsert(saved: BadgeDefinitionAdmin): void {
    const hide = this.statusFilter() === 'active' && !saved.isActive;
    const exists = this.items().some((i) => i.id === saved.id);
    if (hide) {
      if (exists) {
        this.items.update((items) => items.filter((i) => i.id !== saved.id));
        this.totalCount.update((n) => Math.max(0, n - 1));
      }
      return;
    }
    if (exists) {
      this.items.update((items) => items.map((i) => (i.id === saved.id ? saved : i)));
    } else {
      this.items.update((items) => [saved, ...items]);
      this.totalCount.update((n) => n + 1);
    }
  }

  private categories(): string[] {
    return [...new Set(this.items().map((i) => i.category))];
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${ADMIN_SCOPE}.badgeDefinitions.${key}`, params) ?? '';
  }

  private get close(): string {
    return this.text('messages.close');
  }
}

function toRow(item: BadgeDefinitionAdmin): BadgeDefinitionRow {
  const icon = item.iconUrl?.trim();
  return {
    id: item.id,
    code: item.code,
    name: item.name,
    category: item.category,
    iconSrc: icon ? (/^(https?:)?\//.test(icon) ? icon : `/${icon}`) : null,
    rule: ruleSummary(item),
    isActive: item.isActive,
    // `*By` alanları Keycloak `sub`; görüntüleme adı varsa o gösterilir.
    changedBy: item.updatedAtUtc
      ? item.updatedByName ?? item.updatedBy ?? null
      : item.createdByName ?? item.createdBy ?? null,
    changedAtUtc: item.updatedAtUtc ?? item.createdAtUtc,
    source: item,
  };
}

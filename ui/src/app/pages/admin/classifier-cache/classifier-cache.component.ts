import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { firstValueFrom, take } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { ClassifierCacheStatus } from '../../../models/taxonomy';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/components/confirm-dialog/confirm-dialog.component';

/** Yönetim ekranlarının ortak Transloco scope'u: `public/i18n/admin/<lang>.json` (issue #183). */
const ADMIN_SCOPE = 'admin';

@Component({
  selector: 'app-classifier-cache',
  standalone: true,
  templateUrl: './classifier-cache.component.html',
  styleUrls: ['./classifier-cache.component.scss'],
  imports: [
    CommonModule,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressBarModule,
    MatDialogModule,
    MatSnackBarModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(ADMIN_SCOPE)],
})
export class ClassifierCacheComponent implements OnInit {
  private readonly admin = inject(AdminService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  readonly loading = signal(false);
  readonly refreshing = signal(false);
  readonly status = signal<ClassifierCacheStatus | null>(null);

  constructor() {
    this.preloadScope();
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.admin.getClassifierCache().subscribe({
      next: (s) => {
        this.status.set(s);
        this.loading.set(false);
      },
      error: () => {
        this.snack.open(this.text('statusFailed'), this.text('close'), { duration: 4000 });
        this.loading.set(false);
      },
    });
  }

  async refresh(): Promise<void> {
    const data: ConfirmDialogData = {
      title: this.text('confirm.title'),
      message: this.text('confirm.message'),
      confirmText: this.text('confirm.confirmText'),
      icon: 'refresh',
      confirmColor: 'primary',
    };
    const ok = await firstValueFrom(this.dialog.open(ConfirmDialogComponent, { data }).afterClosed());
    if (!ok) return;

    this.refreshing.set(true);
    try {
      const res = await firstValueFrom(this.admin.refreshClassifierCache());
      this.snack.open(res.message, this.text('close'), { duration: 4000 });
      this.load();
    } catch (err: unknown) {
      const message = (err as { error?: { message?: string } } | null)?.error?.message;
      this.snack.open(message ?? this.text('refreshFailed'), this.text('close'), { duration: 5000 });
    } finally {
      this.refreshing.set(false);
    }
  }

  /** Scope'a göreli anahtarı senkron çözer; sözlük şablon render edilirken yüklenmiş olur. */
  private text(key: string): string {
    return this.transloco.translate<string>(`${ADMIN_SCOPE}.classifierCache.${key}`) ?? '';
  }

  /**
   * Şablon dışı metinler (snackbar, dialog, hata mesajı) senkron `translate()` ile okunur;
   * sözlük şablon render edilmeden de hazır olsun diye scope burada yüklenir.
   */
  private preloadScope(): void {
    this.transloco
      .load(`${ADMIN_SCOPE}/${this.transloco.getActiveLang()}`)
      .pipe(take(1))
      .subscribe();
  }

}

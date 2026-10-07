import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoService } from '@jsverse/transloco';
import { Observable, map } from 'rxjs';

import { LocaleService } from '../../services/locale.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../components/confirm-dialog/confirm-dialog.component';

/** Veli bağlantısı ekranlarının Transloco scope'u (`public/i18n/parent-links/<lang>.json`). */
export const PARENT_LINKS_SCOPE = 'parent-links';

/**
 * Issue #419: öğrenci "Veli davet kodu" kartı ile velinin "Çocuklarım" sayfasının ortak UI yardımcıları — scope'lu metin,
 * dile bağlı tarih biçimi, snackbar ve koparma/iptal onay diyaloğu (iki ekranda tekrarlanmasın).
 */
@Injectable({ providedIn: 'root' })
export class ParentLinkUi {
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  /** `parent-links` scope'una göreli anahtar. */
  text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${PARENT_LINKS_SCOPE}.${key}`, params) ?? '';
  }

  /** Aktif dilde tarih (isteğe bağlı saat); geçersiz/boş girdide boş metin. */
  formatDate(iso: string | null | undefined, withTime = false): string {
    if (!iso) return '';
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return '';
    return new Intl.DateTimeFormat(this.localeService.localeDefinition().angularLocale, {
      day: 'numeric',
      month: 'long',
      year: 'numeric',
      ...(withTime ? { hour: '2-digit', minute: '2-digit' } : {}),
    }).format(date);
  }

  toast(message: string): void {
    this.snackBar.open(message, this.text('close'), { duration: 4000 });
  }

  /**
   * Onay diyaloğu: `<prefix>.title|message|confirm` anahtarları (ör. `invite.revokeConfirm`). Kullanıcı onaylarsa true.
   */
  confirm(prefix: string, params?: Record<string, unknown>): Observable<boolean> {
    const data: ConfirmDialogData = {
      title: this.text(`${prefix}.title`),
      message: this.text(`${prefix}.message`, params),
      confirmText: this.text(`${prefix}.confirm`),
      icon: 'link_off',
      confirmColor: 'warn',
    };
    return this.dialog
      .open(ConfirmDialogComponent, { data })
      .afterClosed()
      .pipe(map((ok) => !!ok));
  }
}

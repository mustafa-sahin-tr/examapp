import { Component, DestroyRef, effect, inject, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';

import { PARENT_REQUIRED_URL } from '../../../models/parent-status.model';
import { ParentStatusStore } from '../../../services/parent-status.store';
import {
  ParentFinalWarningDialogComponent,
  ParentFinalWarningDialogData,
} from './parent-final-warning-dialog.component';

/**
 * Issue #441: hoşgörü süresinin son günlerinde (`Grace` + `isFinalWarning`) her sayfada kapatılamaz modal açar; "veli bilgisi
 * ekle" ekranında açmaz (orada form doldurulur). Durum değişince (form gönderildi → `AwaitingActivation`) modal kapanır. Layout'a
 * bir kez yerleştirilir; görünür bir şey çizmez.
 */
@Component({
  selector: 'app-parent-final-warning',
  standalone: true,
  template: '',
})
export class ParentFinalWarningComponent {
  private readonly store = inject(ParentStatusStore);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);

  private readonly path = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects)
    ),
    { initialValue: this.router.url }
  );

  private dialogRef: MatDialogRef<ParentFinalWarningDialogComponent> | null = null;

  constructor() {
    effect(() => {
      const show = this.store.finalWarning() && this.path().split(/[?#]/)[0] !== PARENT_REQUIRED_URL;
      const daysLeft = this.store.daysLeft() ?? 0;
      untracked(() => (show ? this.open(daysLeft) : this.close()));
    });
    inject(DestroyRef).onDestroy(() => this.close());
  }

  private open(daysLeft: number): void {
    if (this.dialogRef) {
      return;
    }
    const data: ParentFinalWarningDialogData = { daysLeft };
    this.dialogRef = this.dialog.open(ParentFinalWarningDialogComponent, {
      data,
      disableClose: true,
      autoFocus: 'first-tabbable',
      maxWidth: '480px',
    });
    this.dialogRef.afterClosed().subscribe(() => (this.dialogRef = null));
  }

  private close(): void {
    this.dialogRef?.close();
  }
}

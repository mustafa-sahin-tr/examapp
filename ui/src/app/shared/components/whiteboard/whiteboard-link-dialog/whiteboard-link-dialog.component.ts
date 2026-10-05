import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { take } from 'rxjs';

const SCOPE = 'whiteboard';

/** Dialog içeriğinin id'si — `aria-describedby` için (tek dialog açık olabildiğinden sabit). */
export const WHITEBOARD_LINK_DIALOG_DESCRIPTION_ID = 'wb-link-dialog-description';

export interface WhiteboardLinkDialogData {
  /** Normalize edilmiş tam adres (yalnızca http(s); dış origin ya da aynı origin'de hassas yol). */
  readonly url: string;
  /** Hedef host — öne çıkarılır (adres çubuğundaki gibi). */
  readonly host: string;
}

/**
 * Issue #332 — tahtadaki dış bağlantı açılmadan önce onay. Hedef adres tam ve seçilebilir metin olarak gösterilir;
 * varsayılan odak "Vazgeç"tedir (Enter yanlışlıkla açmasın). ESC/backdrop = vazgeç. `true` → aç.
 * `role="alertdialog"` + `aria-describedby` (içerik): ekran okuyucu uyarıyı ve adresi birlikte okur.
 */
@Component({
  selector: 'app-whiteboard-link-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, TranslocoDirective],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './whiteboard-link-dialog.component.html',
  styleUrl: './whiteboard-link-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WhiteboardLinkDialogComponent {
  protected readonly data = inject<WhiteboardLinkDialogData>(MAT_DIALOG_DATA);
  protected readonly descriptionId = WHITEBOARD_LINK_DIALOG_DESCRIPTION_ID;
  private readonly dialogRef = inject<MatDialogRef<WhiteboardLinkDialogComponent, boolean>>(MatDialogRef);

  constructor() {
    const transloco = inject(TranslocoService);
    transloco.load(`${SCOPE}/${transloco.getActiveLang()}`).pipe(take(1)).subscribe();
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }

  protected proceed(): void {
    this.dialogRef.close(true);
  }
}

export function openWhiteboardLinkDialog(
  dialog: MatDialog,
  data: WhiteboardLinkDialogData
): MatDialogRef<WhiteboardLinkDialogComponent, boolean> {
  return dialog.open<WhiteboardLinkDialogComponent, WhiteboardLinkDialogData, boolean>(WhiteboardLinkDialogComponent, {
    data,
    role: 'alertdialog',
    ariaDescribedBy: WHITEBOARD_LINK_DIALOG_DESCRIPTION_ID,
    autoFocus: '[data-autofocus]',
    restoreFocus: true,
    width: '480px',
    maxWidth: '94vw',
  });
}

import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, UrlTree } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';

/**
 * #877: kapıdan geri çevrilen kişi ana sayfaya sessizce düşmesin; nedenini kısa bir bildirimle söyle.
 * `messageKey` kök sözlükte (`common.routeDenied.*`).
 */
export function deniedRedirect(messageKey: string): UrlTree {
  inject(MatSnackBar).open(inject(TranslocoService).translate(messageKey), undefined, { duration: 5000 });
  return inject(Router).createUrlTree(['/dashboard']);
}

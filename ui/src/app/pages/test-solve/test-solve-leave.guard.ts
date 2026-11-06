import { CanDeactivateFn } from '@angular/router';

/** Issue #821: çözüm ekranından ayrılmadan önce bileşene sorar (onay penceresi + bekleyen cevapların kaydı). */
export interface LeaveConfirmable {
  canLeave(): boolean | Promise<boolean>;
}

export const testSolveLeaveGuard: CanDeactivateFn<LeaveConfirmable> = (component) => component?.canLeave() ?? true;

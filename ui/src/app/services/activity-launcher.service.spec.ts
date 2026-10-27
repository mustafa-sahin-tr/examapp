import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ActivityLauncherService } from './activity-launcher.service';
import { ActivityService } from './activity.service';
import { TestService } from './test.service';
import { translocoTestingModule } from '../shared/testing/transloco-testing';

describe('ActivityLauncherService (#573)', () => {
  let service: ActivityLauncherService;
  const testService = { startTest: vi.fn() };
  const router = { navigate: vi.fn().mockResolvedValue(true) };
  const snackBar = { open: vi.fn() };
  const activityService = { retake: vi.fn() };
  let confirmResult: boolean | undefined = true;
  const dialog = { open: vi.fn(() => ({ afterClosed: () => of(confirmResult) })) };

  beforeEach(() => {
    vi.clearAllMocks();
    confirmResult = true;
    TestBed.configureTestingModule({
      imports: [translocoTestingModule()],
      providers: [
        { provide: TestService, useValue: testService },
        { provide: Router, useValue: router },
        { provide: MatSnackBar, useValue: snackBar },
        { provide: ActivityService, useValue: activityService },
        { provide: MatDialog, useValue: dialog },
      ],
    });
    service = TestBed.inject(ActivityLauncherService);
  });

  it('open_Started_NavigatesToPlayer', () => {
    testService.startTest.mockReturnValue(of({ success: true, instanceId: 55 }));

    service.open(9);

    expect(testService.startTest).toHaveBeenCalledWith(9);
    expect(router.navigate).toHaveBeenCalledWith(['/activity', 55], { state: { worksheetId: 9 } });
    expect(service.launching()).toBeNull();
  });

  it('open_NotStarted_ShowsServerMessageAndOpensDetail', () => {
    testService.startTest.mockReturnValue(of({ success: false, instanceId: 3, message: 'Zaten tamamlandı.' }));

    service.open(9);

    expect(snackBar.open).toHaveBeenCalledWith('Zaten tamamlandı.', 'Tamam', expect.any(Object));
    expect(router.navigate).toHaveBeenCalledWith(['/test', 9]);
  });

  it('open_HttpError_ShowsFallbackMessage', () => {
    testService.startTest.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    service.open(9);

    expect(snackBar.open).toHaveBeenCalledWith('Etkinlik açılamadı. Tekrar dene.', 'Tamam', expect.any(Object));
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('open_WhileLaunching_IgnoresSecondCall', () => {
    testService.startTest.mockReturnValue(of());
    service.launching.set(1);

    service.open(9);

    expect(testService.startTest).not.toHaveBeenCalled();
  });

  describe('retake (#609)', () => {
    it('confirmed_ResetsSessionAndOpensPlayer', () => {
      activityService.retake.mockReturnValue(of({ success: true, instanceId: 55, alreadyRetaking: false }));

      service.retake(55, 9);

      const data = (dialog.open.mock.calls[0] as unknown[])[1] as { data: { message: string; confirmText: string } };
      expect(data.data.message).toContain('Cevapların silinecek');
      expect(data.data.confirmText).toBe('Yeniden çöz');
      expect(activityService.retake).toHaveBeenCalledWith(55);
      expect(router.navigate).toHaveBeenCalledWith(['/activity', 55], { state: { worksheetId: 9 } });
      expect(snackBar.open).not.toHaveBeenCalled();
      expect(service.retaking()).toBeNull();
    });

    it('cancelled_DoesNotCallServer', () => {
      confirmResult = false;

      service.retake(55, 9);

      expect(activityService.retake).not.toHaveBeenCalled();
      expect(router.navigate).not.toHaveBeenCalled();
      expect(service.retaking()).toBeNull();
    });

    it('alreadyRetaking_StillOpensPlayer', () => {
      activityService.retake.mockReturnValue(of({ success: true, instanceId: 55, alreadyRetaking: true }));

      service.retake(55, 9);

      expect(router.navigate).toHaveBeenCalledWith(['/activity', 55], { state: { worksheetId: 9 } });
    });

    it('reopenCallback_UsedInsteadOfNavigation', () => {
      activityService.retake.mockReturnValue(of({ success: true, instanceId: 55, alreadyRetaking: false }));
      const reopen = vi.fn();

      service.retake(55, 9, reopen);

      expect(reopen).toHaveBeenCalledTimes(1);
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('conflict_ShowsServerMessage', () => {
      activityService.retake.mockReturnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 409,
              error: { success: false, errorCode: 'ActivityNotCompleted', message: 'Yalnız tamamlanmış bir etkinlik yeniden çözülebilir.' },
            })
        )
      );

      service.retake(55, 9);

      expect(snackBar.open).toHaveBeenCalledWith('Yalnız tamamlanmış bir etkinlik yeniden çözülebilir.', 'Tamam', expect.any(Object));
      expect(router.navigate).not.toHaveBeenCalled();
      expect(service.retaking()).toBeNull();
    });

    it.each([
      [403, 'AccessDenied', 'Bu etkinliğe erişimin yok.'],
      [404, 'WorksheetNotFound', 'Etkinlik bulunamadı.'],
      [429, 'RateLimited', 'Kısa sürede çok fazla deneme yaptın.'],
    ])('httpError_%s_%s_ShowsServerMessage', (status, errorCode, message) => {
      activityService.retake.mockReturnValue(
        throwError(() => new HttpErrorResponse({ status, error: { success: false, errorCode, message, forbidden: status === 403 } }))
      );

      service.retake(55, 9);

      expect(snackBar.open).toHaveBeenCalledWith(message, 'Tamam', expect.any(Object));
      expect(router.navigate).not.toHaveBeenCalled();
      expect(service.retaking()).toBeNull();
    });

    it('rateLimitedWithoutMessage_ShowsLocalRateLimitText', () => {
      activityService.retake.mockReturnValue(
        throwError(() => new HttpErrorResponse({ status: 429, error: { errorCode: 'RateLimited' } }))
      );

      service.retake(55, 9);

      expect(snackBar.open).toHaveBeenCalledWith('Çok sık denedin, biraz bekleyip tekrar dene.', 'Tamam', expect.any(Object));
    });

    it('httpErrorWithoutMessage_ShowsFallback', () => {
      activityService.retake.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

      service.retake(55, 9);

      expect(snackBar.open).toHaveBeenCalledWith('Etkinlik yeniden açılamadı. Tekrar dene.', 'Tamam', expect.any(Object));
    });

    it('whileRetaking_IgnoresSecondCall', () => {
      service.retaking.set(55);

      service.retake(55, 9);

      expect(dialog.open).not.toHaveBeenCalled();
    });
  });
});

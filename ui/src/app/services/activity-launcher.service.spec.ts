import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ActivityLauncherService } from './activity-launcher.service';
import { TestService } from './test.service';
import { translocoTestingModule } from '../shared/testing/transloco-testing';

describe('ActivityLauncherService (#573)', () => {
  let service: ActivityLauncherService;
  const testService = { startTest: vi.fn() };
  const router = { navigate: vi.fn().mockResolvedValue(true) };
  const snackBar = { open: vi.fn() };

  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({
      imports: [translocoTestingModule()],
      providers: [
        { provide: TestService, useValue: testService },
        { provide: Router, useValue: router },
        { provide: MatSnackBar, useValue: snackBar },
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
});

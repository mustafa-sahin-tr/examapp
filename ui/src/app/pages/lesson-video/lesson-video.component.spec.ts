import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { throwError } from 'rxjs';

import { LessonVideoComponent } from './lesson-video.component';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import { AuthService } from '../../services/auth.service';
import { BookingService } from '../../services/booking.service';
import { JitsiScriptLoaderService } from '../../services/jitsi-script-loader.service';
import lessonVideoTr from '../../../../public/i18n/lesson-video/tr.json';

/** Gerçek sözlük yüklenir; anahtar bozulursa test kırılır (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { 'lesson-video/tr': lessonVideoTr },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
    scopes: { keepCasing: true },
  },
  preloadLangs: true,
});

/** Issue #298: öğretmen askıdayken video-session 409 `TeacherUnavailable`. */
describe('LessonVideoComponent (issue #298 teacher unavailable)', () => {
  let fixture: ComponentFixture<LessonVideoComponent>;
  let bookingService: jasmine.SpyObj<BookingService>;

  const conflict = (body: unknown) => new HttpErrorResponse({ status: 409, error: body });

  async function render(error: HttpErrorResponse): Promise<HTMLElement> {
    bookingService.getVideoSession.and.returnValue(throwError(() => error));
    fixture = TestBed.createComponent(LessonVideoComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function buttonLabels(host: HTMLElement): string[] {
    return Array.from(host.querySelectorAll('.lvid__state-actions button')).map((b) => b.textContent?.trim() ?? '');
  }

  beforeEach(async () => {
    bookingService = jasmine.createSpyObj<BookingService>('BookingService', ['getVideoSession', 'extractError']);
    bookingService.extractError.and.callFake((err: HttpErrorResponse, fallback: string) => {
      const message = (err.error as { message?: string } | null)?.message;
      return message || fallback;
    });

    await TestBed.configureTestingModule({
      imports: [LessonVideoComponent, translocoTesting],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ bookingId: '42' }) } } },
        { provide: BookingService, useValue: bookingService },
        { provide: JitsiScriptLoaderService, useValue: { load: () => Promise.reject(new Error('unused')) } },
        { provide: AuthService, useValue: { getUserRole: () => 'Student' } },
      ],
    }).compileComponents();
  });

  it('teacherUnavailable_WithServerMessage_ShowsServerMessageWithoutRetry', async () => {
    const host = await render(
      conflict({ success: false, conflict: true, errorCode: 'TeacherUnavailable', message: 'Sunucu mesajı' })
    );

    const alert = host.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Sunucu mesajı');
    expect(alert?.querySelector('mat-icon')?.textContent?.trim()).toBe('event_busy');
    expect(buttonLabels(host)).toEqual([lessonVideoTr.goBack]);
    expect(bookingService.getVideoSession).toHaveBeenCalledTimes(1);
  });

  it('teacherUnavailable_WithoutMessage_FallsBackToLocalTranslation', async () => {
    const host = await render(conflict({ success: false, conflict: true, errorCode: 'TeacherUnavailable', message: '  ' }));

    expect(host.querySelector('[role="alert"]')?.textContent).toContain(lessonVideoTr.error.teacherUnavailable);
    expect(buttonLabels(host)).toEqual([lessonVideoTr.goBack]);
    expect(bookingService.getVideoSession).toHaveBeenCalledTimes(1);
  });

  it('otherConflict_KeepsRetryButton', async () => {
    const host = await render(conflict({ success: false, conflict: true, message: 'Katılım penceresi dışında' }));

    const alert = host.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Katılım penceresi dışında');
    expect(alert?.querySelector('mat-icon')?.textContent?.trim()).toBe('videocam_off');
    expect(buttonLabels(host)).toEqual([lessonVideoTr.retry, lessonVideoTr.goBack]);
  });

  it('teacherUnavailableCodeOnNon409_TreatedAsGenericError', async () => {
    const host = await render(
      new HttpErrorResponse({ status: 403, error: { success: false, errorCode: 'TeacherUnavailable', message: 'Yasak' } })
    );

    expect(buttonLabels(host)).toEqual([lessonVideoTr.retry, lessonVideoTr.goBack]);
  });
});

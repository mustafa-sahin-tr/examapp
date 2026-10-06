import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';

import { StudentSchoolRequestsComponent } from './student-school-requests.component';
import { StudentSchoolRequestService } from '../../services/student-school-request.service';
import { LocaleService } from '../../services/locale.service';
import { StudentSchoolRequest, StudentSchoolRequestPage } from '../../models/student-school-request.model';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import scopeTr from '../../../../public/i18n/student-school-requests/tr.json';
import scopeEn from '../../../../public/i18n/student-school-requests/en.json';

function row(overrides: Partial<StudentSchoolRequest> = {}): StudentSchoolRequest {
  return {
    studentId: 7,
    fullName: 'Ayşe Öğrenci',
    studentNumber: '***1234',
    gradeId: 3,
    gradeName: '8. Sınıf',
    schoolId: 5,
    schoolName: 'Atatürk Ortaokulu',
    registeredAt: '2026-10-01T08:00:00Z',
    ...overrides,
  };
}

function page(items: StudentSchoolRequest[], totalCount = items.length): StudentSchoolRequestPage {
  return { pageNumber: 1, pageSize: 20, totalCount, items };
}

describe('StudentSchoolRequestsComponent (issue #361)', () => {
  let service: jasmine.SpyObj<StudentSchoolRequestService>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;
  let dialog: jasmine.SpyObj<MatDialog>;

  function create(items: StudentSchoolRequest[] = [row()]) {
    service = jasmine.createSpyObj('StudentSchoolRequestService', ['list', 'approve', 'reject', 'refreshPendingCount']);
    service.list.and.returnValue(of(page(items)));
    snackBar = jasmine.createSpyObj('MatSnackBar', ['open']);
    dialog = jasmine.createSpyObj('MatDialog', ['open']);

    TestBed.configureTestingModule({
      imports: [
        StudentSchoolRequestsComponent,
        translocoTestingModule({
          langs: { 'student-school-requests/tr': scopeTr, 'student-school-requests/en': scopeEn },
        }),
      ],
      providers: [
        { provide: StudentSchoolRequestService, useValue: service },
        { provide: MatSnackBar, useValue: snackBar },
        { provide: MatDialog, useValue: dialog },
        { provide: LocaleService, useValue: { localeDefinition: () => ({ angularLocale: 'tr' }) } },
        provideNoopAnimations(),
      ],
    });

    const fixture = TestBed.createComponent(StudentSchoolRequestsComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page and renders pending students with Turkish texts', () => {
    const fixture = create();
    const text = fixture.nativeElement.textContent as string;

    expect(service.list).toHaveBeenCalledWith(1, 20);
    expect(text).toContain('Bekleyen öğrenci başvuruları');
    expect(text).toContain('Ayşe Öğrenci');
    expect(text).toContain('***1234');
    expect(text).toContain('Atatürk Ortaokulu');
    expect(fixture.debugElement.queryAll(By.css('.ssr__approve')).length).toBe(1);
  });

  it('shows the empty state when there are no pending requests', () => {
    const fixture = create([]);
    expect(fixture.nativeElement.textContent).toContain('Bekleyen öğrenci başvurusu yok.');
    expect(fixture.debugElement.query(By.css('table'))).toBeNull();
  });

  it('approve calls the API, shows success and reloads', () => {
    const fixture = create();
    service.approve.and.returnValue(of({ message: 'ok' }));

    fixture.debugElement.query(By.css('.ssr__approve')).nativeElement.click();

    expect(service.approve).toHaveBeenCalledWith(7);
    expect(snackBar.open).toHaveBeenCalledWith('Öğrencinin okul üyeliği onaylandı.', 'Tamam', jasmine.any(Object));
    expect(service.list).toHaveBeenCalledTimes(2);
  });

  it('reject asks for confirmation and only rejects when confirmed', () => {
    const fixture = create();
    service.reject.and.returnValue(of({}));
    dialog.open.and.returnValue({ afterClosed: () => of(false) } as never);

    fixture.debugElement.query(By.css('.ssr__reject')).nativeElement.click();
    expect(service.reject).not.toHaveBeenCalled();

    dialog.open.and.returnValue({ afterClosed: () => of(true) } as never);
    fixture.debugElement.query(By.css('.ssr__reject')).nativeElement.click();

    expect(service.reject).toHaveBeenCalledWith(7);
    const data = dialog.open.calls.mostRecent().args[1]?.data as { message: string };
    expect(data.message).toContain('Ayşe Öğrenci');
    expect(data.message).toContain('Atatürk Ortaokulu');
  });

  it('a 404 from the server (request no longer visible) shows the server message and reloads the list', () => {
    const fixture = create();
    service.approve.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 404, error: { message: 'Bekleyen okul başvurusu bulunamadı.' } }))
    );

    fixture.debugElement.query(By.css('.ssr__approve')).nativeElement.click();

    expect(snackBar.open).toHaveBeenCalledWith('Bekleyen okul başvurusu bulunamadı.', 'Tamam', jasmine.any(Object));
    expect(service.list).toHaveBeenCalledTimes(2);
  });

  it('load error shows the error state with retry', () => {
    const fixture = create();
    service.list.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    (fixture.componentInstance as unknown as { load(): void }).load();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Başvurular yüklenemedi.');
  });

  it('TR and EN scope dictionaries have the same keys', () => {
    const keys = (o: object, prefix = ''): string[] =>
      Object.entries(o).flatMap(([k, v]) =>
        v && typeof v === 'object' ? keys(v as object, `${prefix}${k}.`) : [`${prefix}${k}`]
      );
    expect(keys(scopeEn).sort()).toEqual(keys(scopeTr).sort());
  });
});

import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNativeDateAdapter } from '@angular/material/core';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';

import {
  WorksheetAssignmentDialogComponent,
  WorksheetAssignmentDialogData,
} from './worksheet-assignment-dialog.component';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../../../models/locale';
import rootTr from '../../../../../../public/i18n/tr.json';
import worksheetDetailTr from '../../../../../../public/i18n/worksheet-detail/tr.json';

const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { tr: { ...rootTr, 'worksheet-detail': worksheetDetailTr }, 'worksheet-detail/tr': worksheetDetailTr },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
    scopes: { keepCasing: true },
  },
  preloadLangs: true,
});

/** Issue #222: bağımsız öğretmen sınıf bazlı atama yapamaz. */
describe('WorksheetAssignmentDialogComponent', () => {
  let dialogRef: jasmine.SpyObj<MatDialogRef<WorksheetAssignmentDialogComponent>>;

  function create(overrides: Partial<WorksheetAssignmentDialogData> = {}) {
    const data: WorksheetAssignmentDialogData = {
      worksheetId: 7,
      scope: 'grade',
      grades: [{ id: 1, name: '5-A' }],
      students: [{ id: 11, userId: 21, studentNumber: '101', fullName: 'Ada', schoolName: 'Okul', gradeId: 1 }],
      ...overrides,
    };

    dialogRef = jasmine.createSpyObj('MatDialogRef', ['close']);
    TestBed.configureTestingModule({
      imports: [WorksheetAssignmentDialogComponent, translocoTesting, NoopAnimationsModule],
      providers: [
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    });

    const fixture = TestBed.createComponent(WorksheetAssignmentDialogComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('okullu öğretmende sınıf ve öğrenci seçeneklerini gösterir, varsayılan scope grade', () => {
    const fixture = create();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('[data-testid="scope-grade"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="scope-student"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="independent-tutor-note"]')).toBeNull();
    expect(fixture.componentInstance['form'].controls.scope.value).toBe('grade');
  });

  it('bağımsız öğretmende "Sınıfa ata" seçeneğini render etmez ve scope student olur', () => {
    const fixture = create({ isIndependentTutor: true, scope: 'grade' });
    const el: HTMLElement = fixture.nativeElement;
    const form = fixture.componentInstance['form'];

    expect(el.querySelector('[data-testid="scope-grade"]')).toBeNull();
    expect(el.querySelector('mat-radio-group')).toBeNull();
    expect(el.querySelector('[data-testid="independent-tutor-note"]')).not.toBeNull();
    expect(form.controls.scope.value).toBe('student');
    expect(form.controls.gradeId.value).toBeNull();
  });

  it('bağımsız öğretmen yalnızca öğrenci bazlı istek üretir', () => {
    const fixture = create({ isIndependentTutor: true });
    const component = fixture.componentInstance;
    component['form'].controls.studentId.setValue(11);

    component['submit']();

    const result = dialogRef.close.calls.mostRecent().args[0] as { request: { gradeId?: number; studentId?: number } };
    expect(result.request.studentId).toBe(11);
    expect(result.request.gradeId).toBeUndefined();
  });

  it('öğrenci listesi boşsa boş durum metnini gösterir', () => {
    const fixture = create({ isIndependentTutor: true, students: [] });
    expect(fixture.nativeElement.querySelector('[data-testid="no-students"]')).not.toBeNull();
  });
});

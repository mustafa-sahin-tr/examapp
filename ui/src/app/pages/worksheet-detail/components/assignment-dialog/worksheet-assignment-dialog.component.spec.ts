import { WritableSignal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNativeDateAdapter } from '@angular/material/core';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';

import {
  StudentLookupStatus,
  WorksheetAssignmentDialogComponent,
  WorksheetAssignmentDialogData,
} from './worksheet-assignment-dialog.component';
import { StudentLookup } from '../../../../models/student';
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

  const ada: StudentLookup = { id: 11, userId: 21, studentNumber: '101', fullName: 'Ada', schoolName: 'Okul', gradeId: 1 };
  let studentsSig: WritableSignal<StudentLookup[]>;
  let statusSig: WritableSignal<StudentLookupStatus>;

  type CreateOptions = Partial<Omit<WorksheetAssignmentDialogData, 'students' | 'studentsStatus'>> & {
    students?: StudentLookup[];
    studentsStatus?: StudentLookupStatus;
  };

  function create(options: CreateOptions = {}) {
    const { students = [ada], studentsStatus = 'loaded', ...overrides } = options;
    studentsSig = signal(students);
    statusSig = signal(studentsStatus);
    const data: WorksheetAssignmentDialogData = {
      worksheetId: 7,
      scope: 'grade',
      grades: [{ id: 1, name: '5-A' }],
      students: studentsSig.asReadonly(),
      studentsStatus: statusSig.asReadonly(),
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

  describe('boş öğrenci listesi (issue #223)', () => {
    const q = (fixture: { nativeElement: HTMLElement }, id: string) =>
      fixture.nativeElement.querySelector(`[data-testid="${id}"]`) as HTMLElement | null;

    it('bağımsız öğretmende onaylı ders metnini gösterir, genel metni göstermez', () => {
      const fixture = create({ isIndependentTutor: true, students: [] });

      const independent = q(fixture, 'no-students-independent');
      expect(independent).not.toBeNull();
      expect(independent!.textContent).toContain(
        'Henüz onaylı dersi olan öğrencin yok; randevu onaylanınca burada görünür.'
      );
      expect(q(fixture, 'no-students')).toBeNull();
      expect(fixture.nativeElement.textContent).not.toContain('önce sınıf seçerek');
    });

    it('okullu öğretmende genel metni gösterir, bağımsız metnini göstermez', () => {
      const fixture = create({ scope: 'student', students: [] });

      expect(q(fixture, 'no-students')).not.toBeNull();
      expect(q(fixture, 'no-students-independent')).toBeNull();
    });

    it('bağımsız öğretmende liste doluysa boş durum göstermez', () => {
      const fixture = create({ isIndependentTutor: true });

      expect(q(fixture, 'no-students-independent')).toBeNull();
      expect(q(fixture, 'no-students')).toBeNull();
    });

    it('liste yüklenirken boş durum yerine yükleniyor metnini gösterir', () => {
      const fixture = create({ isIndependentTutor: true, students: [], studentsStatus: 'loading' });

      expect(q(fixture, 'students-loading')).not.toBeNull();
      expect(q(fixture, 'no-students-independent')).toBeNull();
      expect(q(fixture, 'no-students')).toBeNull();
    });

    it('lookup hata verdiyse boş durum yerine hata metnini gösterir', () => {
      const fixture = create({ isIndependentTutor: true, students: [], studentsStatus: 'error' });

      expect(q(fixture, 'students-error')).not.toBeNull();
      expect(q(fixture, 'no-students-independent')).toBeNull();
    });

    it('okullu öğretmende lookup hata verdiyse genel boş metni göstermez', () => {
      const fixture = create({ scope: 'student', students: [], studentsStatus: 'error' });

      expect(q(fixture, 'students-error')).not.toBeNull();
      expect(q(fixture, 'no-students')).toBeNull();
    });

    it('yüklenmeden önce öğrenci seçici disabled, yüklenince enabled olur', () => {
      const fixture = create({ isIndependentTutor: true, students: [], studentsStatus: 'loading' });
      const control = fixture.componentInstance['form'].controls.studentId;
      expect(control.disabled).toBeTrue();

      statusSig.set('error');
      fixture.detectChanges();
      expect(control.disabled).toBeTrue();

      statusSig.set('loaded');
      fixture.detectChanges();
      expect(control.enabled).toBeTrue();
    });

    it('yükleniyor durumunda açılan dialog, parent sinyali yüklenince canlı güncellenir', () => {
      const fixture = create({ isIndependentTutor: true, students: [], studentsStatus: 'loading' });
      expect(q(fixture, 'students-loading')).not.toBeNull();

      studentsSig.set([ada]);
      statusSig.set('loaded');
      fixture.detectChanges();

      expect(q(fixture, 'students-loading')).toBeNull();
      expect(q(fixture, 'no-students-independent')).toBeNull();
      expect(fixture.componentInstance['filteredStudents']()).toEqual([ada]);
      expect(fixture.componentInstance['form'].controls.studentId.enabled).toBeTrue();
    });
  });
});

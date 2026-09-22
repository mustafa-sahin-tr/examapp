import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatSelect } from '@angular/material/select';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';

import { VisibilitySectionComponent } from './visibility-section.component';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { AuthService, UserProfile } from '../../../services/auth.service';
import { WorksheetStudentVisibility, WorksheetTeacherSharing } from '../../../models/test-instance';

import rootTr from '../../../../../public/i18n/tr.json';

/** Gerçek sözlükten beklenen metinler (issue #183): anahtar bozulursa test kırılır. */
const schoolOnlyTexts = rootTr.shared.visibilitySection.teacherSharing.schoolOnly;

interface SetupOptions {
  schoolId?: number | null;
  role?: string;
  mobile?: boolean;
}

function profileOf(schoolId: number | null): UserProfile {
  return { id: 1, keycloakId: 'k', fullName: 'T', email: 'e', avatar: '', profileId: 1, role: 'Teacher', schoolId };
}

describe('VisibilitySectionComponent', () => {
  let component: VisibilitySectionComponent;
  let fixture: ComponentFixture<VisibilitySectionComponent>;
  /** AuthService.user stub'ı — sonradan set edilerek refresh sonrası profil güncellemesi taklit edilir. */
  let user: WritableSignal<UserProfile | null>;

  /**
   * AuthService yalnızca `user` signal'ı ve `hasRole()` için stub'lanır; HttpClient/Router bağımlılığı gerekmez.
   * Headless Chrome penceresi (800x600 yatay) Handset breakpoint'ine girdiği için BreakpointObserver
   * masaüstü/mobil olarak açıkça sabitlenir.
   */
  async function setup({ schoolId = null, role = 'Teacher', mobile = false }: SetupOptions = {}): Promise<void> {
    user = signal<UserProfile | null>(profileOf(schoolId));
    await TestBed.configureTestingModule({
      imports: [translocoTestingModule(), VisibilitySectionComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AuthService, useValue: { user, hasRole: (r: string) => r === role } },
        { provide: BreakpointObserver, useValue: { observe: () => of({ matches: mobile, breakpoints: {} }) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(VisibilitySectionComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function radioLabels(): string[] {
    const labels = fixture.nativeElement.querySelectorAll('.vs-radio-option mat-radio-button') as NodeListOf<HTMLElement>;
    return Array.from(labels).map((el) => el.textContent?.trim() ?? '');
  }

  function noteElement(): HTMLElement | null {
    return fixture.nativeElement.querySelector('.vs-option-note');
  }

  describe('okulu olan kullanıcı (masaüstü)', () => {
    beforeEach(() => setup({ schoolId: 42 }));

    it('should create', () => {
      expect(component).toBeTruthy();
    });

    it('4 seçenek gösterir; SchoolOnly etiketi ve açıklaması gerçek sözlükten gelir', () => {
      expect(component.visibleTeacherSharingOptions().length).toBe(4);
      expect(radioLabels()).toContain(schoolOnlyTexts.label);
      expect(component.optionDescription('schoolOnly')).toBe(schoolOnlyTexts.description);
      expect(component.isOptionDisabled(WorksheetTeacherSharing.SchoolOnly)).toBeFalse();
    });

    it('SchoolOnly seçilince 3 değeriyle emit eder', () => {
      const emitted: unknown[] = [];
      component.visibilityChange.subscribe((change) => emitted.push(change));

      component.onTeacherSharingChange(WorksheetTeacherSharing.SchoolOnly);

      expect(emitted).toEqual([{ teacherSharing: 3, studentVisibility: WorksheetStudentVisibility.Normal }]);
      expect(component.summary()).toContain(rootTr.shared.visibilitySection.summaryTeacher.schoolOnly);
    });
  });

  describe('okulsuz kullanıcı (masaüstü)', () => {
    beforeEach(() => setup({ schoolId: null }));

    it('SchoolOnly seçeneğini göstermez (3 seçenek)', () => {
      expect(component.visibleTeacherSharingOptions().length).toBe(3);
      expect(radioLabels()).not.toContain(schoolOnlyTexts.label);
      expect(component.schoolOnlyUnavailable()).toBeFalse();
      expect(noteElement()).toBeNull();
    });

    it('mevcut değer SchoolOnly ise seçenek görünür ama devre dışıdır; not gösterilir ve radio ile ilişkilendirilir', () => {
      component.teacherSharing = WorksheetTeacherSharing.SchoolOnly;
      fixture.detectChanges();

      expect(component.visibleTeacherSharingOptions().length).toBe(4);
      expect(component.isOptionDisabled(WorksheetTeacherSharing.SchoolOnly)).toBeTrue();
      expect(component.schoolOnlyUnavailable()).toBeTrue();
      const note = noteElement();
      expect(note?.textContent?.trim()).toBe(schoolOnlyTexts.unavailable);
      expect(note?.id).toBe(component.schoolOnlyNoteId);
      const describedInput = fixture.nativeElement.querySelector(
        `input[type="radio"][aria-describedby="${component.schoolOnlyNoteId}"]`
      ) as HTMLInputElement | null;
      expect(describedInput).not.toBeNull();
      expect(describedInput?.disabled).toBeTrue();
    });

    it('profil sonradan (refresh ile) schoolId alınca seçenek kendiliğinden belirir', () => {
      expect(radioLabels()).not.toContain(schoolOnlyTexts.label);

      user.set(profileOf(7));
      fixture.detectChanges();

      expect(component.visibleTeacherSharingOptions().length).toBe(4);
      expect(radioLabels()).toContain(schoolOnlyTexts.label);
      expect(component.isOptionDisabled(WorksheetTeacherSharing.SchoolOnly)).toBeFalse();
    });

    it('schoolId yoksa teacher.schoolId yedeğinden okur', () => {
      user.set({ ...profileOf(null), teacher: { id: 1, userId: 1, schoolName: 'X', schoolId: 9 } });
      fixture.detectChanges();

      expect(component.userSchoolId()).toBe(9);
      expect(component.visibleTeacherSharingOptions().length).toBe(4);
    });
  });

  describe('admin (okulsuz)', () => {
    beforeEach(() => setup({ schoolId: null, role: 'Admin' }));

    it('okulu olmasa da SchoolOnly seçeneğini görür ve seçebilir', () => {
      expect(component.visibleTeacherSharingOptions().length).toBe(4);
      expect(radioLabels()).toContain(schoolOnlyTexts.label);
      expect(component.isOptionDisabled(WorksheetTeacherSharing.SchoolOnly)).toBeFalse();
      expect(component.schoolOnlyUnavailable()).toBeFalse();
    });
  });

  describe('mobil (mat-select)', () => {
    function openSelect(): HTMLElement[] {
      fixture.debugElement.query(By.directive(MatSelect)).componentInstance.open();
      fixture.detectChanges();
      return Array.from(document.querySelectorAll('mat-option'));
    }

    afterEach(() => {
      // Overlay'de açık kalan panel sonraki testi kirletmesin.
      fixture.debugElement.query(By.directive(MatSelect))?.componentInstance.close();
      fixture.detectChanges();
    });

    it('okulu olan kullanıcıda 4 seçenek listelenir, SchoolOnly etkindir', async () => {
      await setup({ schoolId: 42, mobile: true });

      const options = openSelect();

      expect(fixture.nativeElement.querySelector('mat-radio-group')).toBeNull();
      expect(options.length).toBe(4);
      const schoolOnly = options.find((o) => o.textContent?.trim() === schoolOnlyTexts.label);
      expect(schoolOnly).toBeDefined();
      expect(schoolOnly?.getAttribute('aria-disabled')).toBe('false');
    });

    it('okulsuz kullanıcıda 3 seçenek listelenir, not gösterilmez', async () => {
      await setup({ schoolId: null, mobile: true });

      const options = openSelect();

      expect(options.length).toBe(3);
      expect(noteElement()).toBeNull();
    });

    it('okulsuz kullanıcıda mevcut değer SchoolOnly ise seçenek aria-disabled olur ve not görünür', async () => {
      await setup({ schoolId: null, mobile: true });
      component.teacherSharing = WorksheetTeacherSharing.SchoolOnly;
      fixture.detectChanges();

      const options = openSelect();

      expect(options.length).toBe(4);
      const schoolOnly = options.find((o) => o.textContent?.trim() === schoolOnlyTexts.label);
      expect(schoolOnly?.getAttribute('aria-disabled')).toBe('true');
      expect(noteElement()?.textContent?.trim()).toBe(schoolOnlyTexts.unavailable);
    });
  });
});

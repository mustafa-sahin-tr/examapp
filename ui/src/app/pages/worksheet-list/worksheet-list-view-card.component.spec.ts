import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { WorksheetListViewCardComponent } from './worksheet-list-view-card.component';
import { Test, WorksheetTeacherSharing } from '../../models/test-instance';

import { TranslocoTestingModule } from '@jsverse/transloco';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import rootTr from '../../../../public/i18n/tr.json';
import worksheetListTr from '../../../../public/i18n/worksheet-list/tr.json';

/** Gercek scope sozlugu yuklenir; anahtar bozulursa test kirilir (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
  // Scope sozlugu hem scope yolu (provideTranslocoScope yukleyicisi) hem de kok 'tr' icine
  // gomulu olarak verilir; sablondaki 'prefix' bicimi ikincisinden cozulur.
  langs: { tr: { ...rootTr, 'worksheet-list': worksheetListTr }, 'worksheet-list/tr': worksheetListTr },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
    // TranslocoTestingModule uygulamanin config'ini almaz; scope oneki kebab kalsin (issue #183).
    scopes: { keepCasing: true },
  },
  preloadLangs: true,
});

describe('WorksheetListViewCardComponent', () => {
  let component: WorksheetListViewCardComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [WorksheetListViewCardComponent, translocoTesting],
      providers: [provideRouter([])],
    });

    component = TestBed.createComponent(WorksheetListViewCardComponent).componentInstance;
  });

  function setCourse(partial: Partial<Test>): void {
    component.course = { id: 1, name: 'W', ...partial } as Test;
  }

  describe('canCopy', () => {
    it('canCopy_TeacherOnForeignPublicViewWorksheet_ReturnsTrue', () => {
      component.isTeacher = true;
      setCourse({ canEdit: false, isOwner: false, teacherSharing: WorksheetTeacherSharing.PublicView });

      expect(component.canCopy).toBeTrue();
    });

    it('canCopy_TeacherOnForeignPublicAssignableWorksheet_ReturnsTrue', () => {
      component.isTeacher = true;
      setCourse({ canEdit: false, isOwner: false, teacherSharing: WorksheetTeacherSharing.PublicAssignable });

      expect(component.canCopy).toBeTrue();
    });

    it('canCopy_TeacherOnForeignSchoolOnlyWorksheet_ReturnsTrue', () => {
      component.isTeacher = true;
      setCourse({ canEdit: false, isOwner: false, teacherSharing: WorksheetTeacherSharing.SchoolOnly });

      expect(component.canCopy).toBeTrue();
    });

    it('canCopy_NotTeacher_ReturnsFalse', () => {
      component.isTeacher = false;
      setCourse({ canEdit: false, isOwner: false, teacherSharing: WorksheetTeacherSharing.PublicView });

      expect(component.canCopy).toBeFalse();
    });

    it('canCopy_OwnWorksheet_ReturnsFalse', () => {
      component.isTeacher = true;
      setCourse({ canEdit: false, isOwner: true, teacherSharing: WorksheetTeacherSharing.PublicView });

      expect(component.canCopy).toBeFalse();
    });

    it('canCopy_EditableWorksheet_ReturnsFalse', () => {
      component.isTeacher = true;
      setCourse({ canEdit: true, isOwner: false, teacherSharing: WorksheetTeacherSharing.PublicView });

      expect(component.canCopy).toBeFalse();
    });

    it('canCopy_PrivateForeignWorksheet_ReturnsFalse', () => {
      component.isTeacher = true;
      setCourse({ canEdit: false, isOwner: false, teacherSharing: WorksheetTeacherSharing.Private });

      expect(component.canCopy).toBeFalse();
    });
  });

  describe('emitCopy', () => {
    it('emitCopy_Called_StopsPropagationAndEmitsCourseId', () => {
      setCourse({ id: 55 });
      const emitted: number[] = [];
      component.copy.subscribe((id) => emitted.push(id));
      const event = jasmine.createSpyObj<Event>('Event', ['stopPropagation']);

      component.emitCopy(event);

      expect(event.stopPropagation).toHaveBeenCalled();
      expect(emitted).toEqual([55]);
    });

    it('emitCopy_NoCourseId_DoesNotEmit', () => {
      component.course = { name: 'W' } as Test;
      const emitSpy = jasmine.createSpy('emit');
      component.copy.subscribe(emitSpy);
      const event = jasmine.createSpyObj<Event>('Event', ['stopPropagation']);

      component.emitCopy(event);

      expect(event.stopPropagation).toHaveBeenCalled();
      expect(emitSpy).not.toHaveBeenCalled();
    });
  });
});

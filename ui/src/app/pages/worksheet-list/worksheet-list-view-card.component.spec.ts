import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { WorksheetListViewCardComponent } from './worksheet-list-view-card.component';
import { Test, WorksheetTeacherSharing } from '../../models/test-instance';

describe('WorksheetListViewCardComponent', () => {
  let component: WorksheetListViewCardComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [WorksheetListViewCardComponent],
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

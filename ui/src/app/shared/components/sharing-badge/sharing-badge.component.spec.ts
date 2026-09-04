import { ComponentFixture, TestBed } from '@angular/core/testing';

import { WorksheetTeacherSharing } from '../../../models/test-instance';
import { SharingBadgeComponent } from './sharing-badge.component';

describe('SharingBadgeComponent', () => {
  let component: SharingBadgeComponent;
  let fixture: ComponentFixture<SharingBadgeComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SharingBadgeComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(SharingBadgeComponent);
    component = fixture.componentInstance;
    component.teacherSharing = WorksheetTeacherSharing.PublicView;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should show assignable label for PublicAssignable', () => {
    component.teacherSharing = WorksheetTeacherSharing.PublicAssignable;
    fixture.detectChanges();
    expect(component.label()).toBe('Herkese Açık · Atanabilir');
    expect(component.visible()).toBe(true);
  });

  it('should show view-only label for PublicView', () => {
    component.teacherSharing = WorksheetTeacherSharing.PublicView;
    fixture.detectChanges();
    expect(component.label()).toBe('Herkese Açık · Görüntüleme');
    expect(component.visible()).toBe(true);
  });

  it('should hide the badge for Private and not mislabel it as public', () => {
    component.teacherSharing = WorksheetTeacherSharing.Private;
    fixture.detectChanges();
    expect(component.visible()).toBe(false);
    expect(component.label()).not.toContain('Herkese Açık');
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormBuilder } from '@angular/forms';
import { By } from '@angular/platform-browser';

import { TestFormComponent } from './test-form.component';

describe('TestFormComponent', () => {
  let fixture: ComponentFixture<TestFormComponent>;
  let component: TestFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TestFormComponent] }).compileComponents();

    fixture = TestBed.createComponent(TestFormComponent);
    component = fixture.componentInstance;
    component.form = new FormBuilder().group({
      name: [''],
      subtitle: [''],
      description: [''],
      bookId: [null],
      bookTestId: [null],
      newBookName: [''],
      newBookTestName: [''],
      gradeId: [null],
      subjectId: [null],
      topicId: [null],
      subtopicId: [null],
      maxDurationMinutes: [10],
      isPracticeTest: [false],
      imageUrl: [''],
    });
  });

  function applyToAllButtons() {
    return fixture.debugElement
      .queryAll(By.css('button'))
      .filter((b) => (b.nativeElement.textContent || '').includes('Hepsine uygula'));
  }

  it('builds', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('does not render the "hepsine uygula" triggers when showApplyToAll is false', () => {
    component.showApplyToAll = false;
    fixture.detectChanges();

    expect(applyToAllButtons().length).toBe(0);
  });

  it('renders the "hepsine uygula" triggers when showApplyToAll is true', () => {
    component.showApplyToAll = true;
    fixture.detectChanges();

    expect(applyToAllButtons().length).toBeGreaterThan(0);
  });

  describe('compact input', () => {
    it('compact_DefaultsToFalse_RendersFullForm', () => {
      fixture.detectChanges();
      const form = fixture.debugElement.query(By.css('form.tf'));
      expect(component.compact).toBeFalse();
      expect(form.nativeElement.classList.contains('compact')).toBeFalse();
    });

    it('compact_WhenTrue_AddsCompactClassToForm', () => {
      component.compact = true;
      fixture.detectChanges();
      const form = fixture.debugElement.query(By.css('form.tf'));
      expect(form.nativeElement.classList.contains('compact')).toBeTrue();
    });
  });

  it('cannot emit applySubjectToAll while the trigger is hidden', () => {
    component.showApplyToAll = false;
    fixture.detectChanges();

    const emitted = jasmine.createSpy('applySubjectToAll');
    component.applySubjectToAll.subscribe(emitted);

    // nothing to click — the *ngIf removed the button entirely
    expect(applyToAllButtons().length).toBe(0);
    expect(emitted).not.toHaveBeenCalled();
  });
});

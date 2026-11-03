import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { of, throwError } from 'rxjs';
import { LearningOutcome } from '../../../models/learning-outcome';
import { LearningOutcomeService } from '../../../services/learning-outcome.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { LearningOutcomeDialogComponent } from './learning-outcome-dialog.component';
import { LearningOutcomePickerComponent } from './learning-outcome-picker.component';

const outcome = (id: number): LearningOutcome => ({
  id,
  code: `MAT.5.${id}`,
  description: `Kazanım ${id}`,
  curriculumVersion: 'Maarif',
  gradeId: 5,
  subjectId: 2,
  subTopicId: null,
});

const all = (fixture: ComponentFixture<unknown>, id: string) =>
  Array.from<HTMLElement>((fixture.nativeElement as HTMLElement).querySelectorAll(`[data-testid="${id}"]`));

describe('LearningOutcomePickerComponent (issue #692)', () => {
  let service: { search: ReturnType<typeof vi.fn> };

  function create(rows: LearningOutcome[] | null, gradeId: number | null = 5, subjectId: number | null = 2) {
    service = { search: vi.fn().mockReturnValue(rows ? of(rows) : throwError(() => new Error('nope'))) };
    TestBed.configureTestingModule({
      imports: [LearningOutcomePickerComponent, translocoTestingModule()],
      providers: [{ provide: LearningOutcomeService, useValue: service }],
    });
    const fixture = TestBed.createComponent(LearningOutcomePickerComponent);
    fixture.componentRef.setInput('gradeId', gradeId);
    fixture.componentRef.setInput('subjectId', subjectId);
    fixture.detectChanges();
    return fixture;
  }

  it('asks for grade and subject before searching', () => {
    const fixture = create([], 5, null);

    expect(service.search).not.toHaveBeenCalled();
    expect(all(fixture, 'outcome-no-scope').length).toBe(1);
  });

  it('lists outcomes of the grade and subject', () => {
    const fixture = create([outcome(1), outcome(2)]);

    expect(service.search).toHaveBeenCalledWith(5, 2, '');
    expect(all(fixture, 'outcome-option').length).toBe(2);
  });

  it('searches again when the subject changes', () => {
    const fixture = create([outcome(1)]);

    fixture.componentRef.setInput('subjectId', 3);
    fixture.detectChanges();

    expect(service.search).toHaveBeenLastCalledWith(5, 3, '');
  });

  it('explains an empty list without blocking anything', () => {
    const fixture = create([]);

    expect(all(fixture, 'outcome-empty').length).toBe(1);
  });

  it('shows an error when the list cannot be loaded', () => {
    const fixture = create(null);

    expect(all(fixture, 'outcome-error').length).toBe(1);
  });

  it('emits the selection when an outcome is ticked and unticked', () => {
    const fixture = create([outcome(1)]);
    const emitted: LearningOutcome[][] = [];
    fixture.componentInstance.selectedChange.subscribe((value) => emitted.push(value));

    fixture.nativeElement.querySelector('[data-testid="outcome-option"] input').click();
    fixture.componentRef.setInput('selected', [outcome(1)]);
    fixture.detectChanges();
    expect(all(fixture, 'outcome-chip').length).toBe(1);
    fixture.nativeElement.querySelector('[data-testid="outcome-option"] input').click();

    expect(emitted.map((value) => value.map((o) => o.id))).toEqual([[1], []]);
  });
});

describe('LearningOutcomeDialogComponent (issue #692)', () => {
  let service: { search: ReturnType<typeof vi.fn>; getForQuestion: ReturnType<typeof vi.fn>; setForQuestion: ReturnType<typeof vi.fn> };
  let close: ReturnType<typeof vi.fn>;

  function create(current: LearningOutcome[] | null, saveFails = false) {
    close = vi.fn();
    service = {
      search: vi.fn().mockReturnValue(of([outcome(1), outcome(2)])),
      getForQuestion: vi.fn().mockReturnValue(current ? of(current) : throwError(() => new Error('nope'))),
      setForQuestion: vi.fn().mockReturnValue(saveFails ? throwError(() => new Error('nope')) : of([])),
    };
    TestBed.configureTestingModule({
      imports: [LearningOutcomeDialogComponent, translocoTestingModule()],
      providers: [
        { provide: LearningOutcomeService, useValue: service },
        { provide: MAT_DIALOG_DATA, useValue: { questionId: 9, gradeId: 5, subjectId: 2 } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(LearningOutcomeDialogComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('keeps existing links and saves the changed set', () => {
    const fixture = create([outcome(1)]);

    (fixture.nativeElement.querySelectorAll('[data-testid="outcome-option"] input')[1] as HTMLInputElement).click();
    fixture.detectChanges();
    all(fixture, 'outcome-dialog-save')[0].click();

    expect(service.setForQuestion).toHaveBeenCalledWith(9, [1, 2]);
    expect(close).toHaveBeenCalledWith(true);
  });

  it('cannot save when current links failed to load, so they are not wiped', () => {
    const fixture = create(null);

    expect(all(fixture, 'outcome-dialog-error').length).toBe(1);
    expect((all(fixture, 'outcome-dialog-save')[0] as HTMLButtonElement).disabled).toBe(true);
  });

  it('stays open with a message when saving fails', () => {
    const fixture = create([], true);

    all(fixture, 'outcome-dialog-save')[0].click();
    fixture.detectChanges();

    expect(close).not.toHaveBeenCalled();
    expect(all(fixture, 'outcome-dialog-error').length).toBe(1);
  });
});

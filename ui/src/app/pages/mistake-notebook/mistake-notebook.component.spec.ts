import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { MistakeItem } from '../../models/practice';
import { PracticeService } from '../../services/practice.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { MistakeNotebookComponent } from './mistake-notebook.component';

const item = (questionId: number, subjectId: number, subjectName: string): MistakeItem => ({
  questionId,
  subjectId,
  subjectName,
  topicId: null,
  topicName: null,
  lastWrongAt: '2026-11-01T10:00:00Z',
  wrongCount: 2,
  reason: null,
});

describe('MistakeNotebookComponent (issue #698)', () => {
  let practice: {
    getMistakes: ReturnType<typeof vi.fn>;
    startSession: ReturnType<typeof vi.fn>;
    setMistakeReason: ReturnType<typeof vi.fn>;
  };

  function create(items: MistakeItem[]): ComponentFixture<MistakeNotebookComponent> {
    practice = {
      getMistakes: vi.fn().mockReturnValue(of({ totalCount: items.length, items })),
      startSession: vi.fn().mockReturnValue(of({ id: 9 })),
      setMistakeReason: vi.fn().mockReturnValue(of(undefined)),
    };
    TestBed.configureTestingModule({
      imports: [MistakeNotebookComponent, NoopAnimationsModule, translocoTestingModule()],
      providers: [provideRouter([]), { provide: PracticeService, useValue: practice }],
    });
    const fixture = TestBed.createComponent(MistakeNotebookComponent);
    fixture.detectChanges();
    return fixture;
  }

  const el = (fixture: ComponentFixture<MistakeNotebookComponent>) => fixture.nativeElement as HTMLElement;

  it('empty_ShowsGoodNewsAndPracticeLink', () => {
    const fixture = create([]);
    expect(el(fixture).querySelector('[data-test="mistakes-empty"]')).not.toBeNull();
    expect(el(fixture).querySelector('[data-test="mistakes-start"]')).toBeNull();
  });

  it('list_RendersItems', () => {
    const fixture = create([item(1, 10, 'Matematik'), item(2, 11, 'Fen')]);
    expect(el(fixture).querySelectorAll('[data-test="mistake-item"]').length).toBe(2);
  });

  it('subjectFilter_NarrowsListAndPractice', () => {
    const fixture = create([item(1, 10, 'Matematik'), item(2, 11, 'Fen')]);
    const component = fixture.componentInstance;
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    component.subjectId.set(11);
    fixture.detectChanges();
    expect(el(fixture).querySelectorAll('[data-test="mistake-item"]').length).toBe(1);

    el(fixture).querySelector<HTMLButtonElement>('[data-test="mistakes-start"]')?.click();
    expect(practice.startSession).toHaveBeenCalledWith({ mistakes: true, subjectIds: [11] });
  });

  it('start_OpensThePracticeSession', () => {
    const fixture = create([item(1, 10, 'Matematik')]);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    fixture.componentInstance.startPractice();

    expect(practice.startSession).toHaveBeenCalledWith({ mistakes: true, subjectIds: [] });
    expect(navigate).toHaveBeenCalledWith(['/practice'], { queryParams: { session: 9 } });
  });

  it('setReason_Saves_AndRevertsOnError', () => {
    const fixture = create([item(1, 10, 'Matematik')]);
    const component = fixture.componentInstance;

    component.setReason(component.items()[0], 'Calculation');
    expect(practice.setMistakeReason).toHaveBeenCalledWith(1, 'Calculation');
    expect(component.items()[0].reason).toBe('Calculation');

    practice.setMistakeReason.mockReturnValue(throwError(() => new Error('x')));
    component.setReason(component.items()[0], 'Time');
    expect(component.items()[0].reason).toBe('Calculation');
    expect(component.saveFailed()).toBe(true);
  });

  it('loadError_OffersRetry', () => {
    const fixture = create([]);
    practice.getMistakes.mockReturnValue(throwError(() => new Error('x')));
    fixture.componentInstance.load();
    fixture.detectChanges();
    expect(fixture.componentInstance.loadFailed()).toBe(true);
    expect(el(fixture).querySelector('[role="alert"] button')).not.toBeNull();
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import { ImageSelectorComponent } from './image-selector.component';

function answer(x: number, y: number) {
  return { label: '?', x, y, width: 40, height: 20, isCorrect: false, id: 0, imageUrl: '' };
}

function region(name: string, answers: any[] = []) {
  return {
    name,
    x: 0,
    y: 0,
    width: 300,
    height: 300,
    answers,
    passageId: '0',
    imageId: '',
    imageUrl: '',
    id: 0,
    isExample: false,
    exampleAnswer: null,
  };
}

describe('ImageSelectorComponent', () => {
  let component: ImageSelectorComponent;
  let fixture: ComponentFixture<ImageSelectorComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ImageSelectorComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    })
    .compileComponents();

    fixture = TestBed.createComponent(ImageSelectorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('answerCount', () => {
    it('answerCount_NoExplicitChange_DefaultsToFour', () => {
      expect(component.answerCount()).toBe(4);
    });
  });

  describe('renameAnswers', () => {
    // Regression: "Şık 4" bug — last answer must be relabelled too.
    it('renameAnswers_ThreeVerticalAnswers_ProducesABC', () => {
      component.regions.set([region('Soru 1', [answer(10, 90), answer(10, 10), answer(10, 50)])] as any);

      component.renameAnswers(0);

      expect(component.regions()[0].answers.map((a) => a.label)).toEqual(['A', 'B', 'C']);
    });

    it('renameAnswers_FourVerticalAnswers_ProducesABCD', () => {
      component.regions.set([
        region('Soru 1', [answer(10, 30), answer(10, 90), answer(10, 0), answer(10, 60)]),
      ] as any);

      component.renameAnswers(0);

      expect(component.regions()[0].answers.map((a) => a.label)).toEqual(['A', 'B', 'C', 'D']);
    });

    it('renameAnswers_FiveVerticalAnswers_ProducesABCDE', () => {
      component.regions.set([
        region('Soru 1', [
          answer(10, 40),
          answer(10, 10),
          answer(10, 70),
          answer(10, 100),
          answer(10, 130),
        ]),
      ] as any);

      component.renameAnswers(0);

      expect(component.regions()[0].answers.map((a) => a.label)).toEqual(['A', 'B', 'C', 'D', 'E']);
    });

    it('renameAnswers_HorizontalRow_LabelsLeftToRight', () => {
      component.regions.set([
        region('Soru 1', [answer(120, 10), answer(0, 10), answer(240, 10), answer(60, 12)]),
      ] as any);

      component.renameAnswers(0);

      const sorted = [...component.regions()[0].answers].sort((a, b) => a.x - b.x);
      expect(sorted.map((a) => a.label)).toEqual(['A', 'B', 'C', 'D']);
    });
  });

  describe('question selection', () => {
    it('activeRegionIndex_NoRegions_ReturnsNull', () => {
      component.regions.set([]);
      expect(component.activeRegionIndex).toBeNull();
    });

    it('selectQuestion_ValidIndex_EmitsSelectedQuestionChangeWithRegion', () => {
      component.regions.set([region('Soru 1'), region('Soru 2')] as any);
      const spy = jasmine.createSpy('selectedQuestionChange');
      component.selectedQuestionChange.subscribe(spy);

      component.selectQuestion(1);

      expect(spy).toHaveBeenCalledWith({ index: 1, region: component.regions()[1] });
      expect(component.activeRegionIndex).toBe(1);
    });

    it('selectNextQuestion_AtLastRegion_WrapsToFirst', () => {
      component.regions.set([region('Soru 1'), region('Soru 2')] as any);
      component.selectQuestion(1);

      component.selectNextQuestion();

      expect(component.activeRegionIndex).toBe(0);
    });

    it('selectPreviousQuestion_AtFirstRegion_WrapsToLast', () => {
      component.regions.set([region('Soru 1'), region('Soru 2'), region('Soru 3')] as any);
      component.selectQuestion(0);

      component.selectPreviousQuestion();

      expect(component.activeRegionIndex).toBe(2);
    });
  });

  describe('togglePreviewMode', () => {
    it('togglePreviewMode_Entering_KeepsImageFiles', () => {
      spyOn((component as any).questionService, 'getAll').and.returnValue(of([]));
      spyOn((component as any).testService, 'convertQuestionsToRegions').and.returnValue([]);
      const files = [new File(['x'], 'p1.png', { type: 'image/png' })];
      component.imageFiles = files;

      component.togglePreviewMode(7);

      expect(component.previewMode()).toBeTrue();
      expect(component.imageFiles).toBe(files);
    });
  });
});

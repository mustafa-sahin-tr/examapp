import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionCanvasViewComponent } from './question-canvas-view.component';
import { translocoTestingModule } from '../../testing/transloco-testing';

describe('QuestionCanvasViewComponent', () => {
  let component: QuestionCanvasViewComponent;
  let fixture: ComponentFixture<QuestionCanvasViewComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [translocoTestingModule(), QuestionCanvasViewComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(QuestionCanvasViewComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('zoomPercent', () => {
    it('zoomPercent_DefaultScale_Returns100', () => {
      component.contentScale = 1;
      expect(component.zoomPercent).toBe(100);
    });

    it('zoomPercent_ScaledUp_ReturnsRoundedPercent', () => {
      component.contentScale = 1.234;
      expect(component.zoomPercent).toBe(123);
    });

    it('zoomPercent_ZeroOrMissingScale_FallsBackTo100', () => {
      component.contentScale = 0 as unknown as number;
      expect(component.zoomPercent).toBe(100);
    });
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionCanvasViewComponentv2 } from './question-canvas-view-v2.component';

describe('QuestionCanvasViewComponentv2', () => {
  let component: QuestionCanvasViewComponentv2;
  let fixture: ComponentFixture<QuestionCanvasViewComponentv2>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuestionCanvasViewComponentv2],
    }).compileComponents();

    fixture = TestBed.createComponent(QuestionCanvasViewComponentv2);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

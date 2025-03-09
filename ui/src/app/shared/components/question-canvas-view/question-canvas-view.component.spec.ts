import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionCanvasViewComponent } from './question-canvas-view.component';

describe('QuestionCanvasViewComponent', () => {
  let component: QuestionCanvasViewComponent;
  let fixture: ComponentFixture<QuestionCanvasViewComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuestionCanvasViewComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(QuestionCanvasViewComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

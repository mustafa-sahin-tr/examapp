import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionLiteViewComponent } from './question-lite-view.component';

describe('QuestionLiteViewComponent', () => {
  let component: QuestionLiteViewComponent;
  let fixture: ComponentFixture<QuestionLiteViewComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuestionLiteViewComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(QuestionLiteViewComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

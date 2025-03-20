import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionNavigatorComponent } from './question-navigator.component';

describe('QuestionNavigatorComponent', () => {
  let component: QuestionNavigatorComponent;
  let fixture: ComponentFixture<QuestionNavigatorComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuestionNavigatorComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(QuestionNavigatorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

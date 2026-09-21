import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionNavigatorComponent } from './question-navigator.component';
import { translocoTestingModule } from '../../testing/transloco-testing';

describe('QuestionNavigatorComponent', () => {
  let component: QuestionNavigatorComponent;
  let fixture: ComponentFixture<QuestionNavigatorComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [translocoTestingModule(), QuestionNavigatorComponent]
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

import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SpinWheelComponent } from './spin-wheel.component';
import { translocoTestingModule } from '../../testing/transloco-testing';

describe('SpinWheelComponent', () => {
  let component: SpinWheelComponent;
  let fixture: ComponentFixture<SpinWheelComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [translocoTestingModule(), SpinWheelComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(SpinWheelComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

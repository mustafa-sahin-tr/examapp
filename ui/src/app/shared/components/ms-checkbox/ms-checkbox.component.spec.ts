import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MsCheckboxComponent } from './ms-checkbox.component';

describe('MsCheckboxComponent', () => {
  let component: MsCheckboxComponent;
  let fixture: ComponentFixture<MsCheckboxComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MsCheckboxComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(MsCheckboxComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

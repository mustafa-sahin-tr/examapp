import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PointCardComponent } from './point-card.component';

describe('PointCardComponent', () => {
  let component: PointCardComponent;
  let fixture: ComponentFixture<PointCardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PointCardComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(PointCardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

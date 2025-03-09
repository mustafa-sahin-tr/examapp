import { ComponentFixture, TestBed } from '@angular/core/testing';

import { WorksheetBadgeComponent } from './worksheet-badge.component';

describe('WorksheetBadgeComponent', () => {
  let component: WorksheetBadgeComponent;
  let fixture: ComponentFixture<WorksheetBadgeComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WorksheetBadgeComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(WorksheetBadgeComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

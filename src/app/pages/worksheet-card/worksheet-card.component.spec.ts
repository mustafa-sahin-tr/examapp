import { ComponentFixture, TestBed } from '@angular/core/testing';

import { WorksheetCardComponent } from './worksheet-card.component';

describe('WorksheetCardComponent', () => {
  let component: WorksheetCardComponent;
  let fixture: ComponentFixture<WorksheetCardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WorksheetCardComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(WorksheetCardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

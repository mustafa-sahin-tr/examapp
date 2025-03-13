import { ComponentFixture, TestBed } from '@angular/core/testing';

import { WorksheetDetailComponent } from './worksheet-detail.component';

describe('WorksheetDetailComponent', () => {
  let component: WorksheetDetailComponent;
  let fixture: ComponentFixture<WorksheetDetailComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WorksheetDetailComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(WorksheetDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

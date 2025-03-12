import { ComponentFixture, TestBed } from '@angular/core/testing';

import { StudentTimeChartComponent } from './student-time-chart.component';

describe('StudentTimeChartComponent', () => {
  let component: StudentTimeChartComponent;
  let fixture: ComponentFixture<StudentTimeChartComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StudentTimeChartComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(StudentTimeChartComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

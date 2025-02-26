import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CompletedWorksheetCardComponent } from './completed-worksheet-card.component';

 

describe('CompletedWorksheetComponent', () => {
  let component: CompletedWorksheetCardComponent;
  let fixture: ComponentFixture<CompletedWorksheetCardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CompletedWorksheetCardComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(CompletedWorksheetCardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

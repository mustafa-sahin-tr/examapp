import { ComponentFixture, TestBed } from '@angular/core/testing';

import { VisibilitySectionComponent } from './visibility-section.component';

describe('VisibilitySectionComponent', () => {
  let component: VisibilitySectionComponent;
  let fixture: ComponentFixture<VisibilitySectionComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [VisibilitySectionComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(VisibilitySectionComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';

import { EducationBannerComponent } from './education-banner.component';

describe('EducationBannerComponent', () => {
  let component: EducationBannerComponent;
  let fixture: ComponentFixture<EducationBannerComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EducationBannerComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(EducationBannerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';

import { FunfactsComponent } from './funfacts.component';

describe('FunfactsComponent', () => {
  let component: FunfactsComponent;
  let fixture: ComponentFixture<FunfactsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FunfactsComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(FunfactsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BadgeThropyComponent } from './badge-thropy.component';

describe('BadgeThropyComponent', () => {
  let component: BadgeThropyComponent;
  let fixture: ComponentFixture<BadgeThropyComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BadgeThropyComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(BadgeThropyComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

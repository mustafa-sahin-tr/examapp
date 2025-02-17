import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BadgeBoxComponent } from './badge-box.component';

describe('BadgeBoxComponent', () => {
  let component: BadgeBoxComponent;
  let fixture: ComponentFixture<BadgeBoxComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BadgeBoxComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(BadgeBoxComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

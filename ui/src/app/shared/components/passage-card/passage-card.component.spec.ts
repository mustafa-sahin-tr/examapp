import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PassageCardComponent } from './passage-card.component';

describe('PassageCardComponent', () => {
  let component: PassageCardComponent;
  let fixture: ComponentFixture<PassageCardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PassageCardComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(PassageCardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

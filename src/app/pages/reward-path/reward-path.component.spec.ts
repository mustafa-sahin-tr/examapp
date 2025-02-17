import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RewardPathComponent } from './reward-path.component';

describe('RewardPathComponent', () => {
  let component: RewardPathComponent;
  let fixture: ComponentFixture<RewardPathComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RewardPathComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(RewardPathComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

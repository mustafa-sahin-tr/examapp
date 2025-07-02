import { ComponentFixture, TestBed } from '@angular/core/testing';

import { FuturesComponent } from './futures.component';

describe('FuturesComponent', () => {
  let component: FuturesComponent;
  let fixture: ComponentFixture<FuturesComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FuturesComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(FuturesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

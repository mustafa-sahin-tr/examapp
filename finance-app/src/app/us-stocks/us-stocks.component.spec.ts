import { ComponentFixture, TestBed } from '@angular/core/testing';

import { UsStocksComponent } from './us-stocks.component';

describe('UsStocksComponent', () => {
  let component: UsStocksComponent;
  let fixture: ComponentFixture<UsStocksComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UsStocksComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(UsStocksComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

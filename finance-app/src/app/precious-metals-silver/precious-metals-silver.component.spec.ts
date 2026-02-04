import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PreciousMetalsSilverComponent } from './precious-metals-silver.component';

describe('PreciousMetalsSilverComponent', () => {
  let component: PreciousMetalsSilverComponent;
  let fixture: ComponentFixture<PreciousMetalsSilverComponent>;
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PreciousMetalsSilverComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(PreciousMetalsSilverComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

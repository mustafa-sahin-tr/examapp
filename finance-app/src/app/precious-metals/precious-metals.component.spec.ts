import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PreciousMetalsComponent } from './precious-metals.component';

describe('PreciousMetalsComponent', () => {
  let component: PreciousMetalsComponent;
  let fixture: ComponentFixture<PreciousMetalsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PreciousMetalsComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(PreciousMetalsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

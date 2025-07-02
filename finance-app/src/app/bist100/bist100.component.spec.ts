import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Bist100Component } from './bist100.component';

describe('Bist100Component', () => {
  let component: Bist100Component;
  let fixture: ComponentFixture<Bist100Component>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Bist100Component]
    })
    .compileComponents();

    fixture = TestBed.createComponent(Bist100Component);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

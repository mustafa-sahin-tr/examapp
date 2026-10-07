import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { PublicLayoutComponent } from './public-layout.component';

describe('PublicLayoutComponent', () => {
  let component: PublicLayoutComponent;
  let fixture: ComponentFixture<PublicLayoutComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PublicLayoutComponent],
      // AuthService HttpClient ister; istekler test denetleyicisinde kalır, ağa çıkılmaz.
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    })
    .compileComponents();

    fixture = TestBed.createComponent(PublicLayoutComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

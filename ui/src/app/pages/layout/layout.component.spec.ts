import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { LayoutComponent } from './layout.component';
import { SignalRService } from '../../services/signalr.service';

describe('LayoutComponent', () => {
  let component: LayoutComponent;
  let fixture: ComponentFixture<LayoutComponent>;
  let savedStorage: Record<string, string>;

  // Oturum durumu localStorage'tan okunur; başka spec'lerden sızan bir token oturum açık dalını
  // render ettirir. Bu duman testi oturum kapalı kabuğu doğrular, ön koşul burada sabitlenir (#394).
  // Not: oturum açık dal şu an kırık (şablon `userName()` çağırıyor, sınıfta getter) — ayrıca raporlandı.
  beforeEach(() => {
    savedStorage = { ...localStorage };
    localStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
    Object.entries(savedStorage).forEach(([key, value]) => localStorage.setItem(key, value));
  });

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LayoutComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        // Gercek hub baglantisi (WebSocket) testte acilmaz.
        { provide: SignalRService, useValue: jasmine.createSpyObj<SignalRService>('SignalRService', ['startConnection']) },
      ],
    })
    .compileComponents();

    fixture = TestBed.createComponent(LayoutComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

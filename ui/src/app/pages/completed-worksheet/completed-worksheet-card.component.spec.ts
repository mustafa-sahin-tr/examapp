import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';

import { CompletedWorksheetCardComponent } from './completed-worksheet-card.component';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import completedWorksheetTr from '../../../../public/i18n/completed-worksheet/tr.json';

/** Gercek sozluk yuklenir; anahtar bozulursa test kirilir (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { 'completed-worksheet/tr': completedWorksheetTr },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
    // Uygulama config'i ile ayni: scope oneki klasor adiyla birebir ayni kalsin (issue #183).
    scopes: { keepCasing: true },
  },
  preloadLangs: true,
});

describe('CompletedWorksheetComponent', () => {
  let component: CompletedWorksheetCardComponent;
  let fixture: ComponentFixture<CompletedWorksheetCardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CompletedWorksheetCardComponent, translocoTesting],
    }).compileComponents();

    fixture = TestBed.createComponent(CompletedWorksheetCardComponent);
    component = fixture.componentInstance;
    component.completedTest = {
      id: 1,
      name: 'Deneme',
      completedDate: new Date('2026-01-01T00:00:00Z'),
      score: 80,
      durationMinutes: 12,
      correctAnswers: 8,
      wrongAnswers: 2,
      totalQuestions: 10,
      status: 1,
    };
    // Scope sozlugu (`completed-worksheet/tr`) asenkron yuklenir; ilk render'da anahtarlar ham kalir.
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('renders the localized score line', () => {
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Puan: 80/100');
  });
});

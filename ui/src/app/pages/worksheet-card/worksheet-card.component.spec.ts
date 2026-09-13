import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';

import { WorksheetCardComponent } from './worksheet-card.component';
import { Test } from '../../models/test-instance';

import { TranslocoTestingModule } from '@jsverse/transloco';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import rootTr from '../../../../public/i18n/tr.json';
import worksheetCardTr from '../../../../public/i18n/worksheet-card/tr.json';

/** Gercek scope sozlugu yuklenir; anahtar bozulursa test kirilir (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
  // Scope sozlugu hem scope yolu (provideTranslocoScope yukleyicisi) hem de kok 'tr' icine
  // gomulu olarak verilir; sablondaki 'prefix' bicimi ikincisinden cozulur.
  langs: { tr: { ...rootTr, 'worksheet-card': worksheetCardTr }, 'worksheet-card/tr': worksheetCardTr },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
    // TranslocoTestingModule uygulamanin config'ini almaz; scope oneki kebab kalsin (issue #183).
    scopes: { keepCasing: true },
  },
  preloadLangs: true,
});

describe('WorksheetCardComponent', () => {
  let component: WorksheetCardComponent;
  let fixture: ComponentFixture<WorksheetCardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WorksheetCardComponent, translocoTesting],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(WorksheetCardComponent);
    component = fixture.componentInstance;
    component.test = {
      id: 1,
      name: 'Zamanı Ölçme',
      description: 'Deneme',
      gradeId: 5,
      maxDurationSeconds: 1800,
      isPracticeTest: false,
      questionCount: 12,
      instanceCount: 3,
    } as Test;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('kart aksiyonlarını scope sözlüğünden çevirir', () => {
    component.type = 'badge';
    fixture.detectChanges();

    const labels = fixture.debugElement
      .queryAll(By.css('.button-container .action-button'))
      .map((el) => (el.nativeElement.textContent || '').trim());

    expect(labels).toContain('Başlat');
  });

  it('süre etiketini scope sözlüğünden biçimlendirir', () => {
    expect(component.getMaxDurationText()).toBe('30dk');
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom, of } from 'rxjs';

import { TutorSearchComponent } from './tutor-search.component';
import { TUTOR_SEARCH_SCOPE } from './tutor-search-scope';
import { SubjectService } from '../../services/subject.service';
import { TeacherService } from '../../services/teacher.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import tutorSearchTr from '../../../../public/i18n/tutor-search/tr.json';
import tutorSearchEn from '../../../../public/i18n/tutor-search/en.json';

describe('TutorSearchComponent (issue #196 — kompakt filtre)', () => {
  let fixture: ComponentFixture<TutorSearchComponent>;
  let component: TutorSearchComponent;
  let teacherService: jasmine.SpyObj<TeacherService>;
  let subjectService: jasmine.SpyObj<SubjectService>;

  const el = (): HTMLElement => fixture.nativeElement as HTMLElement;

  beforeEach(async () => {
    teacherService = jasmine.createSpyObj<TeacherService>('TeacherService', ['searchTutors']);
    teacherService.searchTutors.and.returnValue(of([]));
    subjectService = jasmine.createSpyObj<SubjectService>('SubjectService', ['loadCategories']);
    subjectService.loadCategories.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [
        TutorSearchComponent,
        // Gerçek scope sözlükleri: anahtar bozulursa test kırılır (issue #183).
        translocoTestingModule({
          langs: {
            [`${TUTOR_SEARCH_SCOPE}/tr`]: tutorSearchTr,
            [`${TUTOR_SEARCH_SCOPE}/en`]: tutorSearchEn,
          },
          // app.config.ts ile aynı: dil değişince şablon yeniden çevrilsin.
          translocoConfig: { reRenderOnLangChange: true },
        }),
      ],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: TeacherService, useValue: teacherService },
        { provide: SubjectService, useValue: subjectService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TutorSearchComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('ücret aralığını tek bir role="group" altında, tek etiket ve tek ₺ ekiyle gösterir', () => {
    const groups = el().querySelectorAll('.ts__price');
    expect(groups.length).toBe(1);

    const group = groups[0] as HTMLElement;
    expect(group.getAttribute('role')).toBe('group');
    // Görsel ₺ eki aria-hidden; para birimi grubun erişilebilir adında.
    expect(group.getAttribute('aria-label')).toBe('Ücret aralığı (₺)');
    expect(group.querySelector('.ts__price-label')?.textContent?.trim()).toBe('Ücret');
    expect(group.querySelector('.ts__price-sep')?.textContent?.trim()).toBe('–');
    expect(group.querySelectorAll('.ts__price-suffix').length).toBe(1);
    expect(group.querySelector('.ts__price-suffix')?.textContent?.trim()).toBe('₺');
    expect(el().querySelectorAll('[matTextSuffix]').length).toBe(0);
  });

  it('min/max input\'ları placeholder, aria-label, type=number ve min=0 taşır', () => {
    const min = el().querySelector<HTMLInputElement>('input[formControlName="minPrice"]')!;
    const max = el().querySelector<HTMLInputElement>('input[formControlName="maxPrice"]')!;

    expect(min.closest('.ts__price')).not.toBeNull();
    expect(max.closest('.ts__price')).not.toBeNull();
    expect(min.getAttribute('placeholder')).toBe('Min');
    expect(max.getAttribute('placeholder')).toBe('Max');
    expect(min.getAttribute('aria-label')).toBe('Minimum ücret');
    expect(max.getAttribute('aria-label')).toBe('Maksimum ücret');
    for (const input of [min, max]) {
      expect(input.type).toBe('number');
      expect(input.getAttribute('min')).toBe('0');
    }
  });

  it('Filtrele mevcut form değerleriyle aynı payload\'u gönderir', () => {
    teacherService.searchTutors.calls.reset();
    component.filterForm.setValue({ subjectId: 3, minPrice: 100, maxPrice: 400, online: true, inPerson: false });

    const form = fixture.debugElement.query(By.css('form.ts__filters'));
    form.triggerEventHandler('ngSubmit', {});

    expect(teacherService.searchTutors).toHaveBeenCalledOnceWith({
      subjectId: 3,
      minPrice: 100,
      maxPrice: 400,
      online: true,
      inPerson: false,
    });
  });

  it('boş bırakılan ücret alanları null gider (filtre uygulanmaz)', () => {
    teacherService.searchTutors.calls.reset();
    component.search();

    expect(teacherService.searchTutors).toHaveBeenCalledOnceWith({
      subjectId: null,
      minPrice: null,
      maxPrice: null,
      online: false,
      inPerson: false,
    });
  });

  it('Temizle tüm alanları sıfırlar ve yeniden arar', () => {
    component.filterForm.setValue({ subjectId: 3, minPrice: 100, maxPrice: 400, online: true, inPerson: true });
    teacherService.searchTutors.calls.reset();

    const resetBtn = el().querySelector<HTMLButtonElement>('.ts__filter-actions button[type="button"]')!;
    resetBtn.click();

    expect(component.filterForm.getRawValue()).toEqual({
      subjectId: null,
      minPrice: null,
      maxPrice: null,
      online: false,
      inPerson: false,
    });
    expect(teacherService.searchTutors).toHaveBeenCalledOnceWith({
      subjectId: null,
      minPrice: null,
      maxPrice: null,
      online: false,
      inPerson: false,
    });
  });

  it('ders listesi yüklenemezse hata mesajını gösterir', () => {
    component.subjectsFailed.set(true);
    fixture.detectChanges();

    expect(el().querySelector('.ts__hint-error')?.textContent?.trim()).toBe('Ders listesi yüklenemedi.');
  });

  it('boş durum aria-live="polite" ile duyurulur', () => {
    const empty = el().querySelector('.ts__state');
    expect(empty?.getAttribute('aria-live')).toBe('polite');
    expect(empty?.textContent).toContain('Bu filtrelere uygun öğretmen bulunamadı');
  });

  /**
   * Gerçek yerleşim ölçümü: karma `src/styles.scss`'i (Material tema + global kurallar) yükler.
   * Kırılım container query olduğu için host genişliği düzeni belirler (karma iframe viewport'u değil).
   */
  describe('yerleşim', () => {
    const rect = (selector: string): DOMRect => el().querySelector(selector)!.getBoundingClientRect();

    const renderAt = (width: number): void => {
      el().style.width = `${width}px`;
      fixture.detectChanges();
    };

    const expectSingleRow = (width: number): void => {
      renderAt(width);
      const filters = el().querySelector<HTMLElement>('.ts__filters')!;
      const styles = getComputedStyle(filters);
      const innerRight =
        filters.getBoundingClientRect().right -
        parseFloat(styles.borderRightWidth) -
        parseFloat(styles.paddingRight);

      const items = {
        subject: rect('.ts__subject'),
        price: rect('.ts__price'),
        online: rect('.ts__modes mat-checkbox:first-child'),
        inPerson: rect('.ts__modes mat-checkbox:last-child'),
        submit: rect('.ts__filter-actions button[type="submit"]'),
        reset: rect('.ts__filter-actions button[type="button"]'),
      };
      const rowTop = items.subject.top;
      const rowBottom = items.subject.bottom;

      for (const [name, r] of Object.entries(items)) {
        // Aynı satır: her öğe ders alanının dikey aralığıyla örtüşür ve üstü satır başlangıcına yakındır.
        expect(r.top).withContext(`${width}px ${name} top`).toBeLessThan(rowTop + 8);
        expect(r.top).withContext(`${width}px ${name} top`).toBeGreaterThanOrEqual(rowTop - 1);
        expect(r.bottom).withContext(`${width}px ${name} bottom`).toBeLessThanOrEqual(rowBottom + 8);
        // Kapsayıcının sağ kenarından taşmaz.
        expect(r.right).withContext(`${width}px ${name} right`).toBeLessThanOrEqual(innerRight + 0.5);
      }

      // Soldan sağa sıra korunur, öğeler üst üste binmez.
      expect(items.subject.right).toBeLessThanOrEqual(items.price.left);
      expect(items.price.right).toBeLessThanOrEqual(items.online.left);
      expect(items.inPerson.right).toBeLessThanOrEqual(items.submit.left);

      // Ücret grubu içeriğini taşırmaz; input'lar en az birkaç haneyi gösterecek genişlikte.
      const price = el().querySelector<HTMLElement>('.ts__price')!;
      expect(price.scrollWidth).withContext(`${width}px price overflow`).toBeLessThanOrEqual(price.clientWidth);
      el()
        .querySelectorAll('.ts__price-input')
        .forEach((field) => expect(field.getBoundingClientRect().width).toBeGreaterThanOrEqual(64));

      // Ders alanı ~180px'i geçmeyen dar sabit genişlikte.
      expect(items.subject.width).toBeLessThanOrEqual(180);
    };

    it('769px: tüm filtreler tek satırda ve taşmıyor', () => expectSingleRow(769));
    it('800px: tüm filtreler tek satırda ve taşmıyor', () => expectSingleRow(800));
    it('1040px: tüm filtreler tek satırda, butonlar sağa yaslı', () => {
      expectSingleRow(1040);
      const filters = el().querySelector<HTMLElement>('.ts__filters')!;
      const innerRight = filters.getBoundingClientRect().right - parseFloat(getComputedStyle(filters).paddingRight);
      expect(Math.abs(rect('.ts__filter-actions').right - innerRight)).toBeLessThan(2);
    });

    it('800px (EN): İngilizce etiketlerle de tek satırda', async () => {
      const transloco = TestBed.inject(TranslocoService);
      await firstValueFrom(transloco.load(`${TUTOR_SEARCH_SCOPE}/en`));
      transloco.setActiveLang('en');
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(rect('.ts__price-label').width).toBeGreaterThan(0);
      expect(el().querySelector('.ts__price-label')?.textContent?.trim()).toBe('Price');
      expectSingleRow(800);
    });

    it('ders hata ipucu satırdaki diğer öğeleri kaydırmaz', () => {
      renderAt(1040);
      const before = [rect('.ts__price').top, rect('.ts__modes').top, rect('.ts__filter-actions').top];
      component.subjectsFailed.set(true);
      fixture.detectChanges();
      const after = [rect('.ts__price').top, rect('.ts__modes').top, rect('.ts__filter-actions').top];
      expect(after).toEqual(before);
      expect(rect('.ts__hint-error').height).toBeGreaterThan(0);
    });

    it('768px altı: alanlar alt alta, ücret min/max yan yana, butonlar eşit ve ≥44px', () => {
      renderAt(500);
      const subject = rect('.ts__subject');
      const price = rect('.ts__price');
      const modes = rect('.ts__modes');
      const actions = rect('.ts__filter-actions');
      expect(price.top).toBeGreaterThanOrEqual(subject.bottom);
      expect(modes.top).toBeGreaterThanOrEqual(price.bottom);
      expect(actions.top).toBeGreaterThanOrEqual(modes.bottom);
      expect(price.width).toBeCloseTo(subject.width, 0);

      const [min, max] = Array.from(el().querySelectorAll('.ts__price-input')).map((f) => f.getBoundingClientRect());
      expect(Math.abs(min.top - max.top)).toBeLessThan(1);

      const [submit, reset] = [
        rect('.ts__filter-actions button[type="submit"]'),
        rect('.ts__filter-actions button[type="button"]'),
      ];
      expect(Math.abs(submit.width - reset.width)).toBeLessThan(1);
      expect(submit.height).toBeGreaterThanOrEqual(44);
      expect(el().querySelector<HTMLElement>('.ts__filters')!.scrollWidth).toBeLessThanOrEqual(
        el().querySelector<HTMLElement>('.ts__filters')!.clientWidth,
      );
    });
  });
});

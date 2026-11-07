import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';

import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import publicPagesEn from '../../../../../public/i18n/public-pages/en.json';
import publicPagesTr from '../../../../../public/i18n/public-pages/tr.json';
import { PRIVACY_SECTIONS, PrivacyPolicyComponent } from '../privacy-policy/privacy-policy.component';
import { TERMS_SECTIONS, TermsComponent } from '../terms/terms.component';
import { LegalSection } from './legal-page.component';

type Dict = Record<string, unknown>;
const at = (dict: Dict, path: string): unknown =>
  path.split('.').reduce<unknown>((node, key) => (node as Dict | undefined)?.[key], dict);

function expectKeys(dict: Dict, prefix: string, sections: readonly LegalSection[]): void {
  for (const key of ['title', 'version', 'updated', 'intro']) {
    expect(at(dict, `${prefix}.${key}`), `${prefix}.${key}`).toBeTypeOf('string');
  }
  for (const s of sections) {
    expect(at(dict, `${prefix}.sections.${s.key}.title`), s.key).toBeTypeOf('string');
    expect(at(dict, `${prefix}.sections.${s.key}.body`), s.key).toBeTypeOf('string');
    const items = (at(dict, `${prefix}.sections.${s.key}.items`) ?? {}) as Dict;
    // Bölüm sabitindeki madde sayısı metindekiyle aynı: eksik madde boş satır, fazlası görünmeyen madde olurdu.
    expect(Object.keys(items).length, `${prefix}.${s.key}.items`).toBe(s.items ?? 0);
  }
}

describe('legal pages (#881)', () => {
  it('every section and item has text in both languages', () => {
    for (const dict of [publicPagesTr, publicPagesEn] as Dict[]) {
      expectKeys(dict, 'privacyPolicy', PRIVACY_SECTIONS);
      expectKeys(dict, 'terms', TERMS_SECTIONS);
    }
  });

  it('privacy page shows a way back, version and date, and the KVKK sections in order', async () => {
    TestBed.configureTestingModule({
      imports: [PrivacyPolicyComponent, translocoTestingModule({ langs: { 'public-pages/tr': publicPagesTr } })],
      providers: [provideRouter([])],
    });
    const fixture = TestBed.createComponent(PrivacyPolicyComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('a.legal__back')?.getAttribute('href')).toBe('/welcome');
    expect(el.querySelector('[data-testid="legal-meta"]')?.textContent).toContain('Sürüm 1.0');
    const titles = Array.from(el.querySelectorAll('.legal__sections > li > h2')).map((h) => h.textContent?.trim());
    expect(titles[0]).toBe(publicPagesTr.privacyPolicy.sections.controller.title);
    expect(titles).toHaveLength(PRIVACY_SECTIONS.length);
    expect(el.querySelectorAll('#rights li')).toHaveLength(9);
    expect(el.textContent).toContain('admin@hedefokul.com');
  });

  it('terms page uses one name: "Kullanım Koşulları"', async () => {
    TestBed.configureTestingModule({
      imports: [TermsComponent, translocoTestingModule({ langs: { 'public-pages/tr': publicPagesTr } })],
      providers: [provideRouter([])],
    });
    const fixture = TestBed.createComponent(TermsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('h1')?.textContent?.trim()).toBe('Kullanım Koşulları');
    expect(el.textContent).not.toContain('Şartlar');
  });
});

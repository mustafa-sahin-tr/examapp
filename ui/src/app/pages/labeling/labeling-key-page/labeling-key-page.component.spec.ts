import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import labelingTr from '../../../../../public/i18n/labeling/tr.json';
import { BookImportService } from '../../../services/book-import.service';
import { LabelingKeyPageComponent } from './labeling-key-page.component';

describe('LabelingKeyPageComponent (#1176)', () => {
  let fixture: ComponentFixture<LabelingKeyPageComponent>;
  const pageImage = vi.fn();
  const el = () => fixture.nativeElement as HTMLElement;
  const dialog = () => el().querySelector<HTMLDialogElement>('dialog')!;
  const button = (label: string) => dialog().querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;

  function create(pages: number[], original = false) {
    fixture = TestBed.createComponent(LabelingKeyPageComponent);
    fixture.componentRef.setInput('jobId', 'j1');
    fixture.componentRef.setInput('pages', pages);
    fixture.componentRef.setInput('original', original);
    fixture.detectChanges();
  }

  beforeEach(() => {
    pageImage.mockReset().mockReturnValue(of(new Blob(['webp'], { type: 'image/webp' })));
    TestBed.configureTestingModule({
      imports: [
        LabelingKeyPageComponent,
        TranslocoTestingModule.forRoot({
          langs: { tr: { labeling: labelingTr } },
          translocoConfig: { availableLangs: ['tr'], defaultLang: 'tr' },
        }),
      ],
      providers: [{ provide: BookImportService, useValue: { pageImage } }],
    });
  });

  afterEach(() => dialog()?.close());

  it('one key page: no paging and no hint', () => {
    create([41]);
    el().querySelector<HTMLButtonElement>('[data-testid="labeling-key-page"]')!.click();
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(dialog().querySelector('.lkp-zoom__hint')).toBeNull();
    expect(button(labelingTr.fix.key.next)).toBeNull();
  });

  it('book key pages (older setup) are paged with a hint', () => {
    create([40, 41]);
    expect(pageImage).toHaveBeenLastCalledWith('j1', 40, false);
    el().querySelector<HTMLButtonElement>('[data-testid="labeling-key-page"]')!.click();
    fixture.detectChanges();

    expect(dialog().querySelector('.lkp-zoom__hint')).not.toBeNull();
    expect(button(labelingTr.fix.key.prev).disabled).toBe(true);
    button(labelingTr.fix.key.next).click();
    fixture.detectChanges();

    expect(pageImage).toHaveBeenLastCalledWith('j1', 41, false);
    expect(dialog().querySelector('.lkp-zoom__title')!.textContent).toContain('2 / 2');
    expect(button(labelingTr.fix.key.next).disabled).toBe(true);
  });

  it('says so when the page cannot be loaded', () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    pageImage.mockReturnValue(throwError(() => new Error('404')));
    create([41]);

    const thumb = el().querySelector<HTMLButtonElement>('[data-testid="labeling-key-page"]')!;
    expect(thumb.disabled).toBe(true);
    expect(thumb.textContent).toContain(labelingTr.fix.key.failed);
  });

  // Issue #1260: düzeltilen sayfanın orijinali; panel modal değil, tuval kullanılabilir kalır.
  it('original: loads the uncleaned page and opens it as a docked, non-modal panel', () => {
    const showModal = vi.spyOn(HTMLDialogElement.prototype, 'showModal');
    create([12], true);

    expect(pageImage).toHaveBeenLastCalledWith('j1', 12, true);
    const thumb = el().querySelector<HTMLButtonElement>('[data-testid="labeling-original-page"]')!;
    expect(thumb.textContent).toContain(labelingTr.fix.original.label);
    thumb.click();
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(showModal).not.toHaveBeenCalled();
    expect(dialog().classList).toContain('lkp-zoom--docked');
    expect(dialog().querySelector('.lkp-zoom__hint')!.textContent).toContain(labelingTr.fix.original.hint);
    expect(dialog().querySelector('.lkp-zoom__title')!.textContent).toContain('s. 12');
  });
});

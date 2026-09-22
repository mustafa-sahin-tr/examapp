import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CompactTestCardComponent } from './compact-test-card.component';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { localeDefinitionOf } from '../../../models/locale';
import { LocaleService } from '../../../services/locale.service';

/** Tarih biçimi tarayıcı diline bağlı kalmasın: aktif dil sabit 'tr'. */
const localeServiceStub = {
  locale: signal('tr' as const).asReadonly(),
  localeDefinition: signal(localeDefinitionOf('tr')).asReadonly(),
};

/** Signal input'lar TestBed'den doğrudan set edilemediği için ince bir host bileşeni kullanılır. */
@Component({
  standalone: true,
  imports: [CompactTestCardComponent],
  template: `
    <app-compact-test-card
      [title]="title()"
      [thumbUrl]="thumbUrl()"
      [dueDate]="dueDate()"
      [progressPercent]="progressPercent()"
      [ariaLabel]="ariaLabel()"
      (activated)="activatedCount = activatedCount + 1"
    ></app-compact-test-card>
  `,
})
class HostComponent {
  readonly title = signal('Matematik Deneme 1');
  readonly thumbUrl = signal<string | null>(null);
  readonly dueDate = signal<Date | string | null>(null);
  readonly progressPercent = signal<number | null>(null);
  readonly ariaLabel = signal<string | null>(null);
  activatedCount = 0;
}

describe('CompactTestCardComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  /** Kartın host elemanı (`role="button"` burada). */
  function card(): HTMLElement {
    return fixture.nativeElement.querySelector('app-compact-test-card') as HTMLElement;
  }

  function query<T extends Element>(selector: string): T | null {
    return card().querySelector<T>(selector);
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HostComponent, translocoTestingModule()],
      providers: [{ provide: LocaleService, useValue: localeServiceStub }],
    });
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  describe('render', () => {
    it('render_TitleGiven_ShowsTitleWithNativeTooltip', () => {
      const title = query<HTMLElement>('.ctc__title');

      expect(title?.textContent?.trim()).toBe('Matematik Deneme 1');
      expect(title?.getAttribute('title')).toBe('Matematik Deneme 1');
    });

    it('render_ThumbUrlNull_ShowsPlaceholderIconWithoutBackgroundImage', () => {
      const thumb = query<HTMLElement>('.ctc__thumb');

      expect(thumb?.querySelector('mat-icon')?.textContent?.trim()).toBe('description');
      expect(thumb?.style.backgroundImage).toBe('');
      expect(thumb?.getAttribute('aria-hidden')).toBe('true');
    });

    it('render_ThumbUrlGiven_SetsBackgroundImageAndHidesPlaceholder', () => {
      host.thumbUrl.set('https://cdn.example/thumb.png');
      fixture.detectChanges();

      const thumb = query<HTMLElement>('.ctc__thumb');

      expect(thumb?.style.backgroundImage).toContain('https://cdn.example/thumb.png');
      expect(thumb?.querySelector('mat-icon')).toBeNull();
    });

    it('render_DueDateNull_DoesNotRenderDueLabel', () => {
      expect(query('.ctc__due')).toBeNull();
    });

    it('render_DueDateIsoString_RendersLocalizedDueLabel', () => {
      const iso = '2026-03-12T10:00:00Z';
      host.dueDate.set(iso);
      fixture.detectChanges();

      const due = query<HTMLElement>('.ctc__due');
      const expectedDate = new Date(iso).toLocaleDateString('tr');

      expect(due?.textContent?.trim()).toBe(`Bitiş: ${expectedDate}`);
    });

    it('render_DueDateAsDateObject_RendersLocalizedDueLabel', () => {
      const date = new Date(2027, 0, 5);
      host.dueDate.set(date);
      fixture.detectChanges();

      expect(query<HTMLElement>('.ctc__due')?.textContent?.trim()).toBe(`Bitiş: ${date.toLocaleDateString('tr')}`);
    });

    it('render_DueDateInvalid_DoesNotRenderDueLabel', () => {
      host.dueDate.set('not-a-date');
      fixture.detectChanges();

      expect(query('.ctc__due')).toBeNull();
    });

    it('render_ProgressPercentNull_DoesNotRenderProgressBadge', () => {
      expect(query('.ctc__progress')).toBeNull();
    });

    it('render_ProgressPercentGiven_RendersRoundedClampedBadge', () => {
      host.progressPercent.set(39.6);
      fixture.detectChanges();
      expect(query<HTMLElement>('.ctc__progress')?.textContent?.trim()).toBe('%40');

      host.progressPercent.set(140);
      fixture.detectChanges();
      expect(query<HTMLElement>('.ctc__progress')?.textContent?.trim()).toBe('%100');

      host.progressPercent.set(0);
      fixture.detectChanges();
      expect(query<HTMLElement>('.ctc__progress')?.textContent?.trim()).toBe('%0');
    });
  });

  describe('accessibility', () => {
    it('host_HasButtonRoleAndTabindex', () => {
      expect(card().getAttribute('role')).toBe('button');
      expect(card().getAttribute('tabindex')).toBe('0');
    });

    it('ariaLabel_NotGiven_DerivedFromTitleDueAndProgress', () => {
      const iso = '2026-03-12T10:00:00Z';
      host.dueDate.set(iso);
      host.progressPercent.set(25);
      fixture.detectChanges();

      expect(card().getAttribute('aria-label')).toBe(
        `Matematik Deneme 1, Bitiş: ${new Date(iso).toLocaleDateString('tr')}, %25`
      );
    });

    it('ariaLabel_OnlyTitle_EqualsTitle', () => {
      expect(card().getAttribute('aria-label')).toBe('Matematik Deneme 1');
    });

    it('ariaLabel_Given_OverridesDerivedLabel', () => {
      host.ariaLabel.set('Özel etiket');
      fixture.detectChanges();

      expect(card().getAttribute('aria-label')).toBe('Özel etiket');
    });
  });

  describe('activated', () => {
    it('click_EmitsActivatedOnce', () => {
      card().click();

      expect(host.activatedCount).toBe(1);
    });

    it('keydownEnter_EmitsActivatedOnce', () => {
      card().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

      expect(host.activatedCount).toBe(1);
    });

    it('keydownSpace_EmitsActivatedOnceAndPreventsDefault', () => {
      const event = new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true });

      card().dispatchEvent(event);

      expect(host.activatedCount).toBe(1);
      expect(event.defaultPrevented).toBeTrue();
    });

    it('keydownOtherKey_DoesNotEmit', () => {
      card().dispatchEvent(new KeyboardEvent('keydown', { key: 'a', bubbles: true }));

      expect(host.activatedCount).toBe(0);
    });
  });
});

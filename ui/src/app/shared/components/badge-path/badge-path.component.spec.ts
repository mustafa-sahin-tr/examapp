import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BadgePathComponent } from './badge-path.component';
import { BadgeThropyItem } from '../badge-thropy/badge-thropy.types';
import { BadgeMedallionState } from '../badge-medallion/badge-state.util';
import { LocaleService } from '../../../services/locale.service';
import { localeDefinitionOf } from '../../../models/locale';
import { translocoTestingModule } from '../../testing/transloco-testing';

function item(id: string, state: BadgeMedallionState, overrides: Partial<BadgeThropyItem> = {}): BadgeThropyItem {
  const earned = state === 'earned' || state === 'new';
  const current = earned ? 10 : state === 'locked' ? 0 : 4;
  return {
    id,
    name: `Rozet ${id}`,
    icon: 'gps_fixed',
    iconUrl: 'achievements/a.svg',
    description: `Açıklama ${id}`,
    currentValue: current,
    targetValue: 10,
    progressPercent: current * 10,
    completedLabel: String(current),
    totalLabel: '10',
    remainingLabel: String(10 - current),
    isCompleted: earned,
    state,
    earnedDateUtc: earned ? '2026-09-12T10:00:00Z' : null,
    pathKey: 'hunter',
    pathName: 'Soru Avcısı',
    pathOrder: Number(id),
    ...overrides,
  };
}

describe('BadgePathComponent (issue #149)', () => {
  let fixture: ComponentFixture<BadgePathComponent>;

  const items = [item('1', 'earned'), item('2', 'new'), item('3', 'in-progress'), item('4', 'locked')];

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function nodes(): HTMLButtonElement[] {
    return Array.from(el().querySelectorAll<HTMLButtonElement>('button.ms-path-node'));
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [BadgePathComponent, translocoTestingModule()],
      providers: [
        {
          provide: LocaleService,
          useValue: { locale: signal('tr').asReadonly(), localeDefinition: signal(localeDefinitionOf('tr')).asReadonly() },
        },
      ],
    });
    fixture = TestBed.createComponent(BadgePathComponent);
    fixture.componentRef.setInput('items', items);
    fixture.detectChanges();
  });

  it('Nodes_AreNativeButtonsInPathOrder_SoTabFollowsThePath', () => {
    const buttons = nodes();
    expect(buttons.length).toBe(4);
    for (const button of buttons) {
      expect(button.getAttribute('type')).toBe('button');
      expect(button.tabIndex).toBe(0);
    }
    expect(buttons.map((b) => b.getAttribute('data-state'))).toEqual(['earned', 'new', 'in-progress', 'locked']);
  });

  it('Nodes_HaveStateAwareAccessibleNames', () => {
    expect(nodes().map((b) => b.getAttribute('aria-label'))).toEqual([
      'Kazanıldı: Rozet 1, 12 Eyl',
      'Yeni kazanıldı: Rozet 2',
      'İlerlemede: Rozet 3, 4/10',
      'Kilitli: Rozet 4, 0/10',
    ]);
  });

  it('Nodes_RenderMedallionWithStateAndVisibleStateText', () => {
    const medallions = Array.from(el().querySelectorAll('app-badge-medallion'));
    expect(medallions.map((m) => m.getAttribute('data-state'))).toEqual(['earned', 'new', 'in-progress', 'locked']);
    expect(medallions.every((m) => m.getAttribute('aria-hidden') === 'true')).toBeTrue();
    expect(nodes()[3].querySelector('.ms-path-node-progress')?.textContent?.trim()).toBe('Kilitli · 0 / 10');
    expect(el().querySelector('img:not(app-badge-medallion img)')).toBeNull();
  });

  // CR U1: seçimin tek kaynağı üst bileşen — panel/vurgu yalnız `selectedId` input'una bağlı.
  it('SelectedId_HighlightsNodeAndShowsSingleDetailPanelWithProgressbarAria', () => {
    fixture.componentRef.setInput('selectedId', '3');
    fixture.detectChanges();

    const third = nodes()[2];
    expect(third.getAttribute('aria-expanded')).toBe('true');
    expect(third.closest('li')?.classList).toContain('ms-path-item-selected');
    expect(nodes().filter((n) => n.getAttribute('aria-expanded') === 'true').length).toBe(1);
    const details = el().querySelectorAll('app-badge-detail.ms-path-detail');
    expect(details.length).toBe(1);
    const detail = details[0] as HTMLElement;
    const bar = detail.querySelector('[role="progressbar"]') as HTMLElement;
    expect(bar.getAttribute('aria-valuenow')).toBe('4');
    expect(bar.getAttribute('aria-valuemin')).toBe('0');
    expect(bar.getAttribute('aria-valuemax')).toBe('10');
    expect(bar.getAttribute('aria-label')).toBe('Rozet 3 ilerlemesi');
    expect(detail.textContent).toContain('Kalan 6 · %40');
  });

  it('SelectedId_AriaControlsPointsToDetailPanels', () => {
    fixture.componentRef.setInput('selectedId', '2');
    fixture.detectChanges();

    const controls = (nodes()[1].getAttribute('aria-controls') ?? '').split(' ');
    expect(controls.length).toBe(2);
    for (const id of controls) {
      expect(el().querySelector(`[id="${id}"]`)).withContext(id).not.toBeNull();
    }
    expect(el().querySelector(`[id="${controls[0]}"]`)?.classList).toContain('ms-path-detail');
    expect(nodes()[0].hasAttribute('aria-controls')).toBeFalse();
  });

  it('SelectedIdOfOtherPathOrNull_NoPanel', () => {
    fixture.componentRef.setInput('selectedId', 'other-path-badge');
    fixture.detectChanges();
    expect(el().querySelector('app-badge-detail')).toBeNull();
    expect(nodes().some((n) => n.getAttribute('aria-expanded') === 'true')).toBeFalse();

    fixture.componentRef.setInput('selectedId', null);
    fixture.detectChanges();
    expect(el().querySelector('app-badge-detail')).toBeNull();
  });

  it('Activate_EmitsIdOnEveryClick_WithoutOwnSelectionState', () => {
    const emitted: string[] = [];
    fixture.componentInstance.badgeSelected.subscribe((id) => emitted.push(id));

    nodes()[0].click();
    nodes()[0].click();
    fixture.detectChanges();

    expect(emitted).toEqual(['1', '1']);
    // Üst bileşen selectedId vermediği sürece panel açılmaz.
    expect(el().querySelector('app-badge-detail')).toBeNull();
  });

  it('KeyboardFocusedNode_NativeButtonActivationEmits', () => {
    // Native <button> Enter/Space'i click'e çevirir; burada odak + click ile aynı yolu doğrularız.
    const emitted: string[] = [];
    fixture.componentInstance.badgeSelected.subscribe((id) => emitted.push(id));
    const second = nodes()[1];
    second.focus();
    expect(document.activeElement).toBe(second);
    second.click();

    expect(emitted).toEqual(['2']);
  });

  it('Layout_SlotWidthLimitsNodeWidth', () => {
    const track = el().querySelector('.ms-single-path-track') as HTMLElement;
    expect(track.style.getPropertyValue('--slot-width')).toBe('22%');
  });

  it('Segments_EarnedNextAndPendingStatuses', () => {
    const statuses = Array.from(el().querySelectorAll('path.ms-path-segment')).map((p) => p.getAttribute('data-status'));
    // 1→2 (2 kazanıldı) earned, 2→3 (sıradaki) next, 3→4 pending (kesikli)
    expect(statuses).toEqual(['earned', 'next', 'pending']);
  });

  it('NoHardcodedGradientOrArrowGlyphs', () => {
    expect(el().querySelector('linearGradient')).toBeNull();
    expect(el().textContent).not.toContain('→');
  });

  it('EmptyItems_ShowsEmptyState', () => {
    fixture.componentRef.setInput('items', []);
    fixture.detectChanges();

    expect(nodes().length).toBe(0);
    expect(el().textContent).toContain('Rozet yolu bulunamadı.');
  });
});

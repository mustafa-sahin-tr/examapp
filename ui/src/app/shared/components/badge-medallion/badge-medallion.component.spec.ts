import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BadgeMedallionComponent, snapMedallionSize } from './badge-medallion.component';
import { BadgeMedallionState } from './badge-state.util';

@Component({
  standalone: true,
  imports: [BadgeMedallionComponent],
  template: `<app-badge-medallion
    [icon]="icon()"
    [iconUrl]="iconUrl()"
    [state]="state()"
    [size]="size()"
    [progressPercent]="progress()"
  />`,
})
class HostComponent {
  readonly icon = signal<string | null>('gps_fixed');
  readonly iconUrl = signal<string | null>(null);
  readonly state = signal<BadgeMedallionState>('earned');
  readonly size = signal(56);
  readonly progress = signal<number | null>(40);
}

describe('BadgeMedallionComponent (issue #149)', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  function medallion(): HTMLElement {
    return fixture.nativeElement.querySelector('app-badge-medallion') as HTMLElement;
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HostComponent] });
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('Host_IsAriaHiddenAndExposesStateAndSize', () => {
    expect(medallion().getAttribute('aria-hidden')).toBe('true');
    expect(medallion().getAttribute('data-state')).toBe('earned');
    expect(medallion().style.getPropertyValue('--bm-size')).toBe('56px');
  });

  it('ValidIcon_RendersMaterialSymbolsGlyph_FilledWhenEarned', () => {
    const icon = medallion().querySelector('.bm__icon') as HTMLElement;
    expect(icon.textContent?.trim()).toBe('gps_fixed');
    expect(icon.classList).toContain('material-symbols-outlined');
    expect(icon.classList).toContain('bm__icon--filled');
    expect(medallion().querySelector('img')).toBeNull();
  });

  it('InProgress_GlyphIsOutline', () => {
    host.state.set('in-progress');
    fixture.detectChanges();
    expect(medallion().querySelector('.bm__icon')?.classList).not.toContain('bm__icon--filled');
  });

  it('NoIcon_LegacyIconUrl_RendersRootRelativeImage', () => {
    host.icon.set(null);
    host.iconUrl.set('achievements/a.svg');
    fixture.detectChanges();

    const img = medallion().querySelector('img') as HTMLImageElement;
    expect(img.getAttribute('src')).toBe('/achievements/a.svg');
    expect(img.getAttribute('alt')).toBe('');
  });

  it('ImageLoadError_FallsBackToMilitaryTech', () => {
    host.icon.set(null);
    host.iconUrl.set('/achievements/missing.svg');
    fixture.detectChanges();

    medallion().querySelector('img')!.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(medallion().querySelector('img')).toBeNull();
    expect(medallion().querySelector('.bm__icon')?.textContent?.trim()).toBe('military_tech');
  });

  it('NothingUsable_FallsBackToMilitaryTech', () => {
    host.icon.set('Bad Name');
    host.iconUrl.set('https://evil.example/achievements/a.svg');
    fixture.detectChanges();

    expect(medallion().querySelector('img')).toBeNull();
    expect(medallion().querySelector('.bm__icon')?.textContent?.trim()).toBe('military_tech');
  });

  const cornerCases: Array<[BadgeMedallionState, string | null]> = [
    ['earned', 'check'],
    ['new', 'auto_awesome'],
    ['locked', 'lock'],
    ['in-progress', null],
  ];
  for (const [state, glyph] of cornerCases) {
    it(`CornerGlyph_${state}_Is_${glyph ?? 'none'}`, () => {
      host.state.set(state);
      fixture.detectChanges();
      const corner = medallion().querySelector('[data-testid="badge-medallion-corner"]');
      expect(corner?.textContent?.trim() ?? null).toBe(glyph);
    });
  }

  it('CornerGlyph_HiddenAt32pxAndBelow', () => {
    host.size.set(32);
    fixture.detectChanges();
    expect(medallion().querySelector('[data-testid="badge-medallion-corner"]')).toBeNull();
    expect(medallion().classList).toContain('badge-medallion--compact');

    host.size.set(40);
    fixture.detectChanges();
    expect(medallion().querySelector('[data-testid="badge-medallion-corner"]')).not.toBeNull();
  });

  it('ProgressRing_OnlyInProgressAndAtLeast56px', () => {
    host.state.set('in-progress');
    host.size.set(56);
    fixture.detectChanges();
    const ring = medallion().querySelector('[data-testid="badge-medallion-ring"]') as HTMLElement;
    expect(ring).not.toBeNull();
    expect(ring.style.getPropertyValue('--bm-progress')).toBe('40%');

    host.size.set(44);
    fixture.detectChanges();
    expect(medallion().querySelector('[data-testid="badge-medallion-ring"]')).toBeNull();

    host.size.set(64);
    host.state.set('locked');
    fixture.detectChanges();
    expect(medallion().querySelector('[data-testid="badge-medallion-ring"]')).toBeNull();
  });

  it('ProgressRing_ClampsOutOfRangeValues', () => {
    host.state.set('in-progress');
    host.progress.set(140);
    fixture.detectChanges();
    const ring = medallion().querySelector('[data-testid="badge-medallion-ring"]') as HTMLElement;
    expect(ring.style.getPropertyValue('--bm-progress')).toBe('100%');
  });

  it('snapMedallionSize_SnapsToDesignScale', () => {
    expect(snapMedallionSize(10)).toBe(24);
    expect(snapMedallionSize(45)).toBe(44);
    expect(snapMedallionSize(60)).toBe(56);
    expect(snapMedallionSize(200)).toBe(72);
    expect(snapMedallionSize('48')).toBe(48);
    expect(snapMedallionSize(null)).toBe(56);
    expect(snapMedallionSize(Number.NaN)).toBe(56);
  });
});

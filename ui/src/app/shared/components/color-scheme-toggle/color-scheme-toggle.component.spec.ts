import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MatSlideToggle } from '@angular/material/slide-toggle';

import { ColorSchemeToggleComponent } from './color-scheme-toggle.component';
import { ColorScheme, ColorSchemeService } from '../../../services/color-scheme.service';

describe('ColorSchemeToggleComponent', () => {
  let component: ColorSchemeToggleComponent;
  let fixture: ComponentFixture<ColorSchemeToggleComponent>;
  let colorSchemeServiceSpy: jasmine.SpyObj<ColorSchemeService>;
  let schemeSignal: ReturnType<typeof signal<ColorScheme>>;

  beforeEach(async () => {
    schemeSignal = signal<ColorScheme>('dark');
    colorSchemeServiceSpy = jasmine.createSpyObj<ColorSchemeService>('ColorSchemeService', [
      'setScheme',
      'toggle',
    ]);
    Object.defineProperty(colorSchemeServiceSpy, 'colorScheme', {
      value: schemeSignal.asReadonly(),
    });

    await TestBed.configureTestingModule({
      imports: [ColorSchemeToggleComponent],
      providers: [{ provide: ColorSchemeService, useValue: colorSchemeServiceSpy }],
    }).compileComponents();

    fixture = TestBed.createComponent(ColorSchemeToggleComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('isLight_CurrentSchemeIsDark_ReturnsFalse', () => {
    expect(component.isLight()).toBeFalse();
  });

  it('isLight_CurrentSchemeIsLight_ReturnsTrue', () => {
    schemeSignal.set('light');
    fixture.detectChanges();

    expect(component.isLight()).toBeTrue();
  });

  it('tooltip_CurrentSchemeIsDark_SuggestsSwitchingToLight', () => {
    expect(component.tooltip()).toBe('Açık temaya geç');
  });

  it('tooltip_CurrentSchemeIsLight_SuggestsSwitchingToDark', () => {
    schemeSignal.set('light');
    fixture.detectChanges();

    expect(component.tooltip()).toBe('Koyu temaya geç');
  });

  it('toggle_Called_DelegatesToColorSchemeServiceToggle', () => {
    component.toggle();

    expect(colorSchemeServiceSpy.toggle).toHaveBeenCalledTimes(1);
    expect(colorSchemeServiceSpy.setScheme).not.toHaveBeenCalled();
  });

  it('slideToggleChange_UserInteractsWithSlideToggle_CallsComponentToggle', () => {
    const slideToggleDebugEl = fixture.debugElement.query(By.directive(MatSlideToggle));
    const slideToggle = slideToggleDebugEl.componentInstance as MatSlideToggle;

    slideToggle.change.emit({ source: slideToggle, checked: true } as never);

    expect(colorSchemeServiceSpy.toggle).toHaveBeenCalledTimes(1);
  });

  it('slideToggle_CurrentSchemeIsDark_HasAriaLabelReflectingSwitchToLight', () => {
    const btn = fixture.nativeElement.querySelector('mat-slide-toggle button') as HTMLElement;

    expect(btn.getAttribute('aria-label')).toBe('Açık temaya geç');
  });

  it('slideToggle_CurrentSchemeIsLight_HasAriaLabelReflectingSwitchToDark', () => {
    schemeSignal.set('light');
    fixture.detectChanges();

    const btn = fixture.nativeElement.querySelector('mat-slide-toggle button') as HTMLElement;

    expect(btn.getAttribute('aria-label')).toBe('Koyu temaya geç');
  });
});

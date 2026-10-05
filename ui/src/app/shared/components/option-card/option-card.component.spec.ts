import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { OptionCardComponent } from './option-card.component';
import { OptionGroupComponent } from './option-group.component';

interface Opt {
  value: string;
  label: string;
  disabled?: boolean;
}

@Component({
  standalone: true,
  imports: [OptionCardComponent, OptionGroupComponent],
  template: `
    <app-option-group [multi]="multi()" labelledBy="q">
      @for (o of options(); track o.value) {
        <button
          app-option-card
          icon="timer"
          [label]="o.label"
          [selected]="selected().includes(o.value)"
          [multi]="multi()"
          [disabled]="!!o.disabled"
          (click)="toggle(o.value)"
        ></button>
      }
    </app-option-group>
  `,
})
class HostComponent {
  readonly multi = signal(false);
  readonly options = signal<Opt[]>([
    { value: 'a', label: 'Süreli' },
    { value: 'b', label: '12' },
    { value: 'c', label: 'Soru' },
  ]);
  readonly selected = signal<string[]>([]);

  toggle(value: string): void {
    if (!this.multi()) {
      this.selected.set([value]);
      return;
    }
    this.selected.update((s) => (s.includes(value) ? s.filter((v) => v !== value) : [...s, value]));
  }
}

describe('OptionCardComponent + OptionGroupComponent (issue #135)', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    el = fixture.nativeElement;
    document.body.appendChild(el);
    fixture.detectChanges();
  });

  afterEach(() => el.remove());

  const cards = (): HTMLButtonElement[] => Array.from(el.querySelectorAll<HTMLButtonElement>('button[app-option-card]'));
  const group = (): HTMLElement => el.querySelector('app-option-group')!;

  function key(target: HTMLElement, k: string): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  it('single_RendersRadioRolesAndNativeButton', () => {
    expect(group().getAttribute('role')).toBe('radiogroup');
    expect(group().getAttribute('aria-labelledby')).toBe('q');
    const [first] = cards();
    expect(first.getAttribute('type')).toBe('button');
    expect(first.getAttribute('role')).toBe('radio');
    expect(first.getAttribute('aria-checked')).toBe('false');
    expect(first.querySelector('mat-icon')?.getAttribute('fontSet')).toBe('material-symbols-outlined');
  });

  it('single_RovingTabindexFollowsSelection', () => {
    expect(cards().map((c) => c.getAttribute('tabindex'))).toEqual(['0', '-1', '-1']);
    cards()[2].click();
    fixture.detectChanges();
    expect(cards()[2].getAttribute('aria-checked')).toBe('true');
    expect(cards()[2].classList).toContain('option-card--selected');
    expect(cards().map((c) => c.getAttribute('tabindex'))).toEqual(['-1', '-1', '0']);
  });

  it('arrowKeys_MoveFocusWithoutSelecting_AndWrap', () => {
    cards()[0].focus();
    const event = key(cards()[0], 'ArrowRight');
    expect(event.defaultPrevented).toBeTrue();
    expect(document.activeElement).toBe(cards()[1]);
    expect(host.selected()).toEqual([]);
    expect(cards()[1].getAttribute('tabindex')).toBe('0');

    key(cards()[1], 'ArrowLeft');
    expect(document.activeElement).toBe(cards()[0]);
    key(cards()[0], 'ArrowUp');
    expect(document.activeElement).toBe(cards()[2]);
    key(cards()[2], 'Home');
    expect(document.activeElement).toBe(cards()[0]);
    key(cards()[0], 'End');
    expect(document.activeElement).toBe(cards()[2]);
  });

  it('arrowKeys_SkipDisabledCards', () => {
    host.options.update((o) => o.map((x) => (x.value === 'b' ? { ...x, disabled: true } : x)));
    fixture.detectChanges();
    cards()[0].focus();
    key(cards()[0], 'ArrowDown');
    expect(document.activeElement).toBe(cards()[2]);
  });

  it('otherKeys_AreNotIntercepted', () => {
    cards()[0].focus();
    expect(key(cards()[0], ' ').defaultPrevented).toBeFalse();
    expect(key(cards()[0], 'Enter').defaultPrevented).toBeFalse();
  });

  it('multi_UsesCheckboxRolesAndAllCardsTabbable', () => {
    host.multi.set(true);
    fixture.detectChanges();
    expect(group().getAttribute('role')).toBe('group');
    expect(cards()[0].getAttribute('role')).toBe('checkbox');
    expect(cards().every((c) => !c.hasAttribute('tabindex'))).toBeTrue();
    expect(cards()[0].classList).toContain('option-card--multi');

    cards()[0].click();
    cards()[1].click();
    fixture.detectChanges();
    expect(cards().map((c) => c.getAttribute('aria-checked'))).toEqual(['true', 'true', 'false']);
  });

  it('numericLabel_GetsNumericClass', () => {
    expect(cards()[1].classList).toContain('option-card--numeric');
    expect(cards()[0].classList).not.toContain('option-card--numeric');
  });
});

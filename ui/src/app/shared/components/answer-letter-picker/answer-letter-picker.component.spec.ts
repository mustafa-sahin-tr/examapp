import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { AnswerLetterPickerComponent } from './answer-letter-picker.component';

@Component({
  standalone: true,
  imports: [AnswerLetterPickerComponent],
  template: `<app-answer-letter-picker
    [count]="count()"
    [index]="3"
    [disabled]="disabled()"
    ariaLabel="Harf"
    (picked)="picked.set($event)"
  />`,
})
class HostComponent {
  readonly count = signal(4);
  readonly disabled = signal(false);
  readonly picked = signal<number | null>(null);
}

describe('AnswerLetterPickerComponent (#1183)', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideNoopAnimations()],
    }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  const trigger = () =>
    fixture.nativeElement.querySelector('[data-testid="answer-letter-picker"]') as HTMLButtonElement;
  const items = () => Array.from(document.querySelectorAll<HTMLButtonElement>('.alp-menu [mat-menu-item]'));

  it('pick_ListsALetterPerOption_DisablesTheCurrentOne_AndEmitsThePickedPosition', async () => {
    trigger().click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(items().map((b) => b.textContent?.trim())).toEqual(['A', 'B', 'C', 'D']);
    expect(items()[3].disabled).toBe(true);

    items()[1].click();
    expect(fixture.componentInstance.picked()).toBe(1);
  });

  it('pick_DisabledOrSingleOption_TriggerIsDisabled', () => {
    fixture.componentInstance.disabled.set(true);
    fixture.detectChanges();
    expect(trigger().disabled).toBe(true);

    fixture.componentInstance.disabled.set(false);
    fixture.componentInstance.count.set(1);
    fixture.detectChanges();
    expect(trigger().disabled).toBe(true);
  });
});

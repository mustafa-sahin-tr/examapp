import { Directive, HostListener, inject } from '@angular/core';
import { NgControl } from '@angular/forms';

import { formatParentPhoneInput } from '../utils/parent-contact.util';

/**
 * Gezinti notu (K1): veli telefonu girişini yazarken `05xx xxx xx xx` biçimine sokar (bkz. {@link formatParentPhoneInput}).
 * Reaktif form kontrolüyle birlikte kullanılır; değer biçimli haliyle forma yazılır, doğrulama aynı kalır.
 */
@Directive({ selector: 'input[appPhoneMask]', standalone: true })
export class PhoneMaskDirective {
  private readonly control = inject(NgControl, { self: true });

  @HostListener('input', ['$event.target'])
  onInput(target: HTMLInputElement): void {
    const formatted = formatParentPhoneInput(target.value);
    if (formatted === target.value) return;
    target.value = formatted;
    this.control.control?.setValue(formatted);
  }
}

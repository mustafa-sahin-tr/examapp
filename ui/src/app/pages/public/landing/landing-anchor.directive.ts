import { DOCUMENT, Directive, HostListener, computed, inject, input } from '@angular/core';

/**
 * `<base href="/">` yüzünden düz `href="#x"` ana adrese gider ve sayfayı yeniden yükler.
 * Bu yönerge gerçek bir bağlantı üretir (yeni sekme çalışır), tıklamada aynı sayfada kaydırır.
 */
@Directive({
  selector: 'a[appLandingAnchor]',
  standalone: true,
  host: { '[attr.href]': 'href()' },
})
export class LandingAnchorDirective {
  private readonly document = inject(DOCUMENT);

  readonly appLandingAnchor = input.required<string>();
  readonly href = computed(() => `/welcome#${this.appLandingAnchor()}`);

  @HostListener('click', ['$event'])
  onClick(event: MouseEvent): void {
    const target = this.document.getElementById(this.appLandingAnchor());
    if (!target) return;
    event.preventDefault();
    target.scrollIntoView({ behavior: 'smooth' });
  }
}

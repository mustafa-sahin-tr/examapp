// src/app/shared/directives/autofocus.directive.ts
import { Directive, ElementRef, AfterViewInit } from '@angular/core';

@Directive({ selector: '[appAutofocus]' , standalone: true})
export class AutofocusDirective implements AfterViewInit {
  constructor(private host: ElementRef<HTMLInputElement>) {}

  ngAfterViewInit() {
    // a zero‑delay timeout ensures the browser has actually inserted it
    setTimeout(() => this.host.nativeElement.focus(), 0);
  }
}

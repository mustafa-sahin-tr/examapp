import { Component, ElementRef, HostListener, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import { LanguageSwitcherComponent } from '../../../../shared/components/language-switcher/language-switcher.component';

@Component({
  selector: 'app-navbar',
  imports: [RouterLink, TranslocoDirective, LanguageSwitcherComponent],
  templateUrl: './navbar.component.html',
  styleUrl: './navbar.component.scss',
})
export class NavbarComponent {
  isMenuOpen = false;
  isSticky = false;
  isRegisterOpen = false;

  toggleMenu() {
    this.isMenuOpen = !this.isMenuOpen;
  }

  closeMenu() {
    this.isMenuOpen = false;
    this.isRegisterOpen = false;
  }

  private host = inject(ElementRef<HTMLElement>);

  toggleRegister(event: Event) {
    event.preventDefault();
    this.isRegisterOpen = !this.isRegisterOpen;
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (this.isRegisterOpen && !this.host.nativeElement.contains(event.target as Node)) {
      this.isRegisterOpen = false;
    }
  }

  @HostListener('window:scroll')
  onScroll() {
    this.isSticky = window.scrollY > 80;
  }
}

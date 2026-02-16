import { Component, HostListener, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './landing.component.html',
  styleUrls: ['./landing.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush, // Performans için
})
export class LandingComponent {
  isScrolled = false;
  appName = 'Hedef Okul';

  @HostListener('window:scroll', [])
  onWindowScroll() {
    // Sayfa 80px aşağı kaydırıldığında navbar değişsin
    this.isScrolled = window.scrollY > 80;
  }
}

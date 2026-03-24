import { Component } from '@angular/core';
import { CarouselModule, OwlOptions } from 'ngx-owl-carousel-o'

@Component({
  selector: 'app-overview',
  imports: [CarouselModule],
  templateUrl: './overview.component.html',
  styleUrls: ['./overview.component.scss'],
  standalone: true,
})
export class OverviewComponent {
  customOptions: OwlOptions = {
    loop: true,
    mouseDrag: true,
    touchDrag: true,
    pullDrag: false,
    dots: true, // Altındaki noktalar için
    navSpeed: 700,
    navText: ['<i class="ri-arrow-left-line"></i>', '<i class="ri-arrow-right-line"></i>'],
    responsive: {
      0: { items: 1 },
      576: { items: 2 },
      768: { items: 2 },
      992: { items: 3 },
      1200: { items: 4 }
    },
    nav: false // İstersen true yapıp okları gösterebilirsin
  };

  // HTML'deki verileri bir diziye alırsan daha temiz olur
  slidesStore = [
    { id: '1', img: 'assets/images/courses/courses1.jpg', title: 'Adobe Illustrator', category: 'Design', price: '$49' },
    { id: '2', img: 'assets/images/courses/courses2.jpg', title: 'Java Programming', category: 'Development', price: '$59' },
    // Diğer kursları buraya ekle...
  ];
}

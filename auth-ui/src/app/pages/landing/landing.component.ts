import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatDividerModule } from '@angular/material/divider';
import { MatChipsModule } from '@angular/material/chips';
import { Router } from '@angular/router';
import { trigger, transition, style, animate, query, stagger } from '@angular/animations';

interface Feature {
  icon: string;
  title: string;
  description: string;
  color: string;
}

interface Testimonial {
  name: string;
  role: string;
  comment: string;
  avatar: string;
  rating: number;
}

interface Statistic {
  value: string;
  label: string;
  icon: string;
}

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatToolbarModule,
    MatDividerModule,
    MatChipsModule,
  ],
  templateUrl: './landing.component.html',
  styleUrl: './landing.component.scss',
  animations: [
    trigger('fadeInUp', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(30px)' }),
        animate('0.8s ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('staggerAnimation', [
      transition('* => *', [
        query(
          ':enter',
          [
            style({ opacity: 0, transform: 'translateY(30px)' }),
            stagger(200, [animate('0.6s ease-out', style({ opacity: 1, transform: 'translateY(0)' }))]),
          ],
          { optional: true }
        ),
      ]),
    ]),
  ],
})
export class LandingComponent implements OnInit {
  currentYear = new Date().getFullYear();

  features: Feature[] = [
    {
      icon: 'quiz',
      title: 'Akıllı Test Sistemi',
      description: 'AI destekli soru analizi ile öğrencilerin seviyesine uygun testler oluşturun.',
      color: 'blue',
    },
    {
      icon: 'analytics',
      title: 'Detaylı Analiz',
      description: 'Öğrenci performansını gerçek zamanlı takip edin ve raporlar alın.',
      color: 'green',
    },
    {
      icon: 'security',
      title: 'Güvenli Platform',
      description: 'Keycloak entegrasyonu ile güvenli ve ölçeklenebilir kullanıcı yönetimi.',
      color: 'purple',
    },
    {
      icon: 'cloud',
      title: 'Bulut Tabanlı',
      description: 'Her yerden erişim, otomatik yedekleme ve sınırsız depolama.',
      color: 'orange',
    },
    {
      icon: 'group',
      title: 'Sınıf Yönetimi',
      description: 'Öğrencilerinizi organize edin, sınıflar oluşturun ve ilerlemelerini takip edin.',
      color: 'teal',
    },
    {
      icon: 'school',
      title: 'Eğitim Odaklı',
      description: 'Pedagojik prensipler doğrultusunda tasarlanmış kullanıcı deneyimi.',
      color: 'red',
    },
  ];

  testimonials: Testimonial[] = [
    {
      name: 'Ayşe Yılmaz',
      role: 'Matematik Öğretmeni',
      comment: 'ExamApp sayesinde öğrencilerimin matematik başarısı %40 arttı. Harika bir platform!',
      avatar: '👩‍🏫',
      rating: 5,
    },
    {
      name: 'Mehmet Demir',
      role: 'Fen Bilimleri Öğretmeni',
      comment:
        'Soru analizi özelliği gerçekten çok işime yarıyor. Hangi konularda eksik olduklarını hemen görebiliyorum.',
      avatar: '👨‍🔬',
      rating: 5,
    },
    {
      name: 'Fatma Kaya',
      role: 'İlkokul Öğretmeni',
      comment: 'Kullanımı çok kolay ve öğrenciler de çok seviyor. Aileler de sürekli takip edebiliyor.',
      avatar: '👩‍🎓',
      rating: 4,
    },
  ];

  statistics: Statistic[] = [
    {
      value: '10,000+',
      label: 'Aktif Öğrenci',
      icon: 'school',
    },
    {
      value: '500+',
      label: 'Öğretmen',
      icon: 'person',
    },
    {
      value: '50,000+',
      label: 'Çözülen Test',
      icon: 'quiz',
    },
    {
      value: '98%',
      label: 'Memnuniyet',
      icon: 'thumb_up',
    },
  ];

  pricingPlans = [
    {
      name: 'Temel',
      price: 'Ücretsiz',
      duration: '',
      features: ['5 öğrenci kapasitesi', 'Temel test oluşturma', 'Basit raporlama', 'Email desteği'],
      popular: false,
      color: 'blue',
    },
    {
      name: 'Profesyonel',
      price: '99',
      duration: '/ay',
      features: [
        '50 öğrenci kapasitesi',
        'Gelişmiş analitik',
        'Sınıf yönetimi',
        'Öncelikli destek',
        'AI destekli soru analizi',
      ],
      popular: true,
      color: 'purple',
    },
    {
      name: 'Kurumsal',
      price: 'Özel',
      duration: '',
      features: ['Sınırsız öğrenci', 'Tüm özellikler', 'Özel entegrasyon', '7/24 destek', 'Eğitim ve danışmanlık'],
      popular: false,
      color: 'orange',
    },
  ];

  isScrolled = signal(false);

  constructor(private router: Router) {}

  ngOnInit() {
    // Scroll event listener for header background change
    if (typeof window !== 'undefined') {
      window.addEventListener('scroll', () => {
        this.isScrolled.set(window.scrollY > 50);
      });
    }
  }

  navigateToLogin() {
    this.router.navigate(['/auth/login']);
  }

  navigateToRegister() {
    this.router.navigate(['/auth/register']);
  }

  scrollToSection(sectionId: string) {
    const element = document.getElementById(sectionId);
    if (element) {
      element.scrollIntoView({ behavior: 'smooth' });
    }
  }

  getStars(rating: number): number[] {
    return Array(5)
      .fill(0)
      .map((_, i) => (i < rating ? 1 : 0));
  }
}

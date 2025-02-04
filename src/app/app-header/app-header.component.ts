import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
@Component({
  selector: 'app-header',
  standalone: true,
  templateUrl: './app-header.component.html',
  styleUrls: ['./app-header.component.scss'],
  imports: [
    MatToolbarModule,
    MatIconModule,
    MatButtonModule,
    MatMenuModule,
    MatCardModule,
    CommonModule
  ]
})
export class AppHeaderComponent {
  profileImage = 'assets/profile.png';  
  userRole: string | null = null;
  isAuthenticated = false;

  constructor(private authService: AuthService, private cdr: ChangeDetectorRef,
    private router: Router
  ) {
    this.authService.isAuthenticated$.subscribe(status => {
      this.isAuthenticated = status;
      //this.cdr.detectChanges(); 
    });
  }

  navigateTo(path: string) {
    this.router.navigate([path]);
  }

  logout() {
    this.authService.logout();
  }
}

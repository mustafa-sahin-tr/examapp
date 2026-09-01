import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { ClassifierCacheStatus } from '../../../models/taxonomy';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/components/confirm-dialog/confirm-dialog.component';

@Component({
  selector: 'app-classifier-cache',
  standalone: true,
  templateUrl: './classifier-cache.component.html',
  styleUrls: ['./classifier-cache.component.scss'],
  imports: [
    CommonModule,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressBarModule,
    MatDialogModule,
    MatSnackBarModule,
  ],
})
export class ClassifierCacheComponent implements OnInit {
  private readonly admin = inject(AdminService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);

  readonly loading = signal(false);
  readonly refreshing = signal(false);
  readonly status = signal<ClassifierCacheStatus | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.admin.getClassifierCache().subscribe({
      next: (s) => {
        this.status.set(s);
        this.loading.set(false);
      },
      error: () => {
        this.snack.open('Cache durumu alınamadı', 'Kapat', { duration: 4000 });
        this.loading.set(false);
      },
    });
  }

  async refresh(): Promise<void> {
    const data: ConfirmDialogData = {
      title: 'Sınıflandırma cache’ini yenile',
      message:
        'Mevcut ders/konu/alt konu taksonomisinden yeni bir Gemini cached content oluşturulacak ve aktif hale gelecek. Devam edilsin mi?',
      confirmText: 'Yenile',
      icon: 'refresh',
      confirmColor: 'primary',
    };
    const ok = await firstValueFrom(this.dialog.open(ConfirmDialogComponent, { data }).afterClosed());
    if (!ok) return;

    this.refreshing.set(true);
    try {
      const res = await firstValueFrom(this.admin.refreshClassifierCache());
      this.snack.open(res.message, 'Kapat', { duration: 4000 });
      this.load();
    } catch (err: any) {
      this.snack.open(err?.error?.message ?? 'Yenileme başarısız', 'Kapat', { duration: 5000 });
    } finally {
      this.refreshing.set(false);
    }
  }
}

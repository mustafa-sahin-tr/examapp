import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, OnDestroy } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';

import { ThemeConfigService, ThemePreset, WorksheetCardThemeConfig } from '../../services/theme-config.service';
import { UserThemeService } from '../../services/user-theme.service';

@Component({
  selector: 'app-user-theme-switcher',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatCardModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatFormFieldModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    FormsModule,
  ],
  template: `
    <mat-card class="user-theme-card">
      <mat-card-header>
        <mat-card-title>🎨 Kişisel Tema Ayarları</mat-card-title>
        <mat-card-subtitle>Seçiminiz kaydedilecek ve bir dahaki girişinizde hatırlanacak</mat-card-subtitle>
      </mat-card-header>

      <mat-card-content>
        <div *ngIf="loading" class="loading-container">
          <mat-spinner diameter="40"></mat-spinner>
          <p>Tema yükleniyor...</p>
        </div>

        <div *ngIf="!loading">
          <!-- Mevcut Durum -->
          <div class="current-theme-info" *ngIf="currentUserTheme">
            <h4>📌 Kaydedilmiş Tema Durumunuz</h4>
            <p>
              <strong>Preset:</strong> {{ getPresetDisplayName(currentUserTheme.themePreset) }}
              <span *ngIf="currentUserTheme.themeCustomConfig" class="custom-badge">+ Özel Ayarlar</span>
            </p>
            <p class="hint">💡 "Mevcut Durumum" seçeneğini seçerek bu ayarları düzenleyebilirsiniz.</p>
            <div class="divider"></div>
          </div>

          <!-- Preset Seçimi -->
          <mat-form-field appearance="outline" class="full-width">
            <mat-label>Tema Yükle veya Seç</mat-label>
            <mat-select [(value)]="selectedPreset" (selectionChange)="onPresetChange($event.value)">
              <mat-option value="current" *ngIf="currentUserTheme">
                <div class="current-option">
                  <strong>👤 Mevcut Durumum</strong>
                  <small>{{ getCurrentThemeDescription() }}</small>
                </div>
              </mat-option>
              <mat-option value="divider-1" disabled>──────────────────</mat-option>
              <mat-option value="minimal">🔳 Minimal - Sadece Renkli Çerçeveler</mat-option>
              <mat-option value="standard">⭐ Standard - Çerçeve + Gradient + İkonlar + İlerleme</mat-option>
              <mat-option value="enhanced">✨ Enhanced - Standard + Ribbon + Işıltı + Typography</mat-option>
              <mat-option value="full">🚀 Full - Tüm Efektler (Animasyonlar Dahil)</mat-option>
            </mat-select>
          </mat-form-field>

          <!-- Özel Ayarlar -->
          <div class="custom-settings">
            <h4>🔧 Özel Ayarlar</h4>
            <p class="settings-note">Bu ayarları değiştirdiğinizde özel bir tema oluşturmuş olursunuz.</p>

            <div class="settings-grid">
              <div class="setting-item">
                <mat-slide-toggle [(ngModel)]="currentTheme.borders" (change)="onCustomChange()" color="primary">
                  🖼️ Renkli Çerçeveler
                </mat-slide-toggle>
                <span class="setting-description">Duruma göre renkli border'lar</span>
              </div>

              <div class="setting-item">
                <mat-slide-toggle [(ngModel)]="currentTheme.gradient" (change)="onCustomChange()" color="primary">
                  🌈 Gradient Overlay
                </mat-slide-toggle>
                <span class="setting-description">Arka plan gradient efektleri</span>
              </div>

              <div class="setting-item">
                <mat-slide-toggle [(ngModel)]="currentTheme.iconBadges" (change)="onCustomChange()" color="primary">
                  🏷️ İkon Rozetleri
                </mat-slide-toggle>
                <span class="setting-description">Sağ üst köşe durum ikonları</span>
              </div>

              <div class="setting-item">
                <mat-slide-toggle [(ngModel)]="currentTheme.ribbons" (change)="onCustomChange()" color="primary">
                  🎗️ Ribbon Banner
                </mat-slide-toggle>
                <span class="setting-description">Sol üst köşe atama türü ribbon'u</span>
              </div>

              <div class="setting-item">
                <mat-slide-toggle [(ngModel)]="currentTheme.glowEffects" (change)="onCustomChange()" color="primary">
                  ✨ Işıltı Efektleri
                </mat-slide-toggle>
                <span class="setting-description">Glow ve shadow efektleri</span>
              </div>

              <div class="setting-item">
                <mat-slide-toggle [(ngModel)]="currentTheme.progressBar" (change)="onCustomChange()" color="primary">
                  📊 İlerleme Çubuğu
                </mat-slide-toggle>
                <span class="setting-description">Alt kısımda zaman ilerlemesi</span>
              </div>

              <div class="setting-item">
                <mat-slide-toggle
                  [(ngModel)]="currentTheme.transformEffects"
                  (change)="onCustomChange()"
                  color="primary"
                >
                  🔄 Transform Animasyonları
                </mat-slide-toggle>
                <span class="setting-description">Döndürme, titreme animasyonları</span>
              </div>

              <div class="setting-item">
                <mat-slide-toggle
                  [(ngModel)]="currentTheme.typographyEffects"
                  (change)="onCustomChange()"
                  color="primary"
                >
                  📝 Typography Efektleri
                </mat-slide-toggle>
                <span class="setting-description">Font ağırlığı ve şeffaflık değişiklikleri</span>
              </div>
            </div>
          </div>
        </div>
      </mat-card-content>

      <mat-card-actions>
        <button mat-raised-button color="warn" (click)="resetToDefault()" [disabled]="loading">
          🔄 Varsayılana Dön
        </button>
        <button mat-raised-button color="accent" (click)="showPreview()" [disabled]="loading">👁️ Önizleme</button>
        <button mat-raised-button color="primary" (click)="saveTheme()" [disabled]="loading">
          💾 Kaydet ve Uygula
        </button>
      </mat-card-actions>
    </mat-card>
  `,
  styles: [
    `
      .user-theme-card {
        max-width: 600px;
        margin: 20px auto;
      }

      .loading-container {
        display: flex;
        flex-direction: column;
        align-items: center;
        padding: 40px;
        gap: 16px;
      }

      .current-theme-info {
        background: #f5f5f5;
        padding: 16px;
        border-radius: 8px;
        margin-bottom: 20px;
      }

      .current-theme-info h4 {
        margin-top: 0;
        color: #333;
      }

      .custom-badge {
        background: #4caf50;
        color: white;
        padding: 2px 8px;
        border-radius: 12px;
        font-size: 11px;
        margin-left: 8px;
      }

      .hint {
        font-size: 12px;
        color: #666;
        margin-top: 8px;
        font-style: italic;
      }

      .divider {
        height: 1px;
        background: #ddd;
        margin: 16px 0;
      }

      .full-width {
        width: 100%;
        margin-bottom: 24px;
      }

      .current-option {
        display: flex;
        flex-direction: column;
        gap: 2px;
      }

      .current-option small {
        font-size: 11px;
        color: #666;
        font-weight: normal;
      }

      .custom-settings h4 {
        margin-bottom: 8px;
        color: #333;
      }

      .settings-note {
        font-size: 14px;
        color: #666;
        margin-bottom: 20px;
        font-style: italic;
      }

      .settings-grid {
        display: grid;
        gap: 16px;
      }

      .setting-item {
        display: flex;
        flex-direction: column;
        gap: 4px;
        padding: 12px;
        border: 1px solid #eee;
        border-radius: 8px;
        transition: border-color 0.3s ease;
      }

      .setting-item:hover {
        border-color: #ddd;
      }

      .setting-description {
        font-size: 12px;
        color: #666;
        margin-left: 32px;
      }

      mat-card-actions {
        display: flex;
        gap: 12px;
        justify-content: flex-end;
        flex-wrap: wrap;
      }

      mat-card-actions button {
        min-width: 140px;
      }

      @media (max-width: 600px) {
        .user-theme-card {
          margin: 10px;
        }

        mat-card-actions {
          justify-content: center;
        }

        mat-card-actions button {
          min-width: auto;
          flex: 1;
        }
      }
    `,
  ],
})
export class UserThemeSwitcherComponent implements OnInit, OnDestroy {
  private readonly userThemeService = inject(UserThemeService);
  private readonly themeConfigService = inject(ThemeConfigService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroy$ = new Subject<void>();

  loading = true;
  selectedPreset: ThemePreset | 'current' = 'standard';
  currentTheme: WorksheetCardThemeConfig = this.themeConfigService.getCurrentTheme();
  currentUserTheme: { themePreset: string; themeCustomConfig?: string | null } | null = null;

  ngOnInit(): void {
    // User theme service'den mevcut tema bilgisini al
    this.userThemeService.userTheme$.pipe(takeUntil(this.destroy$)).subscribe((userTheme) => {
      this.currentUserTheme = userTheme;
      if (userTheme) {
        // User'ın kaydedilmiş teması var, bunu "current" olarak işaretle
        this.selectedPreset = 'current';
        this.currentTheme = this.themeConfigService.getCurrentTheme();
      } else {
        // User'ın kaydedilmiş teması yok, varsayılan preset'i detect et
        this.detectCurrentPreset();
      }
      this.loading = false;
    });

    // Theme service'den güncel tema bilgisini al
    this.currentTheme = this.themeConfigService.getCurrentTheme();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onPresetChange(preset: ThemePreset | 'current' | 'divider-1'): void {
    // selectedPreset sadece gerçek preset'ler için güncellensin
    if (preset !== 'current' && preset !== 'divider-1') {
      this.selectedPreset = preset;
    }

    // "Mevcut Durumum" seçildiyse mevcut ayarları yükle
    if (preset === 'current' && this.currentUserTheme) {
      this.loadCurrentUserTheme();
      return;
    }

    // Divider seçildiyse hiçbir şey yapma
    if (preset === 'divider-1') {
      return;
    }

    this.themeConfigService.setTheme(preset as ThemePreset);
    this.currentTheme = this.themeConfigService.getCurrentTheme();

    // this.loading = true;
    // this.userThemeService.selectPreset(preset as ThemePreset).subscribe({
    //   next: () => {
    //     this.currentTheme = this.themeConfigService.getCurrentTheme();
    //     this.loading = false;
    //     this.snackBar.open(`✅ ${this.getPresetDisplayName(preset)} teması uygulandı!`, 'Tamam', {
    //       duration: 4000,
    //     });

    //     // Sayfayı refresh et ki değişiklik görünsün
    //     // setTimeout(() => window.location.reload(), 1000);
    //   },
    //   error: (error) => {
    //     this.loading = false;
    //     console.error('Theme save error:', error);
    //     this.snackBar.open('❌ Tema kaydedilemedi. Lütfen tekrar deneyin.', 'Tamam', {
    //       duration: 4000,
    //     });
    //   },
    // });
  }

  onCustomChange(): void {
    // Kullanıcı özel ayarlarda değişiklik yaptı, artık "current" modunda
    this.selectedPreset = 'current';

    // Özel tema değişikliği yapmak biraz zaman alsın ki kullanıcı çok hızlı değiştirmesin
    setTimeout(() => {
      this.saveCustomTheme();
    }, 300);
  }

  private saveCustomTheme(): void {
    this.loading = true;

    this.userThemeService.selectCustomTheme(this.currentTheme).subscribe({
      next: () => {
        this.loading = false;
        this.selectedPreset = 'current'; // Artık custom tema mevcut durumunuz
        this.snackBar.open('✅ Özel tema ayarlarınız kaydedildi ve mevcut durumunuz oldu!', 'Tamam', {
          duration: 4000,
        });
      },
      error: (error) => {
        this.loading = false;
        console.error('Custom theme save error:', error);
        this.snackBar.open('❌ Özel tema kaydedilemedi.', 'Tamam', {
          duration: 4000,
        });
      },
    });
  }

  saveTheme(): void {
    // Manuel kaydetme - mevcut ayarları kaydet ve uygula
    this.loading = true;

    // Current seçiliyse veya gerçek preset değilse custom tema olarak kaydet
    if (this.selectedPreset === 'current') {
      this.saveCustomTheme();
      return;
    }

    const isCustomTheme = !this.isThemeMatchingPreset(this.currentTheme, this.selectedPreset as ThemePreset);

    if (isCustomTheme) {
      this.saveCustomTheme();
    } else {
      this.onPresetChange(this.selectedPreset);
    }
  }

  resetToDefault(): void {
    this.loading = true;

    this.userThemeService.resetUserTheme().subscribe({
      next: () => {
        this.currentTheme = this.themeConfigService.getCurrentTheme();
        this.selectedPreset = 'standard';
        this.loading = false;
        this.snackBar.open('🔄 Tema varsayılan ayarlara döndürüldü!', 'Tamam', {
          duration: 4000,
        });

        setTimeout(() => window.location.reload(), 1000);
      },
      error: (error) => {
        this.loading = false;
        console.error('Theme reset error:', error);
        this.snackBar.open('❌ Tema sıfırlanamadı.', 'Tamam', {
          duration: 4000,
        });
      },
    });
  }

  showPreview(): void {
    // Önizleme - sadece theme config service'i güncelle, kaydetme
    this.themeConfigService.setCustomTheme(this.currentTheme);
    this.snackBar.open('👁️ Önizleme uygulandı! Kaydet butonuna basarak kalıcı hale getirebilirsiniz.', 'Tamam', {
      duration: 5000,
    });
  }

  getPresetDisplayName(preset: string): string {
    const names: { [key: string]: string } = {
      minimal: 'Minimal',
      standard: 'Standard',
      enhanced: 'Enhanced',
      full: 'Full',
      current: 'Mevcut Durumum',
    };
    return names[preset] || preset;
  }

  private detectCurrentPreset(): void {
    const presets = this.themeConfigService.getAvailablePresets();

    for (const preset of presets) {
      if (this.isThemeMatchingPreset(this.currentTheme, preset)) {
        this.selectedPreset = preset;
        return;
      }
    }

    // Hiçbir preset ile eşleşmiyorsa custom tema
    this.selectedPreset = 'standard';
  }

  getCurrentThemeDescription(): string {
    if (!this.currentUserTheme) return '';

    const preset = this.currentUserTheme.themePreset;
    const hasCustom = this.currentUserTheme.themeCustomConfig;

    if (hasCustom) {
      return `${this.getPresetDisplayName(preset)} + Özel Ayarlar`;
    } else {
      return `${this.getPresetDisplayName(preset)} Preset`;
    }
  }

  loadCurrentUserTheme(): void {
    if (!this.currentUserTheme) return;

    this.loading = true;

    // Eğer custom config varsa onu yükle, yoksa preset'i yükle
    if (this.currentUserTheme.themeCustomConfig) {
      try {
        const customConfig = JSON.parse(this.currentUserTheme.themeCustomConfig);
        this.currentTheme = customConfig;
        this.themeConfigService.setCustomTheme(customConfig);
        this.selectedPreset = 'current'; // Artık düzenleme modunda
        this.snackBar.open('✅ Kaydedilmiş özel tema ayarlarınız yüklendi ve düzenleyebilirsiniz!', 'Tamam', {
          duration: 4000,
        });
      } catch (error) {
        console.warn('Invalid custom theme config:', error);
        // Custom config bozuksa preset'i yükle
        this.themeConfigService.setTheme(this.currentUserTheme.themePreset as ThemePreset);
        this.currentTheme = this.themeConfigService.getCurrentTheme();
        this.selectedPreset = 'current';
        this.snackBar.open('✅ Kaydedilmiş preset tema yüklendi ve düzenleyebilirsiniz!', 'Tamam', {
          duration: 4000,
        });
      }
    } else {
      // Sadece preset varsa
      this.themeConfigService.setTheme(this.currentUserTheme.themePreset as ThemePreset);
      this.currentTheme = this.themeConfigService.getCurrentTheme();
      this.selectedPreset = 'current';
      this.snackBar.open('✅ Kaydedilmiş preset tema yüklendi ve düzenleyebilirsiniz!', 'Tamam', {
        duration: 4000,
      });
    }

    this.loading = false;
  }

  private isThemeMatchingPreset(theme: WorksheetCardThemeConfig, preset: ThemePreset): boolean {
    const presetConfig = this.themeConfigService.getPresetConfig(preset);
    return Object.keys(theme).every(
      (key) => theme[key as keyof WorksheetCardThemeConfig] === presetConfig[key as keyof WorksheetCardThemeConfig]
    );
  }
}

import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { FormsModule } from '@angular/forms';
import { ThemeConfigService, ThemePreset, WorksheetCardThemeConfig } from '../../services/theme-config.service';

@Component({
  selector: 'app-theme-switcher',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatCardModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatFormFieldModule,
    FormsModule,
  ],
  template: `
    <mat-card class="theme-switcher-card">
      <mat-card-header>
        <mat-card-title>🎨 Worksheet Card Theme</mat-card-title>
        <mat-card-subtitle>Tema ayarlarını canlı olarak değiştirin</mat-card-subtitle>
      </mat-card-header>

      <mat-card-content>
        <!-- Preset Seçimi -->
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Hazır Tema</mat-label>
          <mat-select [(value)]="selectedPreset" (selectionChange)="onPresetChange($event.value)">
            <mat-option value="minimal">🔳 Minimal - Sadece Border</mat-option>
            <mat-option value="standard">⭐ Standard - Border + Gradient + Icons + Progress</mat-option>
            <mat-option value="enhanced">✨ Enhanced - Standard + Ribbon + Glow + Typography</mat-option>
            <mat-option value="full">🚀 Full - Tüm Efektler</mat-option>
          </mat-select>
        </mat-form-field>

        <!-- Özel Ayarlar -->
        <div class="custom-settings">
          <h3>Özel Ayarlar</h3>

          <div class="setting-row">
            <mat-slide-toggle [(ngModel)]="currentTheme.borders" (change)="onCustomChange()">
              🖼️ Border Efektleri
            </mat-slide-toggle>
          </div>

          <div class="setting-row">
            <mat-slide-toggle [(ngModel)]="currentTheme.gradient" (change)="onCustomChange()">
              🌈 Gradient Overlay
            </mat-slide-toggle>
          </div>

          <div class="setting-row">
            <mat-slide-toggle [(ngModel)]="currentTheme.iconBadges" (change)="onCustomChange()">
              🏷️ Icon Badges
            </mat-slide-toggle>
          </div>

          <div class="setting-row">
            <mat-slide-toggle [(ngModel)]="currentTheme.ribbons" (change)="onCustomChange()">
              🎗️ Ribbon/Banner
            </mat-slide-toggle>
          </div>

          <div class="setting-row">
            <mat-slide-toggle [(ngModel)]="currentTheme.glowEffects" (change)="onCustomChange()">
              ✨ Glow/Shadow Efektleri
            </mat-slide-toggle>
          </div>

          <div class="setting-row">
            <mat-slide-toggle [(ngModel)]="currentTheme.transformEffects" (change)="onCustomChange()">
              🔄 Transform Animasyonları
            </mat-slide-toggle>
          </div>

          <div class="setting-row">
            <mat-slide-toggle [(ngModel)]="currentTheme.progressBar" (change)="onCustomChange()">
              📊 Progress Bar
            </mat-slide-toggle>
          </div>

          <div class="setting-row">
            <mat-slide-toggle [(ngModel)]="currentTheme.typographyEffects" (change)="onCustomChange()">
              📝 Typography Efektleri
            </mat-slide-toggle>
          </div>
        </div>
      </mat-card-content>

      <mat-card-actions>
        <button mat-raised-button color="warn" (click)="resetToDefault()">🔄 Varsayılana Dön</button>
        <button mat-raised-button color="primary" (click)="saveTheme()">💾 Kaydet</button>
      </mat-card-actions>
    </mat-card>
  `,
  styles: [
    `
      .theme-switcher-card {
        max-width: 500px;
        margin: 20px auto;
      }

      .full-width {
        width: 100%;
        margin-bottom: 20px;
      }

      .custom-settings {
        margin-top: 20px;
      }

      .custom-settings h3 {
        margin-bottom: 15px;
        color: #333;
      }

      .setting-row {
        margin-bottom: 10px;
        display: flex;
        align-items: center;
      }

      mat-card-actions {
        display: flex;
        gap: 10px;
        justify-content: flex-end;
      }
    `,
  ],
})
export class ThemeSwitcherComponent {
  private readonly themeService = inject(ThemeConfigService);

  selectedPreset: ThemePreset = 'standard';
  currentTheme: WorksheetCardThemeConfig;

  constructor() {
    this.currentTheme = { ...this.themeService.getCurrentTheme() };
    this.detectCurrentPreset();
  }

  onPresetChange(preset: ThemePreset): void {
    this.selectedPreset = preset;
    this.themeService.setTheme(preset);
    this.currentTheme = { ...this.themeService.getCurrentTheme() };
    this.refreshPage();
  }

  onCustomChange(): void {
    debugger;
    this.themeService.setCustomTheme(this.currentTheme);
    this.selectedPreset = 'standard'; // Custom olduğu için preset'i sıfırla
    this.refreshPage();
  }

  resetToDefault(): void {
    this.themeService.resetToDefault();
    this.currentTheme = { ...this.themeService.getCurrentTheme() };
    this.selectedPreset = 'standard';
    this.refreshPage();
  }

  saveTheme(): void {
    // Tema zaten otomatik kaydediliyor, sadece kullanıcıya feedback
    alert("✅ Tema kaydedildi! Değişiklikler localStorage'a yazıldı.");
  }

  private detectCurrentPreset(): void {
    const presets = this.themeService.getAvailablePresets();

    for (const preset of presets) {
      const presetConfig = this.themeService.getPresetConfig(preset);
      if (this.isThemeEqual(this.currentTheme, presetConfig)) {
        this.selectedPreset = preset;
        return;
      }
    }

    // Hiçbir preset ile eşleşmiyorsa custom
    this.selectedPreset = 'standard';
  }

  private isThemeEqual(theme1: WorksheetCardThemeConfig, theme2: WorksheetCardThemeConfig): boolean {
    return Object.keys(theme1).every(
      (key) => theme1[key as keyof WorksheetCardThemeConfig] === theme2[key as keyof WorksheetCardThemeConfig]
    );
  }

  private refreshPage(): void {
    // Sayfayı yenile ki değişiklikler görünsün
    setTimeout(() => {
      window.location.reload();
    }, 100);
  }
}

# 🎨 Worksheet Card Theme System

Bu sistemle worksheet card'ların görsel efektlerini environment, konfigürasyon veya canlı olarak değiştirebilirsiniz.

## 🚀 Kullanım

### 1. Environment ile Tema Ayarlama

**Development (`environment.ts`):**

```typescript
export const environment = {
  production: false,
  apiUrl: 'http://localhost:5079/api',
  worksheetCardTheme: 'enhanced', // 'minimal' | 'standard' | 'enhanced' | 'full'
};
```

**Production (`environment.prod.ts`):**

```typescript
export const environment = {
  production: true,
  apiUrl: 'http://exam_dotnet_8_api:8005/api',
  worksheetCardTheme: 'standard', // Production'da daha performanslı
};
```

### 2. Runtime'da Tema Değiştirme

**Browser Console'dan:**

```javascript
// Preset kullanma
window.WORKSHEET_CARD_THEME_PRESET = 'full';
location.reload();

// Özel konfigürasyon
window.WORKSHEET_CARD_THEME = JSON.stringify({
  borders: true,
  gradient: true,
  iconBadges: false,
  ribbons: true,
  glowEffects: false,
  transformEffects: false,
  progressBar: true,
  typographyEffects: true,
});
location.reload();
```

**Service ile:**

```typescript
import { ThemeConfigService } from './services/theme-config.service';

constructor(private themeService: ThemeConfigService) {
  // Preset değiştirme
  this.themeService.setTheme('full');

  // Özel ayarlar
  this.themeService.setCustomTheme({
    borders: true,
    gradient: false,
    glowEffects: true
  });
}
```

## 📊 Tema Presetleri

### 🔳 **Minimal**

- ✅ Borders
- ❌ Tüm diğer efektler
- **Kullanım:** Performans odaklı, minimal tasarım

### ⭐ **Standard**

- ✅ Borders
- ✅ Gradient Overlay
- ✅ Icon Badges
- ✅ Progress Bar
- ❌ Ribbon, Glow, Transform, Typography
- **Kullanım:** Production ortamı için ideal

### ✨ **Enhanced**

- ✅ Standard +
- ✅ Ribbons
- ✅ Glow Effects
- ✅ Typography Effects
- ❌ Transform Effects
- **Kullanım:** Zengin görsel deneyim, orta performans

### 🚀 **Full**

- ✅ Tüm efektler aktif
- **Kullanım:** Demo, showcase, maximum görsel etki

## 🎛️ Özellik Detayları

| Özellik             | Açıklama                             | Performans Etkisi |
| ------------------- | ------------------------------------ | ----------------- |
| `borders`           | Renkli border'lar + pulse animasyonu | ⚡ Düşük          |
| `gradient`          | Background gradient overlay          | ⚡ Düşük          |
| `iconBadges`        | Sağ üst köşe icon'ları               | ⚡ Düşük          |
| `ribbons`           | Sol üst diagonal ribbon              | ⚡ Düşük          |
| `glowEffects`       | Box-shadow glow efektleri            | ⚡⚡ Orta         |
| `progressBar`       | Alt progress çubuğu                  | ⚡ Düşük          |
| `transformEffects`  | Döndürme, titreme, scale             | ⚡⚡⚡ Yüksek     |
| `typographyEffects` | Font ağırlığı, opacity               | ⚡ Düşük          |

## 🔧 Development Araçları

### Theme Switcher Component

Dashboard'da canlı tema değiştirme için:

```typescript
// dashboard.component.html
<app-theme-switcher></app-theme-switcher>
```

### Debug Metodları

```typescript
// Mevcut temayı göster
console.log(this.themeService.getCurrentTheme());

// Varsayılana dön
this.themeService.resetToDefault();

// Kullanılabilir presetleri listele
console.log(this.themeService.getAvailablePresets());
```

## 🏗️ Özelleştirme

### Yeni Preset Ekleme

`ThemeConfigService`'te `themePresets` objesine yeni preset ekleyin:

```typescript
newPreset: {
  borders: true,
  gradient: false,
  iconBadges: true,
  ribbons: false,
  glowEffects: true,
  transformEffects: false,
  progressBar: true,
  typographyEffects: false
}
```

### CSS Özelleştirme

`worksheet-card.component.scss`'te yeni renkler veya animasyonlar ekleyebilirsiniz.

## 📱 Responsive Davranış

- **Mobile:** Otomatik olarak daha minimal efektler
- **Tablet:** Standard preset önerilen
- **Desktop:** Enhanced/Full presetler kullanılabilir

## ⚡ Performans Önerileri

- **Production:** `standard` preset
- **Development:** `enhanced` preset
- **Demo/Showcase:** `full` preset
- **Mobile/Low-end devices:** `minimal` preset
- **High-performance needed:** Sadece `borders` + `iconBadges`

## 🔄 Migration Guide

Eski statik sistemden yeni tema sistemine geçiş:

1. Environment dosyalarına `worksheetCardTheme` ekleyin
2. `ThemeConfigService`'i component'lere inject edin
3. HTML template'lerinde tema koşullarını kontrol edin
4. CSS'te tema-specific class'ları kullanın

## 🐛 Troubleshooting

**Tema değişiklikleri görünmüyor:**

- Sayfayı yenileyin (tema değişiklikleri restart gerektirir)
- Browser cache'ini temizleyin
- localStorage'ı kontrol edin: `localStorage.getItem('worksheetCardTheme')`

**Performans sorunları:**

- `full` preset yerine `standard` kullanın
- `transformEffects`'i kapatın
- Browser dev tools ile animasyon performansını kontrol edin

# User Theme Integration with Student/Teacher Profiles

Theme bilgisi artık Student ve Teacher entity'lerinin bir parçasıdır. Ayrı API endpoint'ler yerine mevcut authentication flow'u içinde entegre edilmiştir.

## Architecture

### Database Schema

Theme bilgisi doğrudan Student ve Teacher tablolarında saklanır:

- `ThemePreset`: VARCHAR(20) - tema tipi ('minimal', 'standard', 'enhanced', 'full')
- `ThemeCustomConfig`: TEXT - custom tema config (JSON format)

### API Endpoints

#### Student Theme Update

**POST** `/api/exam/student/update-theme`

**Request:**

```json
{
  "themePreset": "enhanced",
  "themeCustomConfig": "{\"borders\":true,\"gradient\":false,...}"
}
```

**Response:**

```json
{
  "themePreset": "enhanced",
  "themeCustomConfig": "{\"borders\":true,\"gradient\":false,...}"
}
```

#### Teacher Theme Update

**POST** `/api/exam/teacher/update-theme`

Same request/response format as Student.

### Authentication Flow Integration

Theme bilgisi authentication refresh sırasında otomatik olarak gelir:

**POST** `/api/exam/auth/refresh`

**Response:**

```json
{
  "id": 123,
  "fullName": "John Doe",
  "role": "Student",
  "student": {
    "id": 456,
    "themePreset": "enhanced",
    "themeCustomConfig": "{\"borders\":true,...}",
    ...
  }
}
```

### Frontend Integration

1. **UserThemeService**: AuthService ile entegre, user profile'dan tema bilgisini alır
2. **APP_INITIALIZER**: Uygulama başlangıcında tema otomatik yüklenir
3. **ThemeConfigService**: Tema konfigürasyonunu worksheet card'lara uygular
4. **UserThemeSwitcher**: Student profile sayfasında tema seçimi UI'ı

### Benefits of This Approach

1. **Reduced API Calls**: Tema bilgisi authentication refresh ile gelir
2. **Simplified State Management**: Theme bilgisi user profile'ın parçası
3. **Better Performance**: No separate theme loading requests
4. **Consistency**: Theme bilgisi her zaman user bilgileri ile senkron

### Implementation Summary

✅ **Backend:**

- Student.cs ve Teacher.cs entity'lerine theme fields eklendi
- StudentService.UpdateStudentTheme() ve TeacherService.UpdateTeacherTheme() metodları eklendi
- AuthController.refresh() theme bilgisini Student/Teacher DTO'larına dahil ediyor
- StudentController ve TeacherController'da theme update endpoint'leri

✅ **Frontend:**

- UserThemeService AuthService ile entegre
- Theme bilgisi user profile'dan otomatik yükleniyor
- Student profile sayfasında theme switcher UI'ı
- APP_INITIALIZER ile tema uygulama başlangıcında yükleniyor

### Next Steps

1. Database migration oluştur (Student ve Teacher tablolarına theme kolonları)
2. Frontend'de test et
3. User experience iyileştirmeleri (loading states, error handling)

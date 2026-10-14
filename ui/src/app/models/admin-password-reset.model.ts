/**
 * Issue #156 — `POST /api/exam/admin/{teachers|students}/{id}/reset-password` başarılı yanıtı
 * (backend `AdminPasswordResetResponseDto`). Gövde yok; `{id}` admin listelerindeki Teacher.Id / Student.Id.
 *
 * Geçici şifre YALNIZCA bu yanıtta bir kez döner. UI'da sadece sonuç dialog'unun yerel state'inde tutulur;
 * servis, store, localStorage, router state veya console'a yazılmaz.
 */
export interface AdminPasswordResetResponse {
  temporaryPassword: string;
  /**
   * Issue #438: true = veli tarafından açılmış çocuk hesabı; şifre KALICI (ilk girişte değiştirme zorunluluğu yok), admin
   * şifreyi veliye iletir. Alan yoksa (eski yanıt) geçici şifre kabul edilir.
   */
  permanent?: boolean;
}

/** Şifresi sıfırlanacak hesabın türü; URL segmentini belirler. */
export type AdminPasswordResetTarget = 'teacher' | 'student';

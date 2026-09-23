/**
 * Issue #155 — `PATCH /api/exam/admin/{teachers|students}/{id}/account-status` gövdesi ve başarılı yanıtı
 * (backend `AdminAccountStatusRequestDto` / `AdminAccountStatusResponseDto`). `{id}` admin listelerindeki
 * Teacher.Id / Student.Id. İdempotenttir: hesap zaten istenen durumdaysa da 200 döner.
 */
export interface AdminAccountStatusRequest {
  enabled: boolean;
}

export interface AdminAccountStatusResponse {
  enabled: boolean;
}

/** Durumu değiştirilecek hesabın türü; URL segmentini belirler. */
export type AdminAccountTarget = 'teacher' | 'student';
